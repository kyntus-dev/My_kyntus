using Documentation.Application;
using Documentation.Application.Api;
using Documentation.Domain.Entities;

namespace Documentation.Application.Abstractions;

public interface IDocumentTemplateEmployeeMappingService
{
    Task ApplyMatchesAsync(IList<TemplateVariableInput> variables, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentTemplateVariableResponse>> EnrichAsync(
        IReadOnlyList<DocumentTemplateVariableResponse> variables,
        CancellationToken ct = default);

    Task<DocumentVariableMergeResult> ResolveAsync(
        Guid? beneficiaryUserId,
        IReadOnlyList<DocumentTemplateVariable> variables,
        IReadOnlyDictionary<string, string>? rhValues,
        CancellationToken ct = default);
}
