using Microsoft.EntityFrameworkCore;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Presence;

/// <summary>
/// Avertissements, éléments variables de paie et dossier administratif. Chaque lecture et chaque
/// écriture est limitée aux agents du périmètre du demandeur (RH / Admin : tous).
/// </summary>
public sealed class PresenceSuiviService(AppDbContext db, IPresenceService presences) : IPresenceSuiviService
{
    // ── Agents du périmètre ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PresenceAgentRefDto>> ListerAgentsAsync(PresenceViewer viewer, CancellationToken ct = default)
    {
        var auteur = await presences.ResoudreAsync(viewer, ct);
        var q = db.Users.AsNoTracking().Where(u => u.SubServiceId != null);
        if (auteur.Agents is { } ids)
        {
            var liste = ids.ToList();
            q = q.Where(u => liste.Contains(u.Id));
        }
        else
        {
            q = q.Where(u => u.IsActive);
        }
        var lignes = await q
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                Cellule = u.SubService!.Service.Name,
                Pole = u.SubService!.Service.Floor.Name,
                PoleId = u.SubService!.Service.Floor.PrimePoleId,
            })
            .ToListAsync(ct);
        var userIds = lignes.Select(l => l.Id).ToList();
        var sousTraitants = (await db.UserHrProfiles.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId) && p.NumeroCarteAutoentrepreneur != null && p.NumeroCarteAutoentrepreneur != "")
                .Select(p => p.UserId)
                .ToListAsync(ct))
            .ToHashSet();
        return lignes
            .Select(l => new PresenceAgentRefDto(
                l.Id,
                $"{l.LastName} {l.FirstName}".Trim(),
                l.Pole,
                l.PoleId,
                l.Cellule,
                sousTraitants.Contains(l.Id) ? "Sous-traitant" : "Salarié"))
            .OrderBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ── Avertissements ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AvertissementDto>> ListerAvertissementsAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var auteur = await presences.ResoudreAsync(viewer, ct);
        var q = db.Avertissements.AsNoTracking().Where(a => a.Date >= du && a.Date <= au);
        if (auteur.Agents is { } ids) { var liste = ids.ToList(); q = q.Where(a => liste.Contains(a.UserId)); }
        var lignes = await q.OrderByDescending(a => a.Date).ThenByDescending(a => a.Id).ToListAsync(ct);
        var noms = await NomsAsync(lignes.Select(a => a.UserId), ct);
        return lignes.Select(a => Map(a, noms)).ToList();
    }

    public async Task<AvertissementDto> AjouterAvertissementAsync(PresenceViewer viewer, AvertissementRequest request, CancellationToken ct = default)
    {
        var auteur = await AutoriserAsync(viewer, request.UserId, ct);
        // v30 : code du référentiel des manquements ; les retards (R01…R03) viennent du pointage.
        string? code = null;
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var manquement = Referentiel.Trouver(request.Code)
                             ?? throw new PresenceRefusException($"Code « {request.Code.Trim()} » absent du référentiel.");
            if (Referentiel.EstRetard(manquement.Code))
                throw new PresenceRefusException("Les retards (R01 à R03) sont calculés depuis le pointage : ils ne se saisissent pas.");
            code = manquement.Code;
        }
        var type = code is not null && string.IsNullOrWhiteSpace(request.Type)
            ? SuiviCatalogues.TypesAvertissement[0]
            : Choix(request.Type, SuiviCatalogues.TypesAvertissement, "Type d'avertissement");
        var ligne = new Avertissement
        {
            UserId = request.UserId,
            Date = request.Date,
            Type = type,
            Code = code,
            Commentaire = Obligatoire(request.Commentaire, 1000, "Le commentaire"),
            CreatedBy = auteur.UserId,
            CreatedByNom = auteur.Nom,
        };
        db.Avertissements.Add(ligne);
        await db.SaveChangesAsync(ct);
        return Map(ligne, await NomsAsync([ligne.UserId], ct));
    }

    public async Task SupprimerAvertissementAsync(PresenceViewer viewer, long id, CancellationToken ct = default)
    {
        var ligne = await db.Avertissements.FirstOrDefaultAsync(a => a.Id == id, ct)
                    ?? throw new PresenceRefusException("Avertissement introuvable.");
        await AutoriserAsync(viewer, ligne.UserId, ct);
        db.Avertissements.Remove(ligne);
        await db.SaveChangesAsync(ct);
    }

    // ── Éléments variables de paie ──────────────────────────────────────────────

    public async Task<IReadOnlyList<EvpDto>> ListerEvpAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var auteur = await presences.ResoudreAsync(viewer, ct);
        var q = db.ElementsVariablesPaie.AsNoTracking().Where(e => e.Date >= du && e.Date <= au);
        if (auteur.Agents is { } ids) { var liste = ids.ToList(); q = q.Where(e => liste.Contains(e.UserId)); }
        var lignes = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToListAsync(ct);
        var noms = await NomsAsync(lignes.Select(e => e.UserId), ct);
        return lignes.Select(e => Map(e, noms)).ToList();
    }

    public async Task<EvpDto> AjouterEvpAsync(PresenceViewer viewer, EvpRequest request, CancellationToken ct = default)
    {
        var auteur = await AutoriserAsync(viewer, request.UserId, ct);
        var ligne = new ElementVariablePaie { UserId = request.UserId };
        Appliquer(ligne, request, auteur);
        db.ElementsVariablesPaie.Add(ligne);
        await db.SaveChangesAsync(ct);
        return Map(ligne, await NomsAsync([ligne.UserId], ct));
    }

    public async Task<EvpDto> ModifierEvpAsync(PresenceViewer viewer, long id, EvpRequest request, CancellationToken ct = default)
    {
        var ligne = await db.ElementsVariablesPaie.FirstOrDefaultAsync(e => e.Id == id, ct)
                    ?? throw new PresenceRefusException("Élément introuvable.");
        await AutoriserAsync(viewer, ligne.UserId, ct);
        var auteur = await AutoriserAsync(viewer, request.UserId, ct);
        ligne.UserId = request.UserId;
        Appliquer(ligne, request, auteur);
        await db.SaveChangesAsync(ct);
        return Map(ligne, await NomsAsync([ligne.UserId], ct));
    }

    public async Task SupprimerEvpAsync(PresenceViewer viewer, long id, CancellationToken ct = default)
    {
        var ligne = await db.ElementsVariablesPaie.FirstOrDefaultAsync(e => e.Id == id, ct)
                    ?? throw new PresenceRefusException("Élément introuvable.");
        await AutoriserAsync(viewer, ligne.UserId, ct);
        db.ElementsVariablesPaie.Remove(ligne);
        await db.SaveChangesAsync(ct);
    }

    private static void Appliquer(ElementVariablePaie ligne, EvpRequest request, PresenceAuteur auteur)
    {
        if (Math.Abs(request.Montant) > 1_000_000m)
            throw new PresenceRefusException("Montant hors limites.");
        ligne.Date = request.Date;
        ligne.Type = Choix(request.Type, SuiviCatalogues.TypesEvp, "Type d'élément");
        ligne.Montant = Math.Round(request.Montant, 2);
        ligne.Commentaire = Obligatoire(request.Commentaire, 1000, "Le commentaire");
        ligne.CreatedAt = DateTime.UtcNow;
        ligne.CreatedBy = auteur.UserId;
        ligne.CreatedByNom = auteur.Nom;
    }

    // ── Dossier administratif ───────────────────────────────────────────────────

    public async Task<DossierDto> GetDossierAsync(PresenceViewer viewer, int userId, CancellationToken ct = default)
    {
        await AutoriserAsync(viewer, userId, ct);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.FirstName, u.LastName, u.Email,
                Role = u.Role.Name,
                Cellule = u.SubService != null ? u.SubService.Service.Name : null,
            })
            .FirstOrDefaultAsync(ct) ?? throw new PresenceRefusException("Agent introuvable.");
        var p = await db.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var fiche = new FicheRhDto(
            $"{user.LastName} {user.FirstName}".Trim(),
            user.Email,
            user.Role,
            user.Cellule,
            string.IsNullOrWhiteSpace(p?.NumeroCarteAutoentrepreneur) ? "Salarié" : "Sous-traitant",
            p?.ImmatriculationInterne,
            p?.Cin,
            p?.ImmatriculationCnss,
            p?.Telephone1,
            p?.Adresse,
            MasquerRib(p?.Rib),
            p?.DateEntree,
            p?.DateEmbauche,
            p?.DateSortie,
            p?.EnFormation ?? false);
        var documents = await db.DossierDocuments.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DossierDocumentDto(d.Id, d.Categorie, d.NomFichier, d.ContentType, d.Taille, d.Commentaire, d.CreatedByNom, d.CreatedAt))
            .ToListAsync(ct);
        return new DossierDto(userId, fiche, documents);
    }

    public async Task<DossierDocumentDto> AjouterDocumentAsync(
        PresenceViewer viewer, int userId, string categorie, string nomFichier, string contentType, byte[] contenu, string? commentaire,
        CancellationToken ct = default)
    {
        var auteur = await AutoriserAsync(viewer, userId, ct);
        if (contenu.Length == 0) throw new PresenceRefusException("Fichier vide.");
        if (contenu.Length > SuiviCatalogues.TailleMaxDocument) throw new PresenceRefusException("Fichier trop volumineux : 10 Mo au plus.");
        var nom = Path.GetFileName(nomFichier ?? string.Empty).Trim();
        if (nom.Length == 0) nom = "document";
        if (nom.Length > 255) nom = nom[^255..];
        var doc = new DossierDocument
        {
            UserId = userId,
            Categorie = Choix(categorie, SuiviCatalogues.CategoriesDossier, "Catégorie"),
            NomFichier = nom,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim()[..Math.Min(120, contentType.Trim().Length)],
            Taille = contenu.Length,
            Contenu = contenu,
            Commentaire = string.IsNullOrWhiteSpace(commentaire) ? null : commentaire.Trim()[..Math.Min(500, commentaire.Trim().Length)],
            CreatedBy = auteur.UserId,
            CreatedByNom = auteur.Nom,
        };
        db.DossierDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return new DossierDocumentDto(doc.Id, doc.Categorie, doc.NomFichier, doc.ContentType, doc.Taille, doc.Commentaire, doc.CreatedByNom, doc.CreatedAt);
    }

    public async Task<DocumentFichier> GetDocumentAsync(PresenceViewer viewer, long id, CancellationToken ct = default)
    {
        var doc = await db.DossierDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw new PresenceRefusException("Document introuvable.");
        await AutoriserAsync(viewer, doc.UserId, ct);
        return new DocumentFichier(doc.NomFichier, doc.ContentType, doc.Contenu);
    }

    public async Task SupprimerDocumentAsync(PresenceViewer viewer, long id, CancellationToken ct = default)
    {
        var doc = await db.DossierDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw new PresenceRefusException("Document introuvable.");
        await AutoriserAsync(viewer, doc.UserId, ct);
        db.DossierDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
    }

    // ── Outils ──────────────────────────────────────────────────────────────────

    private async Task<PresenceAuteur> AutoriserAsync(PresenceViewer viewer, int userId, CancellationToken ct)
    {
        var auteur = await presences.ResoudreAsync(viewer, ct);
        if (!auteur.PeutTraiter(userId))
            throw new PresenceRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw new PresenceRefusException("Agent introuvable.");
        return auteur;
    }

    private async Task<Dictionary<int, string>> NomsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var liste = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => liste.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName} {u.FirstName}".Trim(), ct);
    }

    private static AvertissementDto Map(Avertissement a, IReadOnlyDictionary<int, string> noms) =>
        new(a.Id, a.UserId, noms.GetValueOrDefault(a.UserId) ?? $"#{a.UserId}", a.Date, a.Type, a.Commentaire, a.CreatedByNom, a.CreatedAt, a.Code);

    private static EvpDto Map(ElementVariablePaie e, IReadOnlyDictionary<int, string> noms) =>
        new(e.Id, e.UserId, noms.GetValueOrDefault(e.UserId) ?? $"#{e.UserId}", e.Date, e.Type, e.Montant, e.Commentaire, e.CreatedByNom, e.CreatedAt);

    private static string Choix(string? valeur, IReadOnlyList<string> liste, string libelle)
    {
        var v = (valeur ?? string.Empty).Trim();
        return liste.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase))
               ?? throw new PresenceRefusException($"{libelle} inconnu : « {v} ».");
    }

    private static string Obligatoire(string? texte, int max, string libelle)
    {
        var t = (texte ?? string.Empty).Trim();
        if (t.Length == 0) throw new PresenceRefusException($"{libelle} est obligatoire.");
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>RIB affiché partiellement : seuls les 4 derniers caractères restent lisibles.</summary>
    internal static string? MasquerRib(string? rib)
    {
        var r = (rib ?? string.Empty).Replace(" ", "");
        return r.Length == 0 ? null : r.Length <= 4 ? r : new string('•', r.Length - 4) + r[^4..];
    }
}
