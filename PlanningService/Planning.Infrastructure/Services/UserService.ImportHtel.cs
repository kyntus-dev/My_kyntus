using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Application.Users;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v49 — Création en lot des employés depuis le fichier RH (écran « Liaisons HTEL »).
/// <para>
/// Chaque ligne passe par la création normale d'une fiche : annuaire, miroir Planning, profil RH.
/// Le mail interne n'étant pas dans le fichier, les fiches sont créées <b>sans adresse</b> (v47) :
/// aucun compte de connexion n'est ouvert et aucun e-mail n'est envoyé. Une ligne en échec
/// n'empêche pas les suivantes ; le rapport dit ce qui est passé et ce qui ne l'est pas.
/// </para>
/// </summary>
public partial class UserService
{
    public async Task<ImportEmployesResultDto> ImporterEmployesAsync(
        ImportEmployesRequest request,
        CancellationToken ct = default)
    {
        var resultats = new List<ImportEmployeResultatDto>();
        var lignes = request.Lignes ?? new List<LigneImportEmployeDto>();

        var role = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
            ?? throw new InvalidOperationException("Rôle inconnu : choisissez le rôle des employés importés.");

        if (request.SubServiceId is int ss && !await _context.SubServices.AsNoTracking().AnyAsync(s => s.Id == ss, ct))
            throw new InvalidOperationException("Service inconnu : choisissez un service existant ou laissez vide.");

        // Doublons : nom + prénom déjà présents dans l'application (comparaison sans accents ni casse).
        var existants = await _context.Users.AsNoTracking()
            .Select(u => new { u.FirstName, u.LastName })
            .ToListAsync(ct);
        var cles = existants
            .Select(u => CleNom(u.LastName, u.FirstName))
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var ligne in lignes)
        {
            ct.ThrowIfCancellationRequested();
            var nom = (ligne.Nom ?? string.Empty).Trim();
            var prenom = (ligne.Prenom ?? string.Empty).Trim();

            ImportEmployeResultatDto Echec(string message, string statut = ImportEmployeStatuts.Erreur) =>
                new(ligne.Numero, nom, prenom, false, null, null, message, statut);

            if (nom.Length == 0 || prenom.Length == 0)
            {
                resultats.Add(Echec("Nom ou prénom absent."));
                continue;
            }

            var cle = CleNom(nom, prenom);
            if (!cles.Add(cle))
            {
                // v66 : filet de sécurité — les fiches existantes sont vérifiées et mises à jour avant
                // la création (IFichesReferenceService.SynchroniserImportEmployesAsync) ; une ligne qui
                // arrive ici avec un nom déjà présent est un cas douteux, jamais un doublon créé.
                resultats.Add(Echec("Déjà présent dans l'application (même nom et prénom) : fiche non créée, à vérifier.",
                    ImportEmployeStatuts.AVerifier));
                continue;
            }

            try
            {
                var dto = new CreateUserDto
                {
                    RoleId = request.RoleId,
                    SubServiceId = request.SubServiceId,
                    FirstName = prenom,
                    LastName = nom,
                    // v47 : pas de mail interne dans le fichier RH → adresse provisoire, aucun compte.
                    Email = string.Empty,
                    HireDate = request.HireDate,
                    Level = 1,
                    HrProfile = new UserHrProfileDto
                    {
                        Cin = Vide(ligne.Cin),
                        DateNaissance = ligne.DateNaissance,
                        SituationFamiliale = Vide(ligne.SituationFamiliale),
                        NombreEnfants = ligne.NombreEnfants,
                        Adresse = Vide(ligne.Adresse),
                        EmailPersonnel = Vide(ligne.EmailPersonnel),
                        Telephone1 = Vide(ligne.Telephone),
                        EnFormation = request.EnFormation,
                        TypeFormation = request.EnFormation ? request.TypeFormation : null,
                    },
                };

                var cree = await CreateUserAsync(dto);
                resultats.Add(new ImportEmployeResultatDto(
                    ligne.Numero, nom, prenom, true, cree.Id, cree.Guid.ToString(), null));
            }
            catch (Exception ex)
            {
                cles.Remove(cle);
                _logger.LogWarning(ex, "Import employés : ligne {Ligne} ({Nom} {Prenom}) en échec.", ligne.Numero, nom, prenom);
                resultats.Add(Echec(ex.Message));
            }
        }

        _logger.LogInformation(
            "Import employés (liaisons HTEL) : {Crees} créé(s), {Echecs} en échec, rôle {Role}.",
            resultats.Count(r => r.Ok), resultats.Count(r => !r.Ok), role.Name);

        return new ImportEmployesResultDto(
            resultats,
            resultats.Count(r => r.Ok),
            resultats.Count(r => !r.Ok));
    }

    private static string? Vide(string? valeur)
    {
        var v = (valeur ?? string.Empty).Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Clé « nom + prénom » insensible à l'ordre, aux accents et à la casse.</summary>
    private static string CleNom(string? nom, string? prenom)
    {
        var mots = RapprochementMails.Normaliser($"{nom} {prenom}")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(m => m, StringComparer.Ordinal);
        return string.Join(' ', mots);
    }
}
