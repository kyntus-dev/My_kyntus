using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kyntus.Iam;

/// <summary>Propage le JWT de la requête entrante vers Employee Directory.</summary>
public sealed class DirectoryForwardAuthorizationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>ReBAC via Employee Directory HTTP API (consommé par les modules projection).</summary>
public sealed class DirectoryHttpRebacClient(HttpClient http) : IRebacClient
{
    public async Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
    {
        var url = $"/api/directory/rebac/is-descendant?viewerId={viewerId}&targetId={targetId}";
        var resp = await http.GetFromJsonAsync<IsDescendantResponse>(url, ct);
        return resp?.IsDescendant == true;
    }

    public async Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
    {
        var url =
            $"/api/directory/rebac/managed-nodes?employeeId={employeeId}&kind={Uri.EscapeDataString(kind)}";
        var resp = await http.GetFromJsonAsync<ManagedNodesResponse>(url, ct);
        return resp?.NodeIds ?? [];
    }

    public async Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default)
    {
        var url = $"/api/directory/rebac/managed-employees?employeeId={actorId}";
        var resp = await http.GetFromJsonAsync<ManagedEmployeesResponse>(url, ct);
        return resp?.EmployeeIds?.Select(ParseGuid).Where(g => g != Guid.Empty).ToList() ?? [];
    }

    public async Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("/api/directory/rebac/can-act", new
        {
            actorId,
            targetEmployeeId,
        }, ct);
        if (!resp.IsSuccessStatusCode) return false;
        var body = await resp.Content.ReadFromJsonAsync<CanActResponse>(cancellationToken: ct);
        return body?.Allowed == true;
    }

    public async Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default)
    {
        var url =
            $"/api/directory/rebac/responsibles?kind={Uri.EscapeDataString(kind)}&nodeId={Uri.EscapeDataString(nodeId)}";
        var resp = await http.GetFromJsonAsync<ResponsiblesResponse>(url, ct);
        return resp?.Responsibles?.Select(r => ParseGuid(r.EmployeeId)).Where(g => g != Guid.Empty).ToList()
               ?? [];
    }

    private static Guid ParseGuid(string? value) =>
        Guid.TryParse(value, out var g) ? g : Guid.Empty;

    private sealed record IsDescendantResponse(bool IsDescendant);
    private sealed record ManagedNodesResponse(IReadOnlyList<string> NodeIds);
    private sealed record ManagedEmployeesResponse(IReadOnlyList<string> EmployeeIds);
    private sealed record CanActResponse(bool Allowed);
    private sealed record ResponsibleItem(string EmployeeId);
    private sealed record ResponsiblesResponse(IReadOnlyList<ResponsibleItem> Responsibles);
}

/// <summary>Évalue les policies via POST /api/iam/evaluate (forward JWT utilisateur).</summary>
public sealed class DirectoryHttpPolicyEvaluator(HttpClient http) : IPolicyEvaluator
{
    public async Task<PolicyDecision> EvaluateAsync(PolicyRequest request, CancellationToken ct = default)
    {
        if (IsPrivilegedAdmin(request.Role))
            return new PolicyDecision(true);

        var resp = await http.PostAsJsonAsync("/api/iam/evaluate", new
        {
            action = request.Action,
            resourceType = request.ResourceType,
            resourceId = request.ResourceId,
        }, ct);

        if (!resp.IsSuccessStatusCode)
            return new PolicyDecision(false, $"Directory IAM HTTP {(int)resp.StatusCode}");

        var body = await resp.Content.ReadFromJsonAsync<EvaluateResponse>(cancellationToken: ct);
        return body is null
            ? new PolicyDecision(false, "Empty IAM response")
            : new PolicyDecision(body.Allowed, body.Reason);
    }

    private static bool IsPrivilegedAdmin(string role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    private sealed record EvaluateResponse(bool Allowed, string? Reason);
}

public static class DirectoryHttpIamExtensions
{
    public static IServiceCollection AddKyntusIamViaDirectoryHttp(
        this IServiceCollection services,
        string directoryBaseUrl)
    {
        var baseUri = new Uri(directoryBaseUrl.TrimEnd('/') + "/");

        services.AddHttpContextAccessor();
        services.AddTransient<DirectoryForwardAuthorizationHandler>();

        services.AddHttpClient<DirectoryHttpRebacClient>(c => c.BaseAddress = baseUri)
            .AddHttpMessageHandler<DirectoryForwardAuthorizationHandler>();
        services.AddHttpClient<DirectoryHttpPolicyEvaluator>(c => c.BaseAddress = baseUri)
            .AddHttpMessageHandler<DirectoryForwardAuthorizationHandler>();

        services.AddScoped<IRebacClient>(sp => sp.GetRequiredService<DirectoryHttpRebacClient>());
        services.AddScoped<IPolicyEvaluator>(sp => sp.GetRequiredService<DirectoryHttpPolicyEvaluator>());
        return services;
    }
}
