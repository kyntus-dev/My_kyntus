using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v51 — Service Planning « de référence » pour un service d'Organisation RH (PrimeServiceId).
/// <para>
/// Normalement un service d'Organisation correspond à un seul service Planning. Si le miroir en
/// contient plusieurs (doublon historique), le choix doit être <b>déterministe</b> : sinon la
/// synchronisation range les agents tantôt sur l'un, tantôt sur l'autre, et le planning (porté par
/// l'un des deux) se retrouve vide. Ordre de préférence :
/// </para>
/// <list type="number">
///   <item>non archivé ;</item>
///   <item>porteur d'un modèle de shifts (c'est lui que la génération utilise) ;</item>
///   <item>porteur du plus grand nombre d'agents actifs ;</item>
///   <item>identifiant le plus petit (le plus ancien).</item>
/// </list>
/// </summary>
public static class SubServiceCanonique
{
    /// <summary>Service de référence pour un PrimeServiceId, ou null s'il n'y en a aucun.</summary>
    public static async Task<int?> ResoudreAsync(AppDbContext db, string? primeServiceId, CancellationToken ct = default)
    {
        var prime = (primeServiceId ?? string.Empty).Trim();
        if (prime.Length == 0) return null;
        var table = await TableAsync(db, ct, prime);
        return table.TryGetValue(prime, out var id) ? id : null;
    }

    /// <summary>
    /// Table PrimeServiceId → service de référence (comparaison insensible à la casse).
    /// <paramref name="seulement"/> limite le calcul à certains identifiants (facultatif).
    /// </summary>
    public static async Task<Dictionary<string, int>> TableAsync(
        AppDbContext db,
        CancellationToken ct = default,
        params string[] seulement)
    {
        // Comparaison insensible à la casse, comme la table renvoyée.
        var filtre = seulement
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLower())
            .ToList();

        var query = db.SubServices.AsNoTracking()
            .Where(s => s.PrimeServiceId != null && s.PrimeServiceId != "");
        if (filtre.Count > 0)
            query = query.Where(s => filtre.Contains(s.PrimeServiceId!.Trim().ToLower()));

        var candidats = await query
            .Select(s => new
            {
                s.Id,
                Prime = s.PrimeServiceId!,
                s.IsArchived,
                AvecModele = db.SubServiceShiftConfigs.Any(c => c.SubServiceId == s.Id && c.IsTemplate),
                Actifs = db.Users.Count(u => u.SubServiceId == s.Id && u.IsActive),
            })
            .ToListAsync(ct);

        return candidats
            .GroupBy(c => c.Prime.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(c => c.IsArchived)
                    .ThenByDescending(c => c.AvecModele)
                    .ThenByDescending(c => c.Actifs)
                    .ThenBy(c => c.Id)
                    .First().Id,
                StringComparer.OrdinalIgnoreCase);
    }
}
