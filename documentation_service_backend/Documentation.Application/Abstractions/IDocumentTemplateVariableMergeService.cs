using Documentation.Domain.Entities;

namespace Documentation.Application.Abstractions;

public interface IDocumentTemplateVariableMergeService
{
    Task<Dictionary<string, string>> MergeAsync(
        Guid? beneficiaryUserId,
        Guid? documentRequestId,
        IReadOnlyDictionary<string, string>? source,
        CancellationToken ct = default);

    Task<DocumentVariableMergeResult> MergeForTemplateAsync(
        Guid? beneficiaryUserId,
        Guid? documentRequestId,
        IReadOnlyList<DocumentTemplateVariable> templateVariables,
        IReadOnlyDictionary<string, string>? source,
        CancellationToken ct = default);

    Task ApplyAiRefinementAsync(
        Dictionary<string, string> merged,
        Guid templateVersionId,
        string? documentTitle,
        CancellationToken ct = default);

    void EnsureFrenchDateAlias(Dictionary<string, string> dict);
}
