using System.Globalization;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Planning.Application.PlanningIt;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.PlanningIt;

/// <summary>
/// Planning IT partagé : chaque membre (rôles Ingénieur et Support IT) saisit ses horaires et voit
/// ceux de ses collègues ; l'Admin peut corriger toutes les lignes. Les saisies restent modifiables.
/// Ces horaires sont la référence du pointage des membres IT (voir PointageQueryService).
/// </summary>
public sealed class PlanningItService(AppDbContext db) : IPlanningItService
{
    public async Task<PlanningItSemaineDto> GetSemaineAsync(int? viewerUserId, PlanningItDroits droits, DateOnly jour, CancellationToken ct = default)
    {
        if (!droits.Voir)
            throw new UnauthorizedAccessException("Accès au planning IT non autorisé.");

        var du = PlanningItRegles.Lundi(jour);
        var au = du.AddDays(6);
        var membres = await MembresAsync(ct);
        var ids = membres.Select(m => m.Id).ToList();

        var horaires = await db.PlanningItHoraires.AsNoTracking()
            .Where(h => ids.Contains(h.UserId) && h.Jour >= du && h.Jour <= au)
            .ToListAsync(ct);
        var commentaires = await db.PlanningItCommentaires.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && c.Semaine == du)
            .ToDictionaryAsync(c => c.UserId, c => c.Texte, ct);
        var auteurIds = horaires.Where(h => h.UpdatedBy != null).Select(h => h.UpdatedBy!.Value).Distinct().ToList();
        var auteurs = await db.Users.AsNoTracking().Where(u => auteurIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var parCle = horaires.ToDictionary(h => (h.UserId, h.Jour));

        var lignes = membres
            .OrderByDescending(m => m.Id == viewerUserId)
            .ThenBy(m => m.Nom, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new PlanningItMembreDto(
                m.Id,
                m.Nom,
                m.Role,
                m.Id == viewerUserId,
                droits.ModifierTout || (droits.SaisirSoi && m.Id == viewerUserId),
                Enumerable.Range(0, 7).Select(i =>
                {
                    var d = du.AddDays(i);
                    // v31 : jours fériés français affichés d'office (une saisie reste possible).
                    var ferie = JoursFeries.Nom(d);
                    return parCle.TryGetValue((m.Id, d), out var h)
                        ? new PlanningItJourDto(d, h.Statut, h.Arrivee?.ToString("HH:mm", CultureInfo.InvariantCulture), h.UpdatedAt,
                            h.UpdatedBy is int by ? auteurs.GetValueOrDefault(by) : null, ferie)
                        : new PlanningItJourDto(d, null, null, null, null, ferie);
                }).ToList(),
                commentaires.GetValueOrDefault(m.Id)))
            .ToList();

        return new PlanningItSemaineDto(du, au, droits.ModifierTout, DateTime.UtcNow, lignes);
    }

    public async Task SaisirAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCaseRequest request, CancellationToken ct = default)
    {
        var erreur = PlanningItRegles.Controler(viewerUserId, droits, request);
        if (erreur is not null)
            throw new InvalidOperationException(erreur);
        await VerifierMembreAsync(request.UserId, ct);

        var ligne = await db.PlanningItHoraires.FirstOrDefaultAsync(h => h.UserId == request.UserId && h.Jour == request.Jour, ct);
        if (request.Statut is null)
        {
            if (ligne is not null)
                db.PlanningItHoraires.Remove(ligne);
        }
        else
        {
            if (ligne is null)
            {
                ligne = new PlanningItHoraire { UserId = request.UserId, Jour = request.Jour };
                db.PlanningItHoraires.Add(ligne);
            }
            ligne.Statut = request.Statut;
            ligne.Arrivee = request.Statut == PlanningItStatuts.Travail ? PlanningItRegles.LireHeure(request.Arrivee) : null;
            ligne.UpdatedAt = DateTime.UtcNow;
            ligne.UpdatedBy = viewerUserId;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task CommenterAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCommentaireRequest request, CancellationToken ct = default)
    {
        if (!droits.ModifierTout && !(droits.SaisirSoi && viewerUserId == request.UserId))
            throw new InvalidOperationException("Vous ne pouvez modifier que votre propre commentaire.");
        await VerifierMembreAsync(request.UserId, ct);

        var semaine = PlanningItRegles.Lundi(request.Semaine);
        var texte = (request.Texte ?? string.Empty).Trim();
        if (texte.Length > 500)
            texte = texte[..500];
        var ligne = await db.PlanningItCommentaires.FirstOrDefaultAsync(c => c.UserId == request.UserId && c.Semaine == semaine, ct);
        if (texte.Length == 0)
        {
            if (ligne is not null)
                db.PlanningItCommentaires.Remove(ligne);
        }
        else
        {
            if (ligne is null)
            {
                ligne = new PlanningItCommentaire { UserId = request.UserId, Semaine = semaine };
                db.PlanningItCommentaires.Add(ligne);
            }
            ligne.Texte = texte;
            ligne.UpdatedAt = DateTime.UtcNow;
            ligne.UpdatedBy = viewerUserId;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Membres actifs de la cellule IT (rôles Ingénieur et Support IT).</summary>
    private async Task<List<(int Id, string Nom, string? Role)>> MembresAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role != null)
            .Select(u => new { u.Id, u.LastName, u.FirstName, Role = u.Role.Name })
            .ToListAsync(ct);
        return users
            .Where(u => KyntusRoleNames.IsEquipeIt(u.Role))
            .Select(u => (u.Id, $"{u.LastName} {u.FirstName}".Trim(), (string?)u.Role))
            .ToList();
    }

    private async Task VerifierMembreAsync(int userId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => u.Role != null ? u.Role.Name : null)
            .FirstOrDefaultAsync(ct);
        if (!KyntusRoleNames.IsEquipeIt(role))
            throw new InvalidOperationException("Ce collaborateur ne fait pas partie de la cellule IT.");
    }
}
