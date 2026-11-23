using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v64 — Couverture des shifts pendant les congés, arrêts maladie et autres absences.
/// <para>
/// Les quotas d'un jour (RequiredCount) supposent tout l'effectif présent. Quand des agents sont
/// absents, il y a moins de présents que de places : sans règle, les premiers créneaux sont remplis
/// et les derniers (souvent la fermeture) restent vides. Ici, les places du jour sont redistribuées
/// sur l'effectif réellement disponible :
/// </para>
/// <list type="number">
/// <item>les créneaux critiques (ouverture, fermeture, cellule critique) reçoivent d'abord une personne ;</item>
/// <item>chaque autre créneau demandé reçoit ensuite une personne s'il en reste ;</item>
/// <item>le reste est réparti au prorata du besoin restant (plus forts restes), critiques prioritaires
/// à égalité, sans jamais dépasser le quota d'origine.</item>
/// </list>
/// Fonction pure : le générateur l'applique jour par jour et mode par mode.
/// </summary>
public static class CouvertureAbsences
{
    public sealed record Creneau(int Id, int Requis, bool Critique, int Ordre);

    public static bool EstCritique(SubServiceShiftConfig c) =>
        c.IsCriticalCell || c.ShiftKind is ShiftKind.Opening or ShiftKind.Closing;

    /// <summary>Places par créneau pour <paramref name="disponibles"/> personnes (quota d'origine si l'effectif suffit).</summary>
    public static Dictionary<int, int> Repartir(IReadOnlyList<Creneau> creneaux, int disponibles)
    {
        var resultat = creneaux.ToDictionary(c => c.Id, c => Math.Max(0, c.Requis));
        var total = resultat.Values.Sum();
        if (total == 0 || disponibles >= total)
            return resultat;

        var alloc = creneaux.ToDictionary(c => c.Id, _ => 0);
        var reste = Math.Max(0, disponibles);
        var ordre = creneaux
            .Where(c => c.Requis > 0)
            .OrderByDescending(c => c.Critique)
            .ThenBy(c => c.Ordre)
            .ThenBy(c => c.Id)
            .ToList();

        // 1. et 2. : une personne par créneau, critiques d'abord.
        foreach (var c in ordre)
        {
            if (reste == 0) break;
            alloc[c.Id] = 1;
            reste--;
        }

        // 3. : prorata du besoin restant, méthode des plus forts restes.
        if (reste > 0)
        {
            var besoins = ordre.ToDictionary(c => c.Id, c => c.Requis - alloc[c.Id]);
            var totalBesoin = besoins.Values.Sum();
            if (totalBesoin > 0)
            {
                var parts = ordre
                    .Select(c => (c, exact: (double)reste * besoins[c.Id] / totalBesoin))
                    .ToList();
                var distribue = 0;
                foreach (var (c, exact) in parts)
                {
                    var entier = Math.Min(besoins[c.Id], (int)Math.Floor(exact));
                    alloc[c.Id] += entier;
                    distribue += entier;
                }
                var restant = reste - distribue;
                foreach (var (c, _) in parts
                             .OrderByDescending(p => p.exact - Math.Floor(p.exact))
                             .ThenByDescending(p => p.c.Critique)
                             .ThenBy(p => p.c.Ordre)
                             .ThenBy(p => p.c.Id))
                {
                    if (restant == 0) break;
                    if (alloc[c.Id] >= c.Requis) continue;
                    alloc[c.Id]++;
                    restant--;
                }
                // Filet : places encore libres (arrondis) données aux créneaux qui en manquent, dans l'ordre.
                foreach (var c in ordre)
                {
                    while (restant > 0 && alloc[c.Id] < c.Requis)
                    {
                        alloc[c.Id]++;
                        restant--;
                    }
                }
            }
        }

        foreach (var c in creneaux)
            resultat[c.Id] = alloc[c.Id];
        return resultat;
    }

    /// <summary>
    /// Quotas du jour pour un groupe de créneaux (un mode) et l'effectif disponible ; null si le quota
    /// d'origine est tenable (aucun ajustement nécessaire).
    /// </summary>
    public static Dictionary<int, int>? QuotasDuJour(IReadOnlyList<SubServiceShiftConfig> creneaux, int disponibles)
    {
        var total = creneaux.Sum(c => Math.Max(0, c.RequiredCount));
        if (total == 0 || disponibles >= total)
            return null;
        return Repartir(
            creneaux.Select(c => new Creneau(c.Id, c.RequiredCount, EstCritique(c), c.DisplayOrder)).ToList(),
            disponibles);
    }
}
