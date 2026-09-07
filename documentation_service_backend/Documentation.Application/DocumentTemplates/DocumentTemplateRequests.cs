using MediatR;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Microsoft.AspNetCore.Http;

namespace Documentation.Application.DocumentTemplates;

public record GetDocumentTemplatesQuery : IRequest<IReadOnlyList<DocumentTemplateListItemResponse>>;

public sealed class GetDocumentTemplatesQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetDocumentTemplatesQuery, IReadOnlyList<DocumentTemplateListItemResponse>>
{
    public Task<IReadOnlyList<DocumentTemplateListItemResponse>> Handle(GetDocumentTemplatesQuery request, CancellationToken ct) =>
        templates.GetDocumentTemplatesAsync(ct);
}

public record GetDocumentTemplateQuery(Guid Id) : IRequest<DocumentTemplateDetailResponse>;

public sealed class GetDocumentTemplateQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetDocumentTemplateQuery, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(GetDocumentTemplateQuery request, CancellationToken ct) =>
        templates.GetDocumentTemplateAsync(request.Id, ct);
}

public record GetTemplateSourceFileUrlQuery(Guid Id) : IRequest<TemplateSourceFileUrlResponse>;

public sealed class GetTemplateSourceFileUrlQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetTemplateSourceFileUrlQuery, TemplateSourceFileUrlResponse>
{
    public Task<TemplateSourceFileUrlResponse> Handle(GetTemplateSourceFileUrlQuery request, CancellationToken ct) =>
        templates.GetTemplateSourceFileUrlAsync(request.Id, ct);
}

public record GetTemplateSourceFileQuery(Guid Id) : IRequest<TemplateFileExportDto>;

public sealed class GetTemplateSourceFileQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetTemplateSourceFileQuery, TemplateFileExportDto>
{
    public Task<TemplateFileExportDto> Handle(GetTemplateSourceFileQuery request, CancellationToken ct) =>
        templates.GetTemplateSourceFileAsync(request.Id, ct);
}

public record GetTemplatePreviewQuery(Guid Id) : IRequest<TemplateFileExportDto>;

public sealed class GetTemplatePreviewQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetTemplatePreviewQuery, TemplateFileExportDto>
{
    public Task<TemplateFileExportDto> Handle(GetTemplatePreviewQuery request, CancellationToken ct) =>
        templates.GetTemplatePreviewAsync(request.Id, ct);
}

public record CreateDocumentTemplateCommand(CreateDocumentTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class CreateDocumentTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<CreateDocumentTemplateCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(CreateDocumentTemplateCommand request, CancellationToken ct) =>
        templates.CreateDocumentTemplateAsync(request.Body, ct);
}

public record UpdateDocumentTemplateCommand(Guid Id, UpdateDocumentTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class UpdateDocumentTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<UpdateDocumentTemplateCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(UpdateDocumentTemplateCommand request, CancellationToken ct) =>
        templates.UpdateDocumentTemplateAsync(request.Id, request.Body, ct);
}

public record UpdateDocumentTemplateStatusCommand(Guid Id, UpdateTemplateStatusRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class UpdateDocumentTemplateStatusCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<UpdateDocumentTemplateStatusCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(UpdateDocumentTemplateStatusCommand request, CancellationToken ct) =>
        templates.UpdateDocumentTemplateStatusAsync(request.Id, request.Body, ct);
}

public record DeleteDocumentTemplateCommand(Guid Id) : IRequest;

public sealed class DeleteDocumentTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<DeleteDocumentTemplateCommand>
{
    public Task Handle(DeleteDocumentTemplateCommand request, CancellationToken ct) =>
        templates.DeleteDocumentTemplateAsync(request.Id, ct);
}

public record UploadTemplateFromFileCommand(
    IFormFile File,
    string Code,
    string Name,
    string? Description,
    Guid? DocumentTypeId,
    bool StaticDocument,
    bool RequiresPilotUpload) : IRequest<DocumentTemplateDetailResponse>;

public sealed class UploadTemplateFromFileCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<UploadTemplateFromFileCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(UploadTemplateFromFileCommand request, CancellationToken ct) =>
        templates.UploadTemplateFromFileAsync(
            request.File,
            request.Code,
            request.Name,
            request.Description,
            request.DocumentTypeId,
            request.StaticDocument,
            request.RequiresPilotUpload,
            ct);
}

public record UploadTemplateFromJsonCommand(UploadTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class UploadTemplateFromJsonCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<UploadTemplateFromJsonCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(UploadTemplateFromJsonCommand request, CancellationToken ct) =>
        templates.UploadTemplateFromJsonAsync(request.Body, ct);
}

public record AnalyzeInternalEngineTemplateQuery(InternalEngineTemplateRequest Body) : IRequest<InternalEngineAnalysisResponse>;

public sealed class AnalyzeInternalEngineTemplateQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<AnalyzeInternalEngineTemplateQuery, InternalEngineAnalysisResponse>
{
    public Task<InternalEngineAnalysisResponse> Handle(AnalyzeInternalEngineTemplateQuery request, CancellationToken ct) =>
        templates.AnalyzeInternalEngineTemplateAsync(request.Body);
}

public record CreateInternalEngineTemplateCommand(InternalEngineTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class CreateInternalEngineTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<CreateInternalEngineTemplateCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(CreateInternalEngineTemplateCommand request, CancellationToken ct) =>
        templates.CreateInternalEngineTemplateAsync(request.Body, ct);
}

public record GenerateTemplateFromAiCommand(AiGenerateTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class GenerateTemplateFromAiCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GenerateTemplateFromAiCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(GenerateTemplateFromAiCommand request, CancellationToken ct) =>
        templates.GenerateTemplateFromAiAsync(request.Body, ct);
}

public record GenerateRuleBasedTemplateCommand(RuleGenerateTemplateRequest Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class GenerateRuleBasedTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GenerateRuleBasedTemplateCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(GenerateRuleBasedTemplateCommand request, CancellationToken ct) =>
        templates.GenerateRuleBasedTemplateAsync(request.Body, ct);
}

public record CreateTemplateVersionCommand(Guid Id, CreateTemplateVersionRequest Body) : IRequest<DocumentTemplateVersionResponse>;

public sealed class CreateTemplateVersionCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<CreateTemplateVersionCommand, DocumentTemplateVersionResponse>
{
    public Task<DocumentTemplateVersionResponse> Handle(CreateTemplateVersionCommand request, CancellationToken ct) =>
        templates.CreateTemplateVersionAsync(request.Id, request.Body, ct);
}

public record PutCurrentVersionVariablesCommand(Guid Id, IReadOnlyList<TemplateVariableInput> Body) : IRequest<DocumentTemplateDetailResponse>;

public sealed class PutCurrentVersionVariablesCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<PutCurrentVersionVariablesCommand, DocumentTemplateDetailResponse>
{
    public Task<DocumentTemplateDetailResponse> Handle(PutCurrentVersionVariablesCommand request, CancellationToken ct) =>
        templates.PutCurrentVersionVariablesAsync(request.Id, request.Body, ct);
}

public record GetTemplateVersionsQuery(Guid Id) : IRequest<IReadOnlyList<DocumentTemplateVersionResponse>>;

public sealed class GetTemplateVersionsQueryHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GetTemplateVersionsQuery, IReadOnlyList<DocumentTemplateVersionResponse>>
{
    public Task<IReadOnlyList<DocumentTemplateVersionResponse>> Handle(GetTemplateVersionsQuery request, CancellationToken ct) =>
        templates.GetTemplateVersionsAsync(request.Id, ct);
}

public record TestRunTemplateCommand(Guid Id, TemplateTestRunRequest Body) : IRequest<TemplateTestRunResponse>;

public sealed class TestRunTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<TestRunTemplateCommand, TemplateTestRunResponse>
{
    public Task<TemplateTestRunResponse> Handle(TestRunTemplateCommand request, CancellationToken ct) =>
        templates.TestRunTemplateAsync(request.Id, request.Body, ct);
}

public record GenerateFromTemplateCommand(Guid Id, DocumentTemplateGenerateRequest? Body) : IRequest<DocumentTemplateGenerateResponse>;

public sealed class GenerateFromTemplateCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<GenerateFromTemplateCommand, DocumentTemplateGenerateResponse>
{
    public Task<DocumentTemplateGenerateResponse> Handle(GenerateFromTemplateCommand request, CancellationToken ct) =>
        templates.GenerateFromTemplateAsync(request.Id, request.Body, ct);
}

public record ResolveTemplateEmployeeValuesCommand(Guid Id, ResolveTemplateEmployeeValuesRequest Body)
    : IRequest<TemplateEmployeeResolutionResponse>;

public sealed class ResolveTemplateEmployeeValuesCommandHandler(IDocumentTemplateAppService templates)
    : IRequestHandler<ResolveTemplateEmployeeValuesCommand, TemplateEmployeeResolutionResponse>
{
    public Task<TemplateEmployeeResolutionResponse> Handle(
        ResolveTemplateEmployeeValuesCommand request,
        CancellationToken ct) =>
        templates.ResolveEmployeeValuesAsync(request.Id, request.Body, ct);
}
