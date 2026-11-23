using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v63 — Affectation de référence d'un agent : celle de sa dernière réaffectation (écran
/// Réaffectations ou fiche employé, historique <c>ReaffectationsAgents</c>).
/// Une synchronisation automatique (annuaire, Organisation RH, miroir) ne remet jamais l'agent dans
/// un service qu'il a quitté par réaffectation : l'ancienne affectation n'est jamais réintroduite
/// d'elle-même, même après plusieurs régénérations de planning. Seule une nouvelle réaffectation
/// peut l'y renvoyer. L'historique des anciennes affectations est conservé tel quel.
/// </summary>
public static class AffectationDeReference
{
    /// <param name="actuel">Service actuel de l'agent dans le Planning.</param>
    /// <param name="propose">Service proposé par la synchronisation.</param>
    /// <param name="historique">Réaffectations de l'agent, la plus récente en premier.</param>
    public static bool RetourInterdit(
        int? actuel, int? propose, IReadOnlyList<(int? Ancien, int? Nouveau)> historique)
    {
        if (propose is null || actuel == propose || historique.Count == 0)
            return false;
        // L'affectation actuelle ne vient pas de la dernière réaffectation : rien à protéger.
        if (historique[0].Nouveau != actuel)
            return false;
        return historique.Any(h => h.Ancien == propose);
    }

    public static async Task<bool> RetourInterditAsync(
        AppDbContext db, int userId, int? actuel, int? propose, CancellationToken ct = default)
    {
        if (propose is null || actuel == propose)
            return false;
        var historique = await db.ReaffectationsAgents.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.AncienSubServiceId, r.NouveauSubServiceId })
            .Take(50)
            .ToListAsync(ct);
        return RetourInterdit(actuel, propose,
            historique.Select(h => (h.AncienSubServiceId, h.NouveauSubServiceId)).ToList());
    }
}
