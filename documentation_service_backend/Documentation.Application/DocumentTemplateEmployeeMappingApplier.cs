using Documentation.Application.Api;

namespace Documentation.Application;

public sealed record EmployeeFieldMatchHint(
    string SourceName,
    string? SuggestedFieldKey,
    string? SuggestedFieldLabel,
    double Score,
    string Confidence,
    bool IsEnabled);

public static class DocumentTemplateEmployeeMappingApplier
{
    public static void Apply(IList<TemplateVariableInput> variables, IReadOnlyList<EmployeeFieldMatchHint> matches)
    {
        var byName = matches
            .GroupBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            if (!byName.TryGetValue(variable.Name, out var match))
                continue;

            variable.MappingConfidence = match.Confidence;
            if (string.Equals(match.Confidence, "high", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(match.SuggestedFieldKey)
                && match.IsEnabled)
            {
                variable.SourceFieldKey = match.SuggestedFieldKey;
                variable.MappingConfirmed = true;
                variable.FormScope = "db";
                variable.SourcePriority = 10;
                continue;
            }

            if (string.Equals(match.Confidence, "medium", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(match.SuggestedFieldKey)
                && match.IsEnabled)
            {
                variable.SourceFieldKey = match.SuggestedFieldKey;
                variable.MappingConfirmed = false;
                variable.FormScope = "db";
                variable.SourcePriority = 10;
                continue;
            }

            variable.SourceFieldKey = null;
            variable.MappingConfirmed = false;
            variable.FormScope = "hr";
            variable.SourcePriority = 30;
        }
    }
}
