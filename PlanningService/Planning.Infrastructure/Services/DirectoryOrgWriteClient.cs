using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace Planning.Infrastructure.Services;

public interface IDirectoryOrgWriteClient
{
    Task<string> CreatePoleAsync(string name, Guid businessDepartmentId, CancellationToken ct = default);
    Task<Guid> CreateOperationalDepartmentAsync(string? code, string name, CancellationToken ct = default);
    Task<IReadOnlyList<DirectoryOperationalDepartmentJson>> GetOperationalDepartmentsAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetEmployeeOperationalBusinessDepartmentIdsAsync(CancellationToken ct = default);
    Task<string> CreateCelluleAsync(string poleDirectoryId, string name, CancellationToken ct = default);
    Task<string> CreateServiceAsync(string celluleDirectoryId, string name, CancellationToken ct = default);

    Task<bool> AssignChefDeProjetAsync(string poleDirectoryId, Guid employeeGuid, CancellationToken ct = default);
    Task<bool> AssignSuperviseurAsync(string celluleDirectoryId, Guid employeeGuid, CancellationToken ct = default);
    Task<bool> AssignReferentTechniqueAsync(string serviceDirectoryId, Guid employeeGuid, CancellationToken ct = default);

    Task DeletePoleAsync(string nodeId, CancellationToken ct = default);
    Task DeleteCelluleAsync(string poleId, string nodeId, CancellationToken ct = default);
    Task DeleteServiceAsync(string celluleId, string nodeId, CancellationToken ct = default);

    Task<DirectoryUndoImportWindowResult> UndoImportAssignmentWindowAsync(
        IReadOnlyList<Guid> employeeIds,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetEmployeeIdsWithAssignmentsAfterAsync(
        IReadOnlyList<Guid> employeeIds,
        DateTime after,
        CancellationToken ct = default);
}

public sealed class DirectoryOrgWriteClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<DirectoryOrgWriteClient> logger) : IDirectoryOrgWriteClient
{
    public Task<string> CreatePoleAsync(string name, Guid businessDepartmentId, CancellationToken ct = default) =>
        PostNodeAsync("api/directory/org/structure/poles", new
        {
            name = name.Trim(),
            businessDepartmentId,
        }, ct);

    public async Task<Guid> CreateOperationalDepartmentAsync(string? code, string name, CancellationToken ct = default)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/directory/business-departments")
        {
            Content = JsonContent.Create(new
            {
                code = string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
                name = name.Trim(),
                kind = "Operational",
            }),
        };
        AttachAuth(request);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Directory operational department create failed {Status}: {Body}",
                response.StatusCode,
                err);
            var detail = string.IsNullOrWhiteSpace(err) ? string.Empty : $" {err.Trim()}";
            throw new InvalidOperationException(
                $"Création du département de production échouée ({response.StatusCode}).{detail}");
        }

        var created = await response.Content.ReadFromJsonAsync<DirectoryOperationalDepartmentJson>(cancellationToken: ct);
        if (created is null || !Guid.TryParse(created.Id, out var id))
            throw new InvalidOperationException("Réponse Directory invalide après création du département de production.");

        return id;
    }

    public async Task<IReadOnlyList<DirectoryOperationalDepartmentJson>> GetOperationalDepartmentsAsync(
        CancellationToken ct = default)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/directory/business-departments");
        AttachAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory operational departments fetch failed");
            return Array.Empty<DirectoryOperationalDepartmentJson>();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Directory operational departments fetch failed {Status}: {Body}", response.StatusCode, err);
                return Array.Empty<DirectoryOperationalDepartmentJson>();
            }

            var depts = await ReadJsonOrDefaultAsync<List<DirectoryOperationalDepartmentJson>>(response, ct);
            return (depts ?? [])
                .Where(d => string.Equals(d.Kind, "Operational", StringComparison.OrdinalIgnoreCase) && d.IsActive != false)
                .ToList();
        }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmployeeOperationalBusinessDepartmentIdsAsync(
        CancellationToken ct = default)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/directory/employees");
        AttachAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory employees fetch failed");
            return new Dictionary<Guid, string>();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Directory employees fetch failed {Status}", response.StatusCode);
                return new Dictionary<Guid, string>();
            }

            var employees = await ReadJsonOrDefaultAsync<List<DirectoryEmployeeOrgJson>>(response, ct);
            var map = new Dictionary<Guid, string>();
            foreach (var e in employees ?? [])
            {
                if (!Guid.TryParse(e.Id, out var guid)) continue;
                if (string.IsNullOrWhiteSpace(e.BusinessDepartmentId)) continue;
                if (!string.Equals(e.BusinessDepartmentKind, "Operational", StringComparison.OrdinalIgnoreCase)) continue;
                map[guid] = e.BusinessDepartmentId.Trim();
            }
            return map;
        }
    }

    public Task<string> CreateCelluleAsync(string poleDirectoryId, string name, CancellationToken ct = default) =>
        PostNodeAsync(
            $"api/directory/org/structure/poles/{Uri.EscapeDataString(poleDirectoryId)}/cellules",
            new { name = name.Trim() },
            ct);

    public Task<string> CreateServiceAsync(string celluleDirectoryId, string name, CancellationToken ct = default) =>
        PostNodeAsync(
            $"api/directory/org/structure/cellules/{Uri.EscapeDataString(celluleDirectoryId)}/services",
            new { name = name.Trim() },
            ct);

    public Task<bool> AssignChefDeProjetAsync(string poleDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
        PostAssignmentAsync(
            $"api/directory/org/assignments/ChefDeProjet/{Uri.EscapeDataString(poleDirectoryId)}",
            employeeGuid,
            ct);

    public Task<bool> AssignSuperviseurAsync(string celluleDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
        PostAssignmentAsync(
            $"api/directory/org/assignments/Superviseur/{Uri.EscapeDataString(celluleDirectoryId)}",
            employeeGuid,
            ct);

    public Task<bool> AssignReferentTechniqueAsync(string serviceDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
        PostAssignmentAsync(
            $"api/directory/org/assignments/ReferentTechnique/{Uri.EscapeDataString(serviceDirectoryId)}",
            employeeGuid,
            ct);

    public Task DeletePoleAsync(string nodeId, CancellationToken ct = default) =>
        DeleteNodeAsync($"api/directory/org/structure/poles/{Uri.EscapeDataString(nodeId)}", ct);

    public Task DeleteCelluleAsync(string poleId, string nodeId, CancellationToken ct = default) =>
        DeleteNodeAsync(
            $"api/directory/org/structure/poles/{Uri.EscapeDataString(poleId)}/cellules/{Uri.EscapeDataString(nodeId)}",
            ct);

    public Task DeleteServiceAsync(string celluleId, string nodeId, CancellationToken ct = default) =>
        DeleteNodeAsync(
            $"api/directory/org/structure/cellules/{Uri.EscapeDataString(celluleId)}/services/{Uri.EscapeDataString(nodeId)}",
            ct);

    private async Task DeleteNodeAsync(string path, CancellationToken ct)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AttachAuth(request);

        var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;

        var err = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Directory org delete {Path} failed {Status}: {Body}", path, response.StatusCode, err);
        throw new InvalidOperationException($"Suppression organisationnelle échouée ({response.StatusCode}).");
    }

    public async Task<DirectoryUndoImportWindowResult> UndoImportAssignmentWindowAsync(
        IReadOnlyList<Guid> employeeIds,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/directory/org/assignments/undo-import-window")
        {
            Content = JsonContent.Create(new
            {
                employeeIds,
                windowStart,
                windowEnd,
            }),
        };
        AttachAuth(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Directory undo-import-window failed {Status}: {Body}",
                response.StatusCode,
                err);
            throw new InvalidOperationException(
                $"Annulation des affectations Directory échouée ({response.StatusCode}).");
        }

        var parsed = await ReadJsonOrDefaultAsync<DirectoryUndoImportWindowResult>(response, ct);
        return parsed ?? new DirectoryUndoImportWindowResult();
    }

    public async Task<IReadOnlyList<Guid>> GetEmployeeIdsWithAssignmentsAfterAsync(
        IReadOnlyList<Guid> employeeIds,
        DateTime after,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/directory/org/assignments/created-after")
        {
            Content = JsonContent.Create(new { employeeIds, after }),
        };
        AttachAuth(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Directory assignments created-after failed {Status}: {Body}",
                response.StatusCode,
                err);
            throw new InvalidOperationException(
                $"Lecture des rotations Directory échouée ({response.StatusCode}).");
        }

        var parsed = await ReadJsonOrDefaultAsync<List<Guid>>(response, ct);
        return parsed ?? [];
    }

    private async Task<string> PostNodeAsync(string path, object body, CancellationToken ct)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        AttachAuth(request);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Directory org create {Path} failed {Status}: {Body}", path, response.StatusCode, err);
            var detail = string.IsNullOrWhiteSpace(err) ? string.Empty : $" {err.Trim()}";
            throw new InvalidOperationException($"Création organisationnelle échouée ({response.StatusCode}).{detail}");
        }

        var created = await response.Content.ReadFromJsonAsync<DirectoryNodeJson>(cancellationToken: ct);
        if (created is null || string.IsNullOrWhiteSpace(created.Id))
            throw new InvalidOperationException("Réponse Directory invalide après création org.");

        return created.Id;
    }

    private async Task<bool> PostAssignmentAsync(string path, Guid employeeGuid, CancellationToken ct)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { employeeId = employeeGuid.ToString("D") }),
        };
        AttachAuth(request);

        var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return true;

        var err = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Directory assignment {Path} failed {Status}: {Body}", path, response.StatusCode, err);
        return false;
    }

    private HttpClient CreateClient()
    {
        var baseUrl = configuration["Directory:BaseUrl"]?.Trim()
            ?? "http://employee-directory-backend:8080/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var client = httpClientFactory.CreateClient("DirectorySync");
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        var authHeader = DirectoryHttpAuthContext.AuthorizationHeader.Value
            ?? httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);
    }

    private static async Task<T?> ReadJsonOrDefaultAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(body, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class DirectoryNodeJson
    {
        public string Id { get; set; } = "";
    }

    private sealed class DirectoryEmployeeOrgJson
    {
        public string Id { get; set; } = "";
        public string? BusinessDepartmentId { get; set; }
        public string? BusinessDepartmentKind { get; set; }
    }
}

public sealed class DirectoryUndoImportWindowResult
{
    public int DeletedAssignments { get; set; }
    public int RestoredAssignments { get; set; }
    public int EmployeesTouched { get; set; }
}

public sealed class DirectoryOperationalDepartmentJson
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool? IsActive { get; set; }
    public List<string> PoleIds { get; set; } = [];
}
