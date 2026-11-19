using Microsoft.EntityFrameworkCore;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Presence;

/// <summary>
/// v48 — Bibliothèque « Process Bytel ».
/// <para>
/// L'encadrement dépose les procédures et choisit qui les voit : tous les collaborateurs, ou
/// seulement certaines activités (contrats). Le filtre s'applique sur le <b>pôle</b> de l'agent,
/// seul rattachement connu du Planning : à l'enregistrement, l'écran transmet les pôles des
/// activités choisies.
/// </para>
/// </summary>
public sealed class ProcessDocumentService(AppDbContext db, IPresenceService presences) : IProcessDocumentService
{
    public async Task<ProcessBibliothequeDto> ListerAsync(
        PresenceViewer viewer,
        bool peutDeposer,
        CancellationToken ct = default)
    {
        var (_, _, monPole) = await MoiAsync(viewer, ct);

        var lignes = await db.ProcessDocuments.AsNoTracking()
            .OrderBy(d => d.Dossier).ThenByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id, d.Dossier, d.Titre, d.NomFichier, d.ContentType, d.Taille, d.Commentaire,
                d.PourTous, d.Activites, d.Poles, d.CreatedByNom, d.CreatedAt,
            })
            .ToListAsync(ct);

        var visibles = lignes
            .Where(d => peutDeposer || EstVisible(d.PourTous, d.Poles, monPole))
            .Select(d => new ProcessDocumentDto(
                d.Id, d.Dossier, d.Titre, d.NomFichier, d.ContentType, d.Taille, d.Commentaire,
                d.PourTous, ProcessDocuments.Decouper(d.Activites), d.CreatedByNom, d.CreatedAt))
            .ToList();

        var dossiers = visibles.Select(d => d.Dossier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (dossiers.Count == 0) dossiers.Add(ProcessDocuments.DossierParDefaut);

        return new ProcessBibliothequeDto(dossiers, visibles, peutDeposer, monPole);
    }

    public async Task<ProcessDocumentDto> AjouterAsync(
        PresenceViewer viewer,
        string? dossier,
        string? titre,
        string nomFichier,
        string? contentType,
        byte[] contenu,
        string? commentaire,
        ProcessVisibiliteRequest visibilite,
        CancellationToken ct = default)
    {
        if (contenu.Length == 0)
            throw new PresenceRefusException("Aucun fichier reçu.");
        if (contenu.LongLength > ProcessDocuments.TailleMax)
            throw new PresenceRefusException("Fichier trop volumineux : 25 Mo au plus.");

        var pourTous = visibilite.PourTous;
        var activites = ProcessDocuments.Joindre(visibilite.Activites);
        var poles = ProcessDocuments.Joindre(visibilite.Poles);
        if (!pourTous && poles.Length == 0)
            throw new PresenceRefusException(
                "Choisissez « Tous les collaborateurs » ou au moins une activité destinataire.");

        var (userId, nom, _) = await MoiAsync(viewer, ct);
        var nomFinal = Couper(nomFichier, 255, "document");
        var doc = new ProcessDocument
        {
            Dossier = ProcessDocuments.NormaliserDossier(dossier),
            Titre = Couper(string.IsNullOrWhiteSpace(titre) ? nomFinal : titre!, ProcessDocuments.MaxTitre, nomFinal),
            NomFichier = nomFinal,
            ContentType = Couper(contentType, 120, "application/octet-stream"),
            Taille = contenu.LongLength,
            Contenu = contenu,
            Commentaire = string.IsNullOrWhiteSpace(commentaire) ? null : Couper(commentaire, 1000, ""),
            PourTous = pourTous,
            Activites = pourTous ? string.Empty : activites,
            Poles = pourTous ? string.Empty : poles,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            CreatedByNom = nom,
        };

        db.ProcessDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        return new ProcessDocumentDto(
            doc.Id, doc.Dossier, doc.Titre, doc.NomFichier, doc.ContentType, doc.Taille, doc.Commentaire,
            doc.PourTous, ProcessDocuments.Decouper(doc.Activites), doc.CreatedByNom, doc.CreatedAt);
    }

    public async Task<ProcessDocumentContenuDto> TelechargerAsync(
        PresenceViewer viewer,
        long id,
        bool peutDeposer,
        CancellationToken ct = default)
    {
        var doc = await db.ProcessDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw new PresenceRefusException("Document introuvable.");

        if (!peutDeposer)
        {
            var (_, _, monPole) = await MoiAsync(viewer, ct);
            if (!EstVisible(doc.PourTous, doc.Poles, monPole))
                throw new PresenceRefusException("Ce document n'est pas destiné à votre activité.", horsPerimetre: true);
        }

        return new ProcessDocumentContenuDto(doc.NomFichier, doc.ContentType, doc.Contenu);
    }

    public async Task SupprimerAsync(PresenceViewer viewer, long id, CancellationToken ct = default)
    {
        var doc = await db.ProcessDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw new PresenceRefusException("Document introuvable.");
        db.ProcessDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Visible si le document est pour tous, ou si le pôle de l'agent est destinataire.</summary>
    public static bool EstVisible(bool pourTous, string? poles, string? monPole)
    {
        if (pourTous) return true;
        if (string.IsNullOrWhiteSpace(monPole)) return false;
        return ProcessDocuments.Decouper(poles)
            .Any(p => string.Equals(p, monPole, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Compte Planning du demandeur, son nom et l'identifiant de son pôle.</summary>
    private async Task<(int? UserId, string? Nom, string? PoleId)> MoiAsync(PresenceViewer viewer, CancellationToken ct)
    {
        var auteur = await presences.ResoudreAsync(viewer, ct);
        if (auteur.UserId is not int id)
            return (null, auteur.Nom, null);

        var pole = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.SubService != null && u.SubService.Service != null && u.SubService.Service.Floor != null
                ? u.SubService.Service.Floor.PrimePoleId
                : null)
            .FirstOrDefaultAsync(ct);

        return (id, auteur.Nom, string.IsNullOrWhiteSpace(pole) ? null : pole);
    }

    private static string Couper(string? valeur, int max, string defaut)
    {
        var v = (valeur ?? string.Empty).Trim();
        if (v.Length == 0) return defaut;
        return v.Length > max ? v[..max] : v;
    }
}
