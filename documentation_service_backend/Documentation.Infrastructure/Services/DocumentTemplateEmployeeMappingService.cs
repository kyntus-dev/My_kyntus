using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Documentation.Domain.Entities;

namespace Documentation.Infrastructure.Services;

public sealed class DocumentTemplateEmployeeMappingService(
    IPlanningEmployeeFieldsClient planning) : IDocumentTemplateEmployeeMappingService
{
    public async Task ApplyMatchesAsync(IList<TemplateVariableInput> variables, CancellationToken ct = default)
    {
        if (variables.Count == 0)
            return;

        var matches = await planning.MatchVariablesAsync(variables.ToList(), ct).ConfigureAwait(false);
        if (matches.Count == 0)
            return;

        DocumentTemplateEmployeeMappingApplier.Apply(variables, matches);
    }

    public async Task<IReadOnlyList<DocumentTemplateVariableResponse>> EnrichAsync(
        IReadOnlyList<DocumentTemplateVariableResponse> variables,
        CancellationToken ct = default)
    {
        if (variables.Count == 0)
            return variables;

        var catalog = await planning.GetCatalogAsync(enabledOnly: false, ct).ConfigureAwait(false);
        var byKey = catalog.ToDictionary(f => f.FieldKey, StringComparer.OrdinalIgnoreCase);
        var inputs = variables.Select(ToInput).ToList();
        var matches = await planning.MatchVariablesAsync(inputs, ct).ConfigureAwait(false);
        var matchByName = matches
            .GroupBy(m => m.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return variables.Select(v =>
        {
            matchByName.TryGetValue(v.Name, out var match);
            var sourceKey = v.SourceFieldKey;
            var unavailable = false;
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                unavailable = !byKey.TryGetValue(sourceKey, out var field) || !field.IsEnabled;
            }

            return v with
            {
                MappingUnavailable = unavailable,
                SuggestedFieldKey = match?.SuggestedFieldKey ?? v.SuggestedFieldKey,
                SuggestedFieldLabel = match?.SuggestedFieldLabel ?? v.SuggestedFieldLabel,
                MappingScore = match?.Score ?? v.MappingScore,
                MappingConfidence = v.MappingConfidence ?? match?.Confidence,
            };
        }).ToList();
    }

    public async Task<DocumentVariableMergeResult> ResolveAsync(
        Guid? beneficiaryUserId,
        IReadOnlyList<DocumentTemplateVariable> variables,
        IReadOnlyDictionary<string, string>? rhValues,
        CancellationToken ct = default)
    {
        var includeKeys = variables
            .Select(v => v.SourceFieldKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PlanningEmployeeFieldValues? planningValues = null;
        if (beneficiaryUserId is { } guid && guid != Guid.Empty)
        {
            planningValues = await planning
                .GetEmployeeFieldValuesAsync(guid, includeKeys, ct)
                .ConfigureAwait(false);
        }

        var unavailable = new HashSet<string>(
            planningValues?.UnavailableKeys ?? [],
            StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolutions = new List<ResolvedTemplateVariable>();

        foreach (var variable in variables)
        {
            string? dbValue = null;
            string? rhValue = null;
            var mappingUnavailable = false;
            if (!string.IsNullOrWhiteSpace(variable.SourceFieldKey) && planningValues is not null)
            {
                mappingUnavailable = unavailable.Contains(variable.SourceFieldKey);
                planningValues.Values.TryGetValue(variable.SourceFieldKey, out dbValue);
            }

            rhValues?.TryGetValue(variable.VariableName, out rhValue);
            var resolved = DocumentTemplateVariableValueResolver.Resolve(
                variable.VariableName,
                variable.SourceFieldKey,
                mappingUnavailable,
                dbValue,
                rhValue,
                variable.DefaultValue);
            values[variable.VariableName] = resolved.ResolvedValue;
            resolutions.Add(resolved);
        }

        return new DocumentVariableMergeResult(values, resolutions);
    }

    private static TemplateVariableInput ToInput(DocumentTemplateVariableResponse v) => new()
    {
        Name = v.Name,
        Type = v.Type,
        IsRequired = v.IsRequired,
        DefaultValue = v.DefaultValue,
        ValidationRule = v.ValidationRule,
        DisplayLabel = v.DisplayLabel,
        FormScope = v.FormScope,
        SourcePriority = v.SourcePriority,
        NormalizedName = v.NormalizedName,
        RawPlaceholder = v.RawPlaceholder,
        SourceFieldKey = v.SourceFieldKey,
        MappingConfidence = v.MappingConfidence,
        MappingConfirmed = v.MappingConfirmed,
    };
}
