using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Employes;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v63 — Mise à jour des fiches employés depuis le fichier RH de référence.
///
/// 1. <see cref="AnalyserAsync"/> lit le classeur (toutes les colonnes, + N° BOX des feuilles « LISTE BOX »),
///    contrôle chaque valeur, rapproche chaque ligne d'une fiche par le nom normalisé et calcule, champ par
///    champ, ce qui changerait. Rien n'est écrit.
/// 2. <see cref="AppliquerAsync"/> applique les lignes identifiées et celles que la personne a confirmées :
///    une valeur vide du fichier ne vide jamais une fiche, une valeur refusée n'est pas appliquée, chaque
///    changement est journalisé (avant / après) et poussé vers l'annuaire (source du profil RH).
/// 3. <see cref="AnnulerAsync"/> remet les valeurs d'avant, sauf celles modifiées depuis par quelqu'un d'autre.
/// </summary>
public sealed class FichesReferenceService(
    AppDbContext db,
    IUserService users,
    IDirectoryEmployeeWriteClient annuaire,
    IMemoryCache cache,
    ILogger<FichesReferenceService> logger,
    IServiceScopeFactory? scopeFactory = null,
    FichesReferenceSuivi? suivi = null) : IFichesReferenceService
{
    private const long TailleMax = 15 * 1024 * 1024;
    private const int LignesMax = 5000;
    private static readonly TimeSpan DureeAnalyse = TimeSpan.FromHours(1);
    private const string ChampCreation = "__creation__";
    private const string ChampDesactivation = "__desactivation__";
    private const string ChampDateEmbaucheCompte = "user.hireDate";

    private sealed record LigneLue(int Numero, FicheReference.Valeurs Valeurs);

    private sealed record Classeur(
        string Feuille,
        List<ColonneFichierDto> Reconnues,
        List<string> NonReprises,
        List<string> FeuillesBox,
        List<LigneLue> Lignes);

    private sealed record AnalyseEnCache(
        string Fichier,
        Dictionary<int, FicheReference.Valeurs> Valeurs,
        Dictionary<int, FicheReferenceRapprochement.Resultat> Rapprochement);

    private sealed record AgentApp(
        int Id, string FirstName, string LastName, bool IsActive, DateTime HireDate, string Role, string? Service, UserHrProfile? Profil);

    // ── Analyse ──────────────────────────────────────────────────────────────────────────────

    public async Task<AnalyseFichesReferenceDto> AnalyserAsync(Stream fichier, string nomFichier, CancellationToken ct = default)
    {
        using var copie = new MemoryStream();
        await fichier.CopyToAsync(copie, ct);
        if (copie.Length == 0) throw new InvalidOperationException("Le fichier est vide.");
        if (copie.Length > TailleMax) throw new InvalidOperationException("Le fichier dépasse 15 Mo.");
        copie.Position = 0;

        var aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);
        nomFichier = Court(Path.GetFileName(nomFichier), 255) ?? "fichier.xlsx";
        var classeur = LireClasseur(copie, aujourdhui);
        if (classeur.Lignes.Count > LignesMax)
            throw new InvalidOperationException($"Fichier trop volumineux ({classeur.Lignes.Count} lignes, maximum {LignesMax}).");

        var agents = await ChargerAgentsAsync(ct);
        var rapprochement = FicheReferenceRapprochement.Rapprocher(
            classeur.Lignes.Select(l => new FicheReferenceRapprochement.Ligne(
                l.Numero, l.Valeurs.Agents, l.Valeurs.NumeroOrdre, l.Valeurs.Sorti(aujourdhui), l.Valeurs.DateEmbauche ?? l.Valeurs.DateAnciennete)).ToList(),
            agents.Select(a => new FicheReferenceRapprochement.Agent(
                a.Id, a.LastName, a.FirstName, a.Profil?.ImmatriculationInterne, a.IsActive)).ToList());

        var parId = agents.ToDictionary(a => a.Id);
        var valeurs = classeur.Lignes.ToDictionary(l => l.Numero, l => l.Valeurs);
        var lignes = new List<LigneFicheReferenceDto>();
        foreach (var r in rapprochement)
        {
            var v = valeurs[r.Numero];
            var agent = r.UserId is int id && parId.TryGetValue(id, out var a) ? a : null;
            var avertissements = new List<string>(v.Avertissements);
            List<FicheReference.Modification> modifs = [];
            List<string> manquants = [];
            if (agent is not null)
            {
                modifs = FicheReference.Comparer(agent.Profil, v);
                manquants = FicheReference.ChampsManquants(Simuler(agent.Profil, modifs));
                if (ModifDateEmbaucheCompte(agent.HireDate, v) is { } compte) modifs.Add(compte);
                avertissements.AddRange(AvertissementsEtat(agent, v, aujourdhui));
            }
            else if (r.Statut == FicheReferenceRapprochement.Nouveau)
            {
                manquants = FicheReference.ChampsManquants(v);
            }

            var choix = r.Statut switch
            {
                FicheReferenceRapprochement.Identifie when r.UserId is int u => $"user:{u}",
                FicheReferenceRapprochement.Nouveau => "creer",
                _ => "ignorer",
            };
            lignes.Add(new LigneFicheReferenceDto(
                r.Numero,
                v.Agents,
                v.NumeroOrdre,
                r.Statut,
                r.Message,
                r.UserId,
                agent is null ? null : $"{agent.LastName} {agent.FirstName}".Trim(),
                agent?.IsActive ?? false,
                v.Sorti(aujourdhui),
                v.DateSortie,
                r.Statut is FicheReferenceRapprochement.Nouveau or FicheReferenceRapprochement.Ambigu or FicheReferenceRapprochement.AVerifier
                    ? FicheReference.RoleSuggere(v.Poste) : null,
                choix,
                r.Candidats.Select(c => new CandidatFicheDto(c.UserId, c.Nom, c.Prenom, c.Matricule, c.Actif, c.Raison)).ToList(),
                modifs.Select(VersDto).ToList(),
                v.Anomalies.ToList(),
                avertissements,
                manquants));
        }

        // Employés actifs de l'application que le fichier ne cite pas (ni identifiés, ni proposés).
        var cites = rapprochement
            .SelectMany(r => r.Candidats.Select(c => c.UserId).Append(r.UserId ?? 0))
            .ToHashSet();
        var absents = agents
            .Where(a => a.IsActive && !cites.Contains(a.Id) && !EstCompteTechnique(a.Role))
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new AgentAbsentDto(a.Id, a.LastName, a.FirstName, a.Profil?.ImmatriculationInterne, a.Service))
            .ToList();

        var jeton = Guid.NewGuid().ToString("N");
        cache.Set(CleCache(jeton), new AnalyseEnCache(nomFichier, valeurs, rapprochement.ToDictionary(r => r.Numero)), DureeAnalyse);

        int Nb(string statut) => lignes.Count(l => l.Statut == statut);
        var identifies = lignes.Where(l => l.Statut == FicheReferenceRapprochement.Identifie).ToList();
        var compteurs = new CompteursAnalyseFichesDto(
            classeur.Lignes.Count,
            identifies.Count,
            identifies.Count(l => l.Modifications.Count > 0),
            identifies.Count(l => l.Modifications.Count == 0),
            Nb(FicheReferenceRapprochement.Nouveau),
            Nb(FicheReferenceRapprochement.AVerifier),
            Nb(FicheReferenceRapprochement.Ambigu),
            Nb(FicheReferenceRapprochement.Historique),
            Nb(FicheReferenceRapprochement.SortiInconnu),
            Nb(FicheReferenceRapprochement.Ignore),
            lignes.Count(l => l.Anomalies.Count > 0),
            lignes.Count(l => l.ChampsManquants.Count > 0 && !l.Sorti
                              && l.Statut is FicheReferenceRapprochement.Identifie or FicheReferenceRapprochement.Nouveau),
            identifies.Sum(l => l.Modifications.Count));

        var manquantes = FicheReference.ColonnesManquantes(classeur.Reconnues.Select(r => r.Cle).ToList());
        var maintenant = DateTimeOffset.UtcNow;
        return new AnalyseFichesReferenceDto(
            jeton, nomFichier, classeur.Feuille, maintenant, maintenant.Add(DureeAnalyse),
            classeur.Reconnues, manquantes, classeur.NonReprises, classeur.FeuillesBox,
            compteurs, lignes, absents);
    }

    private static IEnumerable<string> AvertissementsEtat(AgentApp agent, FicheReference.Valeurs v, DateOnly aujourdhui)
    {
        if (!agent.IsActive && !v.Sorti(aujourdhui))
            yield return "Fiche désactivée dans l'application alors que l'employé est actif dans le fichier (pas de réactivation automatique).";
        if (agent.IsActive && v.Sorti(aujourdhui))
            yield return $"Sorti le {v.DateSortie:dd/MM/yyyy} selon le fichier, toujours actif dans l'application.";
        if (agent.Profil?.DateSortie is DateOnly s && v.DateSortie is null && !v.Sorti(aujourdhui))
            yield return $"La fiche porte une date de sortie ({s:dd/MM/yyyy}) absente du fichier : à vérifier (elle n'est pas effacée).";
    }

    // ── Application ──────────────────────────────────────────────────────────────────────────

    public Task<ApplicationFichesReferenceDto> AppliquerAsync(
        AppliquerFichesReferenceRequest demande, string auteur, CancellationToken ct = default) =>
        ExecuterAsync(PrendreAnalyse(demande), demande, Court(auteur, 160) ?? "inconnu", Guid.NewGuid(), null, ct);

    /// <summary>L'analyse validée à l'écran ; elle ne s'applique qu'une fois (pas de double import par double clic).</summary>
    private AnalyseEnCache PrendreAnalyse(AppliquerFichesReferenceRequest demande)
    {
        if (string.IsNullOrWhiteSpace(demande.Jeton)
            || !cache.TryGetValue(CleCache(demande.Jeton), out AnalyseEnCache? analyse) || analyse is null)
            throw new InvalidOperationException("L'analyse a expiré ou est introuvable : relancez l'analyse du fichier.");
        cache.Remove(CleCache(demande.Jeton));
        return analyse;
    }

    public SuiviApplicationFichesDto DemarrerApplication(AppliquerFichesReferenceRequest demande, string auteur, string? autorisation,
        JournalAuteurExterne? acteurJournal = null)
    {
        if (scopeFactory is null || suivi is null)
            throw new InvalidOperationException("Application en arrière-plan indisponible.");
        if (string.IsNullOrWhiteSpace(demande.Jeton) || !cache.TryGetValue(CleCache(demande.Jeton), out AnalyseEnCache? apercu) || apercu is null)
            throw new InvalidOperationException("L'analyse a expiré ou est introuvable : relancez l'analyse du fichier.");

        var importId = Guid.NewGuid();
        var etat = suivi.Demarrer(importId, apercu.Fichier, apercu.Rapprochement.Count)
                   ?? throw new InvalidOperationException("Une mise à jour des fiches est déjà en cours : attendez qu'elle se termine.");
        var analyse = PrendreAnalyse(demande);
        auteur = Court(auteur, 160) ?? "inconnu";

        // Hors du contexte de la requête : la passerelle coupe la requête au bout de 90 s, le travail continue.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                DirectoryHttpAuthContext.AuthorizationHeader.Value = string.IsNullOrWhiteSpace(autorisation) ? null : autorisation;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = (FichesReferenceService)scope.ServiceProvider.GetRequiredService<IFichesReferenceService>();
                    etat.Resultat = await service.ExecuterAsync(analyse, demande, auteur, importId,
                        () => Interlocked.Increment(ref etat.Traitees), CancellationToken.None);
                    etat.Statut = FichesReferenceSuivi.Termine;
                    // v64 : journal d'actions — import du fichier RH de référence.
                    var k = etat.Resultat.Compteurs;
                    await scope.ServiceProvider.GetRequiredService<IJournalActionsService>().EnregistrerAsync(new JournalEvenement(
                        "Imports et exports", "Import", $"Fichier RH de référence « {apercu.Fichier} »", "FichesReference", importId.ToString(),
                        Nouvelles: new Dictionary<string, string?>
                        {
                            ["Fiches mises à jour"] = k.MisesAJour.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["Fiches créées"] = k.Crees.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["Comptes désactivés"] = k.Desactives.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["Champs modifiés"] = k.ChampsModifies.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["Lignes ignorées"] = k.Ignores.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["Erreurs"] = k.Erreurs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        Details: "Le détail fiche par fiche est dans l'historique du fichier RH (annulable).",
                        Source: "POST /api/users/fiches-reference/appliquer"), acteurJournal);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fichier RH de référence : application {ImportId} interrompue", importId);
                    etat.Erreur = "L'application s'est interrompue. Les fiches déjà traitées sont dans l'historique ; relancez l'analyse pour terminer.";
                    etat.Statut = FichesReferenceSuivi.Echec;
                }
            });
        }
        return FichesReferenceSuivi.VersDto(etat);
    }

    public SuiviApplicationFichesDto? SuiviApplication(Guid importId) =>
        suivi?.Lire(importId) is { } etat ? FichesReferenceSuivi.VersDto(etat) : null;

    private async Task<ApplicationFichesReferenceDto> ExecuterAsync(
        AnalyseEnCache analyse, AppliquerFichesReferenceRequest demande, string auteur, Guid importId, Action? avancer, CancellationToken ct)
    {
        var importLe = DateTime.UtcNow;
        var aujourdhui = DateOnly.FromDateTime(importLe);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        var fichesTraitees = new Dictionary<int, int>();
        var resultats = new List<LigneAppliqueeFicheDto>();

        foreach (var (numero, r) in analyse.Rapprochement.OrderBy(kv => kv.Key))
        {
            try { await AppliquerLigneAsync(numero, r); }
            finally { avancer?.Invoke(); }
        }

        async Task AppliquerLigneAsync(int numero, FicheReferenceRapprochement.Resultat r)
        {
            var v = analyse.Valeurs[numero];
            var choix = demande.Choix.TryGetValue(numero, out var c) && !string.IsNullOrWhiteSpace(c)
                ? c.Trim().ToLowerInvariant()
                : r.Statut == FicheReferenceRapprochement.Identifie && r.UserId is int u ? $"user:{u}" : "ignorer";

            LigneAppliqueeFicheDto Ligne(string resultat, int? userId, string? message, bool annuaireOk = true,
                IReadOnlyList<ModificationFicheDto>? modifs = null, IReadOnlyList<string>? manquants = null) =>
                new(numero, v.Agents, resultat, userId, message, annuaireOk,
                    modifs ?? Array.Empty<ModificationFicheDto>(), v.Anomalies.ToList(), manquants ?? Array.Empty<string>());

            if (choix == "ignorer" || r.Statut == FicheReferenceRapprochement.Ignore)
            {
                resultats.Add(Ligne("ignore", null, r.Message ?? "Ligne non appliquée."));
                return;
            }

            try
            {
                if (choix.StartsWith("user:", StringComparison.Ordinal)
                    && int.TryParse(choix[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cible))
                {
                    if (fichesTraitees.TryGetValue(cible, out var autre))
                    {
                        resultats.Add(Ligne("erreur", cible, $"Cette fiche a déjà été mise à jour par la ligne {autre} : ligne non appliquée."));
                        return;
                    }
                    fichesTraitees[cible] = numero;
                    var maj = await MettreAJourAsync(cible, v, importId, importLe, analyse.Fichier, auteur, ct);
                    if (maj is null)
                    {
                        resultats.Add(Ligne("erreur", cible, "Fiche introuvable (supprimée depuis l'analyse ?)."));
                        return;
                    }
                    var message = maj.Modifications.Count == 0 ? "Fiche déjà à jour." : null;
                    var resultat = maj.Modifications.Count == 0 ? "sans-changement" : "mis-a-jour";
                    if (demande.DesactiverSortis && v.Sorti(aujourdhui) && await DesactiverAsync(cible, v, importId, importLe, analyse.Fichier, auteur, ct))
                    {
                        resultat = "desactive";
                        message = $"Compte désactivé (sortie le {v.DateSortie:dd/MM/yyyy}).";
                    }
                    resultats.Add(Ligne(resultat, cible, message, maj.AnnuaireOk, maj.Modifications, v.Sorti(aujourdhui) ? new List<string>() : maj.Manquants));
                }
                else if (choix == "creer")
                {
                    if (r.Statut is FicheReferenceRapprochement.Historique or FicheReferenceRapprochement.SortiInconnu || v.Sorti(aujourdhui))
                    {
                        resultats.Add(Ligne("ignore", null, "Aucune fiche n'est créée pour un employé sorti."));
                        return;
                    }
                    var cree = await CreerAsync(v, roles, importId, importLe, analyse.Fichier, auteur, ct);
                    fichesTraitees[cree.UserId] = numero;
                    resultats.Add(Ligne("cree", cree.UserId, $"Fiche créée (rôle « {cree.Role} », sans mail interne : à attribuer dans Mails internes).",
                        cree.AnnuaireOk, cree.Modifications, cree.Manquants));
                }
                else
                {
                    resultats.Add(Ligne("erreur", null, $"Choix « {choix} » non reconnu."));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fichier RH de référence : ligne {Numero} non appliquée", numero);
                db.ChangeTracker.Clear();
                resultats.Add(Ligne("erreur", null, ex is InvalidOperationException ? ex.Message : "Erreur technique : ligne non appliquée (voir les journaux)."));
            }
        }

        int Nb(string res) => resultats.Count(l => l.Resultat == res);
        var compteurs = new CompteursApplicationFichesDto(
            Nb("mis-a-jour") + Nb("desactive"),
            Nb("sans-changement"),
            Nb("cree"),
            Nb("desactive"),
            Nb("ignore"),
            Nb("erreur"),
            resultats.Sum(l => l.Modifications.Count),
            resultats.Count(l => l.ChampsManquants.Count > 0),
            resultats.Count(l => !l.AnnuaireSynchronise));

        logger.LogInformation(
            "Fichier RH de référence {Fichier} appliqué par {Auteur} : {Maj} mises à jour, {Crees} créations, {Champs} champs, {Erreurs} erreurs",
            analyse.Fichier, auteur, compteurs.MisesAJour, compteurs.Crees, compteurs.ChampsModifies, compteurs.Erreurs);
        return new ApplicationFichesReferenceDto(importId, analyse.Fichier, DateTimeOffset.UtcNow, compteurs, resultats);
    }

    private sealed record ResultatMaj(List<ModificationFicheDto> Modifications, List<string> Manquants, bool AnnuaireOk);

    /// <summary>Met à jour une fiche avec les valeurs non vides du fichier ; journalise chaque champ modifié.</summary>
    private async Task<ResultatMaj?> MettreAJourAsync(
        int userId, FicheReference.Valeurs v, Guid importId, DateTime importLe, string fichier, string auteur, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        var profil = await db.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var nouveauProfil = profil is null;
        profil ??= new UserHrProfile { UserId = userId };

        var modifs = FicheReference.Comparer(profil, v);
        var agent = Court($"{user.LastName} {user.FirstName}".Trim(), 200);
        FicheReferenceModification Journal(string champ, string? avant, string? apres) => new()
        {
            ImportId = importId, ImportLe = importLe, Fichier = fichier, Auteur = auteur,
            UserId = userId, Agent = agent, Champ = champ, AncienneValeur = avant, NouvelleValeur = apres,
        };

        var journal = new List<FicheReferenceModification>();
        foreach (var m in modifs)
        {
            FicheReference.Ecrire(profil, m.Cle, m.Nouvelle);
            journal.Add(Journal(m.Cle, m.Ancienne, m.Nouvelle));
        }

        // La date d'embauche du compte suit celle du fichier (ancienneté, congés, nouveaux arrivants).
        if (ModifDateEmbaucheCompte(user.HireDate, v) is { } compte)
        {
            journal.Add(Journal(compte.Cle, compte.Ancienne, compte.Nouvelle));
            modifs.Add(compte);
            user.HireDate = DateTime.SpecifyKind(DateEmbaucheCompte(v)!.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        var manquants = FicheReference.ChampsManquants(profil);
        if (journal.Count == 0)
            return new ResultatMaj([], manquants, true);

        if (nouveauProfil) db.UserHrProfiles.Add(profil);
        profil.UpdatedAt = DateTimeOffset.UtcNow;
        db.FichesReferenceModifications.AddRange(journal);
        await db.SaveChangesAsync(ct);

        // Le profil RH canonique est dans l'annuaire : sans cette poussée, sa prochaine projection
        // remettrait les anciennes valeurs dans la fiche.
        var annuaireOk = await PousserAnnuaireAsync(user, ct);
        return new ResultatMaj(modifs.Select(VersDto).ToList(), manquants, annuaireOk);
    }

    private sealed record ResultatCreation(int UserId, string Role, List<ModificationFicheDto> Modifications, List<string> Manquants, bool AnnuaireOk);

    /// <summary>Crée la fiche par l'API employés existante (sans mail interne), puis la complète.</summary>
    private async Task<ResultatCreation> CreerAsync(
        FicheReference.Valeurs v, Dictionary<string, int> roles, Guid importId, DateTime importLe, string fichier, string auteur, CancellationToken ct)
    {
        var (nom, prenom) = FicheReference.SeparerNom(v.Agents ?? string.Empty);
        if (string.IsNullOrWhiteSpace(nom))
            throw new InvalidOperationException("Nom de l'employé manquant : fiche non créée.");
        var role = FicheReference.RoleSuggere(v.Poste);
        if (!roles.TryGetValue(role, out var roleId) && !roles.TryGetValue(role = "Employee", out roleId))
            throw new InvalidOperationException("Rôle « Employee » introuvable : fiche non créée.");

        var embauche = DateEmbaucheCompte(v) ?? v.DateAnciennete ?? DateOnly.FromDateTime(importLe);
        var dto = new CreateUserDto
        {
            RoleId = roleId,
            FirstName = string.IsNullOrWhiteSpace(prenom) ? "-" : prenom,
            LastName = nom,
            Email = string.Empty,
            HireDate = DateTime.SpecifyKind(embauche.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            HrProfile = new UserHrProfileDto
            {
                ImmatriculationInterne = v.NumeroOrdre,
                DateAnciennete = v.DateAnciennete,
                DateEmbauche = v.DateEmbauche,
                DateEntree = v.DateEntree ?? v.DateAnciennete ?? v.DateEmbauche,
                DateSortie = v.DateSortie,
                Poste = v.Poste,
                TypeContrat = v.TypeContrat,
                Nationalite = v.Nationalite,
                VilleNaissance = v.VilleNaissance,
                Sexe = v.Sexe,
                SituationFamiliale = v.SituationFamiliale,
                NombreEnfants = v.NombreEnfants,
                Cin = v.Cin,
                ImmatriculationCnss = v.ImmatriculationCnss,
                DateNaissance = v.DateNaissance,
                Adresse = v.Adresse,
                Telephone1 = v.Telephone1,
                TelephoneUrgence = v.TelephoneUrgence,
                RelationUrgence = v.RelationUrgence,
                NumeroBox = v.NumeroBox,
                // v64 : fichier des entrepreneurs et type d'employé (salarié par défaut).
                NumeroCarteAutoentrepreneur = v.RegistreNational,
                CarteImmatriculation = v.CarteImmatriculation,
                NumeroPasseport = v.NumeroPasseport,
                TypeEmploye = v.TypeEmploye ?? TypesEmploye.Salarie,
            },
        };
        var cree = await users.CreateUserAsync(dto);
        db.FichesReferenceModifications.Add(new FicheReferenceModification
        {
            ImportId = importId, ImportLe = importLe, Fichier = fichier, Auteur = auteur,
            UserId = cree.Id, Agent = Court($"{nom} {prenom}".Trim(), 200), Champ = ChampCreation, NouvelleValeur = role,
        });
        await db.SaveChangesAsync(ct);

        // Selon le mode (annuaire maître ou non), une partie du profil peut ne pas avoir été reprise :
        // on complète ce qui manque, avec le même contrôle que pour une mise à jour.
        var complement = await MettreAJourAsync(cree.Id, v, importId, importLe, fichier, auteur, ct);
        var profil = await db.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == cree.Id, ct);
        var renseignes = FicheReference.Champs
            .Select(c => (c, valeur: profil is null ? null : c.Lire(profil)))
            .Where(x => !string.IsNullOrWhiteSpace(x.valeur))
            .Select(x => new ModificationFicheDto(x.c.Cle, x.c.Libelle, null, x.valeur))
            .ToList();
        return new ResultatCreation(cree.Id, role, renseignes, FicheReference.ChampsManquants(profil), complement?.AnnuaireOk ?? true);
    }

    private async Task<bool> DesactiverAsync(
        int userId, FicheReference.Valeurs v, Guid importId, DateTime importLe, string fichier, string auteur, CancellationToken ct)
    {
        var actif = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (bool?)u.IsActive).FirstOrDefaultAsync(ct);
        if (actif != true) return false;
        if (!await users.SetUserActiveAsync(userId, false, v.DateSortie, ct)) return false;
        db.FichesReferenceModifications.Add(new FicheReferenceModification
        {
            ImportId = importId, ImportLe = importLe, Fichier = fichier, Auteur = auteur,
            UserId = userId, Agent = Court(v.Agents, 200), Champ = ChampDesactivation, AncienneValeur = "actif", NouvelleValeur = "inactif",
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> PousserAnnuaireAsync(User user, CancellationToken ct)
    {
        try
        {
            var ok = await annuaire.TryUpdateEmployeeAsync(user, ct);
            if (!ok)
                logger.LogWarning("Fichier RH de référence : annuaire non mis à jour pour l'employé {UserId}", user.Id);
            return ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fichier RH de référence : annuaire injoignable pour l'employé {UserId}", user.Id);
            return false;
        }
    }

    // ── Historique et annulation ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ImportFichesReferenceDto>> HistoriqueAsync(CancellationToken ct = default)
    {
        var lignes = await db.FichesReferenceModifications.AsNoTracking()
            .Select(m => new { m.ImportId, m.ImportLe, m.Fichier, m.Auteur, m.UserId, m.Champ, m.AnnuleeLe })
            .ToListAsync(ct);
        return lignes
            .GroupBy(m => m.ImportId)
            .Select(g => new ImportFichesReferenceDto(
                g.Key,
                g.Min(m => m.ImportLe),
                g.First().Fichier ?? string.Empty,
                g.First().Auteur ?? string.Empty,
                g.Select(m => m.UserId).Distinct().Count(),
                g.Count(m => !m.Champ.StartsWith("__", StringComparison.Ordinal) && m.Champ != ChampDateEmbaucheCompte),
                g.Count(m => m.Champ == ChampCreation),
                g.Count(m => m.Champ == ChampDesactivation),
                g.All(m => m.AnnuleeLe != null)))
            .OrderByDescending(i => i.Le)
            .Take(100)
            .ToList();
    }

    public async Task<IReadOnlyList<LigneAppliqueeFicheDto>> DetailImportAsync(Guid importId, CancellationToken ct = default)
    {
        var lignes = await db.FichesReferenceModifications.AsNoTracking()
            .Where(m => m.ImportId == importId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        return lignes
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var resultat = g.Any(m => m.Champ == ChampCreation) ? "cree"
                    : g.Any(m => m.Champ == ChampDesactivation) ? "desactive" : "mis-a-jour";
                var modifs = g.Where(m => !m.Champ.StartsWith("__", StringComparison.Ordinal))
                    .Select(m => new ModificationFicheDto(m.Champ,
                        m.Champ == ChampDateEmbaucheCompte ? LibelleDateEmbaucheCompte : FicheReference.Libelle(m.Champ) ?? m.Champ,
                        m.AncienneValeur, m.NouvelleValeur))
                    .ToList();
                var annule = g.All(m => m.AnnuleeLe != null);
                return new LigneAppliqueeFicheDto(0, g.First().Agent, resultat, g.Key, annule ? "Annulé" : null, true, modifs, [], []);
            })
            .OrderBy(l => l.Agent)
            .ToList();
    }

    public async Task<AnnulationFichesReferenceDto> AnnulerAsync(Guid importId, string auteur, CancellationToken ct = default)
    {
        var lignes = await db.FichesReferenceModifications
            .Where(m => m.ImportId == importId && m.AnnuleeLe == null)
            .OrderByDescending(m => m.Id)
            .ToListAsync(ct);
        if (lignes.Count == 0)
            throw new InvalidOperationException("Import introuvable ou déjà annulé.");

        int restaurees = 0, conservees = 0, desactivees = 0;
        var maintenant = DateTime.UtcNow;
        foreach (var groupe in lignes.GroupBy(m => m.UserId))
        {
            var userId = groupe.Key;
            // Réactivation / désactivation passent par l'API employés (règles de sortie, annuaire).
            foreach (var m in groupe.Where(x => x.Champ is ChampCreation or ChampDesactivation))
            {
                var actif = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (bool?)u.IsActive).FirstOrDefaultAsync(ct);
                if (m.Champ == ChampCreation && actif == true && await users.SetUserActiveAsync(userId, false, null, ct))
                    desactivees++;
                if (m.Champ == ChampDesactivation && actif == false)
                    await users.SetUserActiveAsync(userId, true, null, ct);
            }

            var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct);
            var profil = await db.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var change = false;
            foreach (var m in groupe.Where(x => x.Champ is not (ChampCreation or ChampDesactivation)))
            {
                if (m.Champ == ChampDateEmbaucheCompte)
                {
                    if (user is not null
                        && DateOnly.FromDateTime(user.HireDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == m.NouvelleValeur
                        && DateOnly.TryParseExact(m.AncienneValeur, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var avant))
                    {
                        user.HireDate = DateTime.SpecifyKind(avant.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                        restaurees++;
                        change = true;
                    }
                    else conservees++;
                }
                else if (profil is not null && FicheReference.Egales(FicheReference.Lire(profil, m.Champ), m.NouvelleValeur))
                {
                    FicheReference.Ecrire(profil, m.Champ, m.AncienneValeur);
                    restaurees++;
                    change = true;
                }
                else
                {
                    // Modifiée depuis par quelqu'un d'autre : la valeur actuelle est gardée.
                    conservees++;
                }
            }

            foreach (var m in groupe) m.AnnuleeLe = maintenant;
            if (profil is not null && change) profil.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            if (change && user is not null)
                await PousserAnnuaireAsync(user, ct);
        }

        logger.LogInformation("Import de fiches {ImportId} annulé par {Auteur} : {Restaurees} valeurs restaurées, {Conservees} conservées",
            importId, auteur, restaurees, conservees);
        var message = conservees > 0
            ? $"{restaurees} valeur(s) restaurée(s). {conservees} valeur(s) modifiée(s) depuis l'import ont été conservées."
            : $"{restaurees} valeur(s) restaurée(s).";
        if (desactivees > 0) message += $" {desactivees} fiche(s) créée(s) par l'import désactivée(s) (historique conservé).";
        return new AnnulationFichesReferenceDto(importId, restaurees, conservees, desactivees, message);
    }

    // ── Complétude ───────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FicheIncompleteDto>> FichesIncompletesAsync(CancellationToken ct = default)
    {
        var agents = await ChargerAgentsAsync(ct);
        return agents
            .Where(a => a.IsActive && !EstCompteTechnique(a.Role))
            .Select(a => new FicheIncompleteDto(a.Id, a.LastName, a.FirstName, a.Profil?.ImmatriculationInterne, a.Service, a.Role,
                FicheReference.ChampsManquants(a.Profil)))
            .Where(f => f.ChampsManquants.Count > 0)
            .OrderByDescending(f => f.ChampsManquants.Count).ThenBy(f => f.Nom).ThenBy(f => f.Prenom)
            .ToList();
    }

    // ── Outils ───────────────────────────────────────────────────────────────────────────────

    private async Task<List<AgentApp>> ChargerAgentsAsync(CancellationToken ct)
    {
        var lignes = await (
            from u in db.Users.AsNoTracking()
            join p in db.UserHrProfiles.AsNoTracking() on u.Id equals p.UserId into gp
            from p in gp.DefaultIfEmpty()
            select new
            {
                u.Id, u.FirstName, u.LastName, u.IsActive, u.HireDate,
                Role = u.Role.Name,
                Service = u.SubService != null ? u.SubService.Name : null,
                Profil = p,
            }).ToListAsync(ct);
        return lignes.Select(l => new AgentApp(l.Id, l.FirstName, l.LastName, l.IsActive, l.HireDate, l.Role, l.Service, l.Profil)).ToList();
    }

    private static bool EstCompteTechnique(string? role) =>
        role is not null && role.Contains("admin", StringComparison.OrdinalIgnoreCase);

    private const string LibelleDateEmbaucheCompte = "Date d'embauche du compte";

    /// <summary>
    /// Date d'embauche du compte : la date d'embauche du fichier ; à défaut (fichier des entrepreneurs),
    /// sa date d'entrée.
    /// </summary>
    private static DateOnly? DateEmbaucheCompte(FicheReference.Valeurs v) => v.DateEmbauche ?? v.DateEntree;

    private static FicheReference.Modification? ModifDateEmbaucheCompte(DateTime hireDate, FicheReference.Valeurs v) =>
        DateEmbaucheCompte(v) is DateOnly embauche && DateOnly.FromDateTime(hireDate) != embauche
            ? new FicheReference.Modification(ChampDateEmbaucheCompte, LibelleDateEmbaucheCompte,
                DateOnly.FromDateTime(hireDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                embauche.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : null;

    private static string CleCache(string jeton) => $"fiches-reference:{jeton}";

    private static string? Court(string? texte, int max) =>
        string.IsNullOrWhiteSpace(texte) ? null : texte.Length <= max ? texte : texte[..max];

    private static ModificationFicheDto VersDto(FicheReference.Modification m) => new(m.Cle, m.Libelle, m.Ancienne, m.Nouvelle);

    /// <summary>Fiche telle qu'elle serait après application (pour le contrôle de complétude).</summary>
    private static UserHrProfile Simuler(UserHrProfile? profil, IEnumerable<FicheReference.Modification> modifs)
    {
        var copie = new UserHrProfile();
        if (profil is not null)
            foreach (var c in FicheReference.Champs.Concat(FicheReference.ChampsSiVides))
                c.Ecrire(copie, c.Lire(profil));
        foreach (var m in modifs)
            FicheReference.Ecrire(copie, m.Cle, m.Nouvelle);
        return copie;
    }

    /// <summary>
    /// Lit le classeur : la feuille principale est celle qui reconnaît le plus de colonnes (dont « Agents »),
    /// l'en-tête est cherché dans les 10 premières lignes. Les feuilles « LISTE BOX » donnent le N° BOX
    /// (cellules fusionnées ou laissées vides sous un numéro : le dernier numéro vaut pour les lignes suivantes).
    /// </summary>
    private static Classeur LireClasseur(Stream flux, DateOnly aujourdhui)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(flux); }
        catch (Exception) { throw new InvalidOperationException("Fichier illisible : enregistrez-le au format Excel (.xlsx), puis réessayez."); }

        using (wb)
        {
            var feuilles = new List<FeuilleLue>();
            foreach (var ws in wb.Worksheets)
                if (LireEnTete(ws) is { } f) feuilles.Add(f);

            var principale = feuilles
                .OrderByDescending(f => f.Colonnes.Count)
                .FirstOrDefault(f => f.Colonnes.ContainsValue(FicheReference.CleAgents));
            if (principale is null || principale.Colonnes.Count < 4)
                throw new InvalidOperationException(
                    "Aucune feuille ne contient les colonnes du fichier de référence (« Agents » et au moins 3 autres colonnes reconnues : N° Ordre, Date d'embauche, CIN…).");

            // N° BOX des autres feuilles.
            var boxParOrdre = new Dictionary<string, string>();
            var boxParNom = new Dictionary<string, string>();
            var feuillesBox = new List<string>();
            foreach (var f in feuilles.Where(x => x != principale && x.Colonnes.ContainsValue(FicheReference.CleBox)))
            {
                feuillesBox.Add(f.Ws.Name);
                var colBox = f.Colonnes.First(kv => kv.Value == FicheReference.CleBox).Key;
                var colOrdre = f.Colonnes.FirstOrDefault(kv => kv.Value == FicheReference.CleNumeroOrdre).Key;
                var colNom = f.Colonnes.FirstOrDefault(kv => kv.Value == FicheReference.CleAgents).Key;
                string? boxCourante = null;
                for (var r = f.LigneEnTete + 1; r <= f.DerniereLigne; r++)
                {
                    boxCourante = FicheReference.LireTexte(Cellule(f.Ws.Cell(r, colBox))) ?? boxCourante;
                    if (boxCourante is null) continue;
                    var ordre = colOrdre > 0 ? FicheReferenceRapprochement.NormaliserMatricule(FicheReference.LireNumeroOrdre(Cellule(f.Ws.Cell(r, colOrdre)))) : string.Empty;
                    var nom = colNom > 0 ? FicheReference.CleNom(FicheReference.LireTexte(Cellule(f.Ws.Cell(r, colNom)))) : string.Empty;
                    if (ordre.Length > 0) boxParOrdre.TryAdd(ordre, boxCourante);
                    if (nom.Length > 0) boxParNom.TryAdd(nom, boxCourante);
                }
            }

            // Colonnes de la feuille principale : une colonne par champ ; tout le reste est signalé.
            var ws0 = principale.Ws;
            var reconnues = new List<ColonneFichierDto>();
            var nonReprises = new List<string>();
            var colonneDe = new Dictionary<string, int>();
            for (var col = principale.PremiereColonne; col <= principale.DerniereColonne; col++)
            {
                var lettre = ws0.Column(col).ColumnLetter();
                var entete = principale.EnTetes.GetValueOrDefault(col);
                var cle = principale.Colonnes.GetValueOrDefault(col);
                if (cle is not null && colonneDe.TryAdd(cle, col))
                {
                    var def = FicheReference.Colonnes.First(c => c.Cle == cle);
                    reconnues.Add(new ColonneFichierDto(cle, def.Intitule, entete, lettre));
                    continue;
                }
                var remplie = Enumerable.Range(principale.LigneEnTete + 1, Math.Max(0, principale.DerniereLigne - principale.LigneEnTete))
                    .Any(r => FicheReference.LireTexte(Cellule(ws0.Cell(r, col))) is not null);
                if (cle is not null)
                    nonReprises.Add($"Colonne {lettre} « {entete} » : doublon de la colonne {ws0.Column(colonneDe[cle]).ColumnLetter()}, non reprise.");
                else if (entete is not null)
                    nonReprises.Add($"Colonne {lettre} « {entete} » : intitulé non reconnu{(remplie ? ", valeurs non reprises" : " (colonne vide)")}.");
                else if (remplie)
                    nonReprises.Add($"Colonne {lettre} sans intitulé mais remplie : valeurs non reprises (ajoutez un intitulé).");
            }

            var lignes = new List<LigneLue>();
            for (var r = principale.LigneEnTete + 1; r <= principale.DerniereLigne; r++)
            {
                var brut = colonneDe.ToDictionary(kv => kv.Key, kv => Cellule(ws0.Cell(r, kv.Value)));
                if (brut.Values.All(x => FicheReference.LireTexte(x) is null)) continue;
                var v = FicheReference.Interpreter(brut, aujourdhui);
                v.Telephone1 = TelephoneMarocain(v.Telephone1);
                v.TelephoneUrgence = TelephoneMarocain(v.TelephoneUrgence);
                if (v.NumeroBox is null)
                {
                    var ordre = FicheReferenceRapprochement.NormaliserMatricule(v.NumeroOrdre);
                    v.NumeroBox = (ordre.Length > 0 ? boxParOrdre.GetValueOrDefault(ordre) : null)
                                  ?? boxParNom.GetValueOrDefault(FicheReference.CleNom(v.Agents));
                }
                lignes.Add(new LigneLue(r, v));
            }

            return new Classeur(ws0.Name, reconnues, nonReprises, feuillesBox, lignes);
        }
    }

    /// <summary>Un numéro saisi comme nombre perd son 0 initial (612345678 → 0612345678).</summary>
    private static string? TelephoneMarocain(string? numero) =>
        numero is { Length: 9 } n && n.All(char.IsDigit) && n[0] is '5' or '6' or '7' ? "0" + n : numero;

    private sealed record FeuilleLue(
        IXLWorksheet Ws, int LigneEnTete, int PremiereColonne, int DerniereColonne, int DerniereLigne,
        Dictionary<int, string?> Colonnes, Dictionary<int, string> EnTetes);

    private static FeuilleLue? LireEnTete(IXLWorksheet ws)
    {
        var plage = ws.RangeUsed();
        if (plage is null) return null;
        var premiere = plage.FirstRow().RowNumber();
        var derniere = plage.LastRow().RowNumber();
        var colMin = plage.FirstColumn().ColumnNumber();
        var colMax = plage.LastColumn().ColumnNumber();

        FeuilleLue? meilleure = null;
        for (var r = premiere; r <= Math.Min(premiere + 9, derniere); r++)
        {
            var enTetes = new Dictionary<int, string>();
            var colonnes = new Dictionary<int, string?>();
            for (var c = colMin; c <= colMax; c++)
            {
                var texte = FicheReference.LireTexte(Cellule(ws.Cell(r, c)));
                if (texte is null) continue;
                enTetes[c] = texte;
                colonnes[c] = FicheReference.ColonneDe(texte);
            }
            var reconnues = colonnes.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
            var distinctes = reconnues.Values.Distinct().Count();
            if (distinctes >= 2 && distinctes > (meilleure?.Colonnes.Values.Distinct().Count() ?? 0))
                meilleure = new FeuilleLue(ws, r, colMin, colMax, derniere, reconnues, enTetes);
        }
        return meilleure;
    }

    /// <summary>Valeur brute d'une cellule : date, nombre ou texte (formules : valeur calculée).</summary>
    private static object? Cellule(IXLCell cellule)
    {
        try
        {
            var v = cellule.Value;
            return v.Type switch
            {
                XLDataType.Blank => null,
                XLDataType.Number => v.GetNumber(),
                XLDataType.DateTime => v.GetDateTime(),
                XLDataType.Text => v.GetText(),
                XLDataType.Boolean => v.GetBoolean() ? "Oui" : "Non",
                XLDataType.TimeSpan => v.GetTimeSpan().ToString(),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
