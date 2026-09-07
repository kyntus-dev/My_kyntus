using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Sépare les templates weekday des templates samedi optionnels.
/// Sans ligne samedi dédiée, le samedi reprend les shifts weekday (quota moitié).
/// </summary>
internal static class ShiftConfigScope
{
    public static List<SubServiceShiftConfig> Weekday(IEnumerable<SubServiceShiftConfig> configs)
        => configs.Where(c => !c.AppliesToSaturday).ToList();

    public static List<SubServiceShiftConfig> Saturday(IEnumerable<SubServiceShiftConfig> configs)
    {
        var list = configs as IList<SubServiceShiftConfig> ?? configs.ToList();
        var sat = list.Where(c => c.AppliesToSaturday).ToList();
        return sat.Count > 0 ? sat : Weekday(list);
    }

    public static bool HasDistinctSaturday(IEnumerable<SubServiceShiftConfig> configs)
        => configs.Any(c => c.AppliesToSaturday);
}
