using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public sealed class EmployeeDocumentFieldQueryService(
    IEmployeeFieldService fieldService,
    IUserService userService,
    EmployeeImportColumnMatcher matcher) : IEmployeeDocumentFieldQueryService
{
    public async Task<List<EmployeeVariableMatchDto>> MatchVariablesAsync(
        IReadOnlyList<EmployeeVariableMatchSourceDto> sources,
        CancellationToken ct = default)
    {
        var fields = await fieldService.GetAllAsync(enabledOnly: false, ct);
        return matcher.MatchVariableSources(sources, fields).ToList();
    }

    public async Task<EmployeeDocumentFieldValuesDto?> GetFieldValuesAsync(
        Guid employeeGuid,
        IReadOnlyList<string>? includeKeys,
        CancellationToken ct = default)
    {
        var user = await userService.GetUserByGuidAsync(employeeGuid);
        if (user is null)
            return null;

        var catalog = await fieldService.GetAllAsync(enabledOnly: false, ct);
        var byKey = catalog.ToDictionary(f => f.FieldKey, StringComparer.OrdinalIgnoreCase);
        var flattened = EmployeeDocumentFieldValueFlattener.Flatten(user);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var unavailable = new List<string>();

        foreach (var field in catalog.Where(f => f.IsEnabled))
        {
            flattened.TryGetValue(field.FieldKey, out var value);
            values[field.FieldKey] = value;
        }

        foreach (var raw in includeKeys ?? [])
        {
            var key = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key) || values.ContainsKey(key))
                continue;

            flattened.TryGetValue(key, out var value);
            values[key] = value;

            if (!byKey.TryGetValue(key, out var field) || !field.IsEnabled)
                unavailable.Add(key);
        }

        return new EmployeeDocumentFieldValuesDto
        {
            EmployeeGuid = employeeGuid,
            Values = values,
            UnavailableKeys = unavailable,
        };
    }
}
