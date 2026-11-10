namespace Planning.Application.Pointage;

/// <summary>
/// Pointeuses d'accès à une zone (armoire technique…) : leurs badgeages ne comptent ni comme arrivée,
/// ni comme départ, ni comme pause.
/// </summary>
public static class PointageTerminaux
{
    public static IReadOnlyList<string> Motifs(string? configuration) =>
        (configuration ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(PointageNameMatcher.Normaliser)
            .Where(m => m.Length > 0)
            .ToList();

    /// <summary>Vrai si le nom ou le numéro de série de la pointeuse contient l'un des motifs.</summary>
    public static bool EstExclu(string? alias, string? numeroSerie, IReadOnlyList<string> motifs)
    {
        if (motifs.Count == 0) return false;
        var a = PointageNameMatcher.Normaliser(alias);
        var sn = PointageNameMatcher.Normaliser(numeroSerie);
        return motifs.Any(m => (a.Length > 0 && a.Contains(m, StringComparison.Ordinal))
                               || (sn.Length > 0 && sn.Contains(m, StringComparison.Ordinal)));
    }
}
