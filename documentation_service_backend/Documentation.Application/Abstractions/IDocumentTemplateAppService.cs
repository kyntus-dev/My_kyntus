using Documentation.Application.Api;
using Microsoft.AspNetCore.Http;

namespace Documentation.Application.Abstractions;

public interface IDocumentTemplateAppService
{
    Task<IReadOnlyList<DocumentTemplateListItemResponse>> GetDocumentTemplatesAsync(CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> GetDocumentTemplateAsync(Guid id, CancellationToken ct = default);

    Task<TemplateSourceFileUrlResponse> GetTemplateSourceFileUrlAsync(Guid id, CancellationToken ct = default);

    Task<TemplateFileExportDto> GetTemplateSourceFileAsync(Guid id, CancellationToken ct = default);

    Task<TemplateFileExportDto> GetTemplatePreviewAsync(Guid id, CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> CreateDocumentTemplateAsync(CreateDocumentTemplateRequest body, CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> UpdateDocumentTemplateAsync(
        Guid id,
        UpdateDocumentTemplateRequest body,
        CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> UpdateDocumentTemplateStatusAsync(
        Guid id,
        UpdateTemplateStatusRequest body,
        CancellationToken ct = default);

    Task DeleteDocumentTemplateAsync(Guid id, CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> UploadTemplateFromFileAsync(
        IFormFile file,
        string code,
        string name,
        string? description,
        Guid? documentTypeId,
        bool staticDocument,
        bool requiresPilotUpload,
        CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> UploadTemplateFromJsonAsync(UploadTemplateRequest body, CancellationToken ct = default);

    Task<InternalEngineAnalysisResponse> AnalyzeInternalEngineTemplateAsync(InternalEngineTemplateRequest body);

    Task<DocumentTemplateDetailResponse> CreateInternalEngineTemplateAsync(
        InternalEngineTemplateRequest body,
        CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> GenerateTemplateFromAiAsync(AiGenerateTemplateRequest body, CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> GenerateRuleBasedTemplateAsync(
        RuleGenerateTemplateRequest body,
        CancellationToken ct = default);

    Task<DocumentTemplateVersionResponse> CreateTemplateVersionAsync(
        Guid id,
        CreateTemplateVersionRequest body,
        CancellationToken ct = default);

    Task<DocumentTemplateDetailResponse> PutCurrentVersionVariablesAsync(
        Guid id,
        IReadOnlyList<TemplateVariableInput> body,
        CancellationToken ct = default);

    Task<IReadOnlyList<DocumentTemplateVersionResponse>> GetTemplateVersionsAsync(Guid id, CancellationToken ct = default);

    Task<TemplateTestRunResponse> TestRunTemplateAsync(
        Guid id,
        TemplateTestRunRequest body,
        CancellationToken ct = default);

    Task<DocumentTemplateGenerateResponse> GenerateFromTemplateAsync(
        Guid templateId,
        DocumentTemplateGenerateRequest? body,
        CancellationToken ct = default);

    Task<TemplateEmployeeResolutionResponse> ResolveEmployeeValuesAsync(
        Guid templateId,
        ResolveTemplateEmployeeValuesRequest body,
        CancellationToken ct = default);
}
