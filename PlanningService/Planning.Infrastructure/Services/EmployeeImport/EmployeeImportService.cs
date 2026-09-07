using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public partial class EmployeeImportService(
    AppDbContext db,
    EmployeeImportFileParser parser,
    EmployeeImportColumnMatcher matcher,
    IEmployeeImportConfigService configService,
    IEmployeeFieldService fieldService,
    IEmployeeImportSessionStore sessionStore,
    IEmployeeImportExecutor executor,
    IEmployeeImportExecuteQueue executeQueue,
    IEmployeeImportOrgResolver orgResolver,
    IEmployeeImportOrgGapAnalyzer orgGapAnalyzer,
    EmployeeImportTemplateBuilder templateBuilder,
    IPlanningOrgMirrorService orgMirror,
    IDirectoryOrgWriteClient directoryOrg,
    IEmployeeImportCredentialsStore credentialsStore,
    IHttpContextAccessor httpContextAccessor,
    IUserService userService) : IEmployeeImportService
{
    private readonly AppDbContext _db = db;
    private readonly IEmployeeImportCredentialsStore _credentialsStore = credentialsStore;
    public async Task<EmployeeImportAnalyzeResponse> AnalyzeAsync(IFormFile file, CancellationToken ct = default)
    {
        var auth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        await orgMirror.SyncFromDirectoryOverviewAsync(
            string.IsNullOrWhiteSpace(auth) ? null : auth, ct);

        var parsed = parser.Parse(file);
        if (parsed.Headers.Count == 0)
            throw new InvalidOperationException("Le fichier ne contient pas d'en-têtes.");

        var activeFields = (await configService.GetConfigAsync(ct)).Where(f => f.IsEnabled).ToList();
        var targets = activeFields
            .Select(f => new FieldMatchTarget(f.FieldKey, f.Label, f.Aliases))
            .ToList();

        var matches = matcher.MatchHeaders(parsed.Headers, targets);

        await using var fileBuffer = new MemoryStream();
        await file.CopyToAsync(fileBuffer, ct);
        var fileBytes = fileBuffer.ToArray();
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? GuessContentType(file.FileName)
            : file.ContentType;

        var sessionId = await sessionStore.SaveAsync(file.FileName, parsed, fileBytes, contentType, ct);

        var columnMap = matches
            .Where(m => !string.IsNullOrWhiteSpace(m.SuggestedFieldKey))
            .ToDictionary(m => m.ColumnIndex, m => m.SuggestedFieldKey!);

        var preview = parsed.Rows
            .Take(50)
            .Select(row => BuildPreviewRow(row, columnMap))
            .ToList();

        var alerts = new List<string>();
        if (!matches.Any(m => m.SuggestedFieldKey == "email"))
            alerts.Add("Aucune colonne email détectée — les lignes seront ignorées sans identifiant.");
        if (!matches.Any(m => m.SuggestedFieldKey == "role"))
            alerts.Add("Aucune colonne rôle détectée — la création échouera pour les nouveaux employés.");

        var orgSnapshot = await orgResolver.LoadSnapshotAsync(ct);
        var orgAnalysis = orgGapAnalyzer.AnalyzeFile(parsed, columnMap, orgSnapshot, orgSnapshot.Roles);

        foreach (var issue in orgAnalysis.OrgLineIssues.Where(i => i.Severity == "error"))
            alerts.Add($"Ligne {issue.LineNumber} : {issue.Message}");

        await EnrichPendingOperationalDepartmentsAsync(orgAnalysis.PendingOrgCreations, alerts, ct);

        return new EmployeeImportAnalyzeResponse
        {
            ImportSessionId = sessionId,
            FileName = file.FileName,
            TotalRows = parsed.Rows.Count,
            Headers = parsed.Headers.ToList(),
            SuggestedMappings = matches.Select(m => new EmployeeImportColumnMappingDto
            {
                ColumnIndex = m.ColumnIndex,
                SourceHeader = m.SourceHeader,
                SuggestedFieldKey = m.SuggestedFieldKey,
                Confidence = m.Confidence
            }).ToList(),
            PreviewRows = preview,
            Alerts = alerts,
            ActiveFields = activeFields,
            PendingOrgCreations = orgAnalysis.PendingOrgCreations,
            ResolvedRows = orgAnalysis.ResolvedRows,
            OrgLineIssues = orgAnalysis.OrgLineIssues
        };
    }

    public async Task<EmployeeImportPreviewResponse> PreviewAsync(
        EmployeeImportPreviewRequest request,
        CancellationToken ct = default)
    {
        var parsed = await sessionStore.GetAsync(request.ImportSessionId, ct)
            ?? throw new InvalidOperationException("Session d'import expirée ou introuvable. Re-analysez le fichier.");

        var resolvedMappings = await fieldService.ResolveImportMappingsAsync(
            request.Mappings, parsed.Headers, ct);
        var activeFields = (await configService.GetConfigAsync(ct)).Where(f => f.IsEnabled).ToList();
        var columnMap = EmployeeImportMappingHelper.BuildColumnMap(resolvedMappings, activeFields);

        const int maxTake = 200;
        var take = request.Take <= 0 ? 50 : Math.Clamp(request.Take, 1, maxTake);
        var skip = Math.Clamp(request.Skip, 0, Math.Max(0, parsed.Rows.Count));

        var previewRows = parsed.Rows
            .Skip(skip)
            .Take(take)
            .Select(row => BuildPreviewRow(row, columnMap))
            .ToList();

        var extraFieldKeys = activeFields
            .Where(f => !f.IsSystemField && columnMap.Values.Any(v =>
                string.Equals(v, f.FieldKey, StringComparison.OrdinalIgnoreCase)))
            .Select(f => f.FieldKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EmployeeImportPreviewResponse
        {
            PreviewRows = previewRows,
            ExtraFieldKeys = extraFieldKeys,
            ActiveFields = activeFields,
            TotalRows = parsed.Rows.Count,
            Skip = skip,
            Take = take,
        };
    }

    public async Task<EmployeeImportReportDto> ExecuteAsync(
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default)
    {
        var auth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        var started = await executor.StartAsync(request, startedByEmail, ct);
        await executeQueue.EnqueueAsync(
            new EmployeeImportExecuteWorkItem(
                started.ImportJobId,
                request,
                startedByEmail,
                string.IsNullOrWhiteSpace(auth) ? null : auth),
            ct);
        return started;
    }

    public async Task<EmployeeImportRevalidateOrgResponse> RevalidateOrgAsync(
        EmployeeImportRevalidateOrgRequest request,
        CancellationToken ct = default)
    {
        // Pas de SyncFromDirectoryOverview : miroir déjà aligné à l'analyze.
        var parsed = await sessionStore.GetAsync(request.ImportSessionId, ct)
            ?? throw new InvalidOperationException("Session d'import expirée ou introuvable. Re-analysez le fichier.");

        var activeFields = (await configService.GetConfigAsync(ct)).Where(f => f.IsEnabled).ToList();
        var columnMap = EmployeeImportMappingHelper.BuildColumnMap(request.Mappings, activeFields);

        if (!columnMap.Values.Any(v => v is "pole" or "cellule" or "service"))
        {
            throw new InvalidOperationException(
                "Mappez au moins une colonne Pôle, Cellule ou Service avant l'étape Organisation.");
        }

        var orgSnapshot = await orgResolver.LoadSnapshotAsync(ct);
        var orgAnalysis = orgGapAnalyzer.AnalyzeFile(parsed, columnMap, orgSnapshot, orgSnapshot.Roles);
        await EnrichPendingOperationalDepartmentsAsync(orgAnalysis.PendingOrgCreations, alerts: null, ct);

        return new EmployeeImportRevalidateOrgResponse
        {
            PendingOrgCreations = orgAnalysis.PendingOrgCreations,
            ResolvedRows = orgAnalysis.ResolvedRows,
            OrgLineIssues = orgAnalysis.OrgLineIssues
        };
    }

    public async Task<List<EmployeeImportJobSummaryDto>> GetHistoryAsync(int take = 50, CancellationToken ct = default) =>
        await GetHistoryInternalAsync(take, ct);

    public async Task<EmployeeImportReportDto?> GetJobReportAsync(Guid jobId, CancellationToken ct = default) =>
        await GetJobReportInternalAsync(jobId, ct);

    public async Task<EmployeeImportSourceFile?> GetJobSourceFileAsync(Guid jobId, CancellationToken ct = default) =>
        await GetJobSourceFileInternalAsync(jobId, ct);

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct = default) =>
        await templateBuilder.BuildAsync(ct);

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" => "text/csv",
            ".xls" => "application/vnd.ms-excel",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    private async Task EnrichPendingOperationalDepartmentsAsync(
        List<PendingOrgCreationDto> pendingOrgCreations,
        List<string>? alerts,
        CancellationToken ct)
    {
        var poleCreations = pendingOrgCreations.Where(p => p.Type == "pole").ToList();
        if (poleCreations.Count == 0)
            return;

        var departments = await directoryOrg.GetOperationalDepartmentsAsync(ct);
        var missing = new Dictionary<string, PendingOrgCreationDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var pole in poleCreations)
        {
            if (string.IsNullOrWhiteSpace(pole.OperationalDepartment))
            {
                alerts?.Add(
                    $"Département de production requis pour créer le pôle « {pole.Pole} » " +
                    "(mappez la colonne dans le fichier).");
                continue;
            }

            var raw = pole.OperationalDepartment.Trim();
            if (EmployeeImportOperationalDeptResolver.ResolveBusinessDepartmentId(raw, departments) is not null)
                continue;

            var key = EmployeeImportColumnMatcher.Normalize(raw);
            if (!missing.TryGetValue(key, out var deptPending))
            {
                deptPending = new PendingOrgCreationDto
                {
                    Type = "operationalDepartment",
                    OperationalDepartment = raw,
                    ConfirmationLabel = $"Créer le département de production « {raw} »",
                    Approved = true,
                };
                missing[key] = deptPending;
            }

            foreach (var line in pole.AffectedLineNumbers)
            {
                if (!deptPending.AffectedLineNumbers.Contains(line))
                    deptPending.AffectedLineNumbers.Add(line);
            }
        }

        if (missing.Count == 0)
            return;

        // Départements avant pôles (ordre d'affichage + provision).
        pendingOrgCreations.InsertRange(
            0,
            missing.Values.OrderBy(d => d.OperationalDepartment, StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string?> BuildPreviewRow(
        IReadOnlyList<string> row,
        Dictionary<int, string> columnMap)
    {
        var mapped = EmployeeImportRowMapper.MapRow(row, columnMap);
        foreach (var key in mapped.Keys.ToList())
        {
            if (key == "password" && !string.IsNullOrWhiteSpace(mapped[key]))
                mapped[key] = "********";
        }
        return mapped;
    }
}
