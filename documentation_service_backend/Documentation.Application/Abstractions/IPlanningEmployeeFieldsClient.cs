using Documentation.Application;
using Documentation.Application.Api;

namespace Documentation.Application.Abstractions;

public sealed class PlanningEmployeeFieldCatalogItem
{
    public string FieldKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsSystemField { get; set; }
    public string DataType { get; set; } = "text";
}

public sealed class PlanningEmployeeFieldValues
{
    public Guid EmployeeGuid { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> UnavailableKeys { get; set; } = [];
}

public interface IPlanningEmployeeFieldsClient
{
    Task<IReadOnlyList<PlanningEmployeeFieldCatalogItem>> GetCatalogAsync(
        bool enabledOnly,
        CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeFieldMatchHint>> MatchVariablesAsync(
        IReadOnlyList<TemplateVariableInput> variables,
        CancellationToken ct = default);

    Task<PlanningEmployeeFieldValues?> GetEmployeeFieldValuesAsync(
        Guid employeeGuid,
        IReadOnlyList<string>? includeKeys,
        CancellationToken ct = default);
}
