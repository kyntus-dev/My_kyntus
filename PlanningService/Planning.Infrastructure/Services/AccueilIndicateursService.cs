using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>v87 — Historique personnel des indicateurs de l'accueil (table accueil_indicateurs_jour).</summary>
public sealed partial class AccueilIndicateursService(AppDbContext db) : IAccueilIndicateursService
{
    public const int MaxCles = 40;
    public const int MaxJours = 120;
    /// <summary>Conservation : au-delà, les relevés de la personne sont effacés lors de son enregistrement suivant.</summary>
    public const int ConservationJours = 400;

    [GeneratedRegex("^[a-z0-9-]{1,60}$")]
    private static partial Regex CleValide();

    public async Task<int> EnregistrerAsync(Guid subjectId, DateOnly jour, IReadOnlyDictionary<string, decimal> valeurs, CancellationToken ct = default)
    {
        if (subjectId == Guid.Empty || valeurs.Count == 0) return 0;
        var retenues = valeurs
            .Where(kv => CleValide().IsMatch(kv.Key) && kv.Value is > -1_000_000_000m and < 1_000_000_000m)
            .Take(MaxCles)
            .ToDictionary(kv => kv.Key, kv => decimal.Round(kv.Value, 2));
        if (retenues.Count == 0) return 0;

        var existantes = await db.IndicateursAccueil
            .Where(x => x.SubjectId == subjectId && x.Jour == jour)
            .ToListAsync(ct);
        var maintenant = DateTime.UtcNow;
        foreach (var (cle, valeur) in retenues)
        {
            var ligne = existantes.FirstOrDefault(x => x.Cle == cle);
            if (ligne is null)
            {
                db.IndicateursAccueil.Add(new IndicateurAccueilJour { SubjectId = subjectId, Jour = jour, Cle = cle, Valeur = valeur, MisAJourLe = maintenant });
            }
            else if (ligne.Valeur != valeur)
            {
                ligne.Valeur = valeur;
                ligne.MisAJourLe = maintenant;
            }
        }

        var limite = jour.AddDays(-ConservationJours);
        var anciennes = await db.IndicateursAccueil.Where(x => x.SubjectId == subjectId && x.Jour < limite).ToListAsync(ct);
        if (anciennes.Count > 0) db.IndicateursAccueil.RemoveRange(anciennes);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Deux onglets ouverts au même moment : l'autre enregistrement a gagné, rien à faire.
            return 0;
        }
        return retenues.Count;
    }

    public async Task<IReadOnlyList<JourIndicateursAccueil>> LireAsync(Guid subjectId, DateOnly aujourdhui, int jours, CancellationToken ct = default)
    {
        if (subjectId == Guid.Empty) return [];
        var n = Math.Clamp(jours, 1, MaxJours);
        var debut = aujourdhui.AddDays(-n);
        var lignes = await db.IndicateursAccueil.AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.Jour >= debut && x.Jour <= aujourdhui)
            .Select(x => new { x.Jour, x.Cle, x.Valeur })
            .ToListAsync(ct);
        return lignes
            .GroupBy(x => x.Jour)
            .OrderBy(g => g.Key)
            .Select(g => new JourIndicateursAccueil(g.Key, g.ToDictionary(x => x.Cle, x => x.Valeur)))
            .ToList();
    }
}
