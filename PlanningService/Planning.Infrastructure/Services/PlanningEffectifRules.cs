namespace Planning.Infrastructure.Services;

/// <summary>
/// v61 — Règles de synchronisation de l'effectif d'un planning (pures, testables).
/// <list type="bullet">
/// <item>Un agent sorti (compte désactivé ou date de sortie passée) ou affecté ailleurs n'apparaît plus
/// dans les jours à venir du planning ; ses journées passées, et toute journée déjà déclarée,
/// restent intactes (historique de présence et de paie conservé).</item>
/// <item>Un agent arrivé dans la cellule (création, réactivation, réaffectation) est ajouté aux jours
/// encore modifiables de la semaine, à partir de sa date d'entrée.</item>
/// </list>
/// </summary>
public static class PlanningEffectifRules
{
    /// <summary>État d'un agent utile à la synchronisation.</summary>
    public sealed record Agent(int UserId, bool Actif, int? CelluleId, DateOnly? DateEntree, DateOnly? DateSortie);

    /// <summary>L'agent doit-il figurer dans le planning de cette cellule ce jour-là ?</summary>
    public static bool DoitFigurer(Agent agent, int celluleId, DateOnly jour)
    {
        if (agent.CelluleId != celluleId) return false;
        if (agent.DateEntree is DateOnly entree && jour < entree) return false;
        if (agent.DateSortie is DateOnly sortie && jour > sortie) return false;
        return agent.Actif;
    }

    /// <summary>
    /// Journée à masquer à l'affichage : agent désactivé ou sorti, après sa date de sortie (sans date :
    /// à partir d'aujourd'hui). Les jours d'avant restent visibles (historique).
    /// </summary>
    public static bool MasqueeApresSortie(bool actif, DateOnly? dateSortie, DateOnly jour, DateOnly aujourdhui)
    {
        if (dateSortie is DateOnly sortie) return jour > sortie;
        return !actif && jour >= aujourdhui;
    }

    /// <summary>Un créneau proposé à un agent ajouté : besoin restant, ordre d'affichage, pauses.</summary>
    public sealed record Creneau(int ConfigId, int Requis, int Affectes, int Ordre);

    /// <summary>
    /// Créneau d'un agent ajouté : celui qui manque le plus de monde ce jour-là (requis − affectés),
    /// puis le premier dans l'ordre d'affichage. Null si aucun créneau n'est configuré.
    /// </summary>
    public static int? ChoisirCreneau(IEnumerable<Creneau> creneaux) =>
        creneaux
            .OrderByDescending(c => c.Requis - c.Affectes)
            .ThenBy(c => c.Ordre)
            .ThenBy(c => c.ConfigId)
            .Select(c => (int?)c.ConfigId)
            .FirstOrDefault();

    /// <summary>Pause la moins chargée parmi les créneaux de pause autorisés (null si aucun).</summary>
    public static TimeOnly? ChoisirPause(IReadOnlyList<TimeOnly> autorisees, IReadOnlyDictionary<TimeOnly, int> occupees) =>
        autorisees.Count == 0
            ? null
            : autorisees.OrderBy(p => occupees.TryGetValue(p, out var n) ? n : 0).ThenBy(p => p).First();
}
