namespace Documentation.Application;

public sealed record DocumentVariableMergeResult(
    Dictionary<string, string> Values,
    IReadOnlyList<ResolvedTemplateVariable> Resolutions);

public sealed record ResolvedTemplateVariable(
    string Name,
    bool FilledFromDb,
    bool NeedsRhInput,
    bool UnavailableMapping,
    string? SourceFieldKey,
    string? DbValue,
    string ResolvedValue);

public static class DocumentTemplateVariableValueResolver
{
    public static ResolvedTemplateVariable Resolve(
        string name,
        string? sourceFieldKey,
        bool mappingUnavailable,
        string? dbValue,
        string? rhValue,
        string? defaultValue)
    {
        var db = Normalize(dbValue);
        if (!string.IsNullOrEmpty(db))
        {
            return new ResolvedTemplateVariable(
                name, true, false, mappingUnavailable, sourceFieldKey, db, db);
        }

        var rh = Normalize(rhValue);
        if (!string.IsNullOrEmpty(rh))
        {
            return new ResolvedTemplateVariable(
                name, false, false, mappingUnavailable, sourceFieldKey, dbValue, rh);
        }

        var fallback = Normalize(defaultValue);
        if (!string.IsNullOrEmpty(fallback))
        {
            return new ResolvedTemplateVariable(
                name, false, false, mappingUnavailable, sourceFieldKey, dbValue, fallback);
        }

        return new ResolvedTemplateVariable(
            name, false, true, mappingUnavailable, sourceFieldKey, dbValue, string.Empty);
    }

    public static string Normalize(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(v))
            return string.Empty;

        var lower = v.ToLowerInvariant();
        if (lower is "-" or "\u2014" or "_" or "x" or "(x)" or "()" or "( )" or "n/a" or "na")
            return string.Empty;
        if (v.All(ch => ch is '_' or '-' or '\u2014' || char.IsWhiteSpace(ch)))
            return string.Empty;
        return v;
    }
}
