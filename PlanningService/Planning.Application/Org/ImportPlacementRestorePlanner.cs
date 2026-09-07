namespace Planning.Application.Org;

public readonly record struct PlacementAppearance(int SubServiceId, DateTime PlanningCreatedAt);

public static class ImportPlacementRestorePlanner
{
    /// <summary>
    /// Cellule / service d'origine : le SubService le plus fréquent sur les plannings
    /// créés avant l'import. Les rotations après l'import ne sont pas dans cet ensemble.
    /// </summary>
    public static int? InferPreviousSubServiceId(
        IReadOnlyList<PlacementAppearance> appearances,
        DateTime importStartedAt)
    {
        var prior = appearances.Where(a => a.PlanningCreatedAt < importStartedAt).ToList();
        if (prior.Count == 0)
            return null;

        return prior
            .GroupBy(a => a.SubServiceId)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(x => x.PlanningCreatedAt))
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    public static bool ShouldRestore(int? currentSubServiceId, int? previousSubServiceId, bool hasLaterOrgAssignment)
    {
        if (hasLaterOrgAssignment)
            return false;
        if (!previousSubServiceId.HasValue)
            return false;
        return currentSubServiceId != previousSubServiceId;
    }
}
