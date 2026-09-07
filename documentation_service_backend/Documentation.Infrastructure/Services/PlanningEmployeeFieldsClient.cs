using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Documentation.Infrastructure.Services;

public sealed class PlanningEmployeeFieldsClient(
    HttpClient http,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PlanningEmployeeFieldsClient> logger) : IPlanningEmployeeFieldsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<PlanningEmployeeFieldCatalogItem>> GetCatalogAsync(
        bool enabledOnly,
        CancellationToken ct = default)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, $"api/users/fields?enabledOnly={enabledOnly.ToString().ToLowerInvariant()}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await http.SendAsync(req, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<PlanningEmployeeFieldCatalogItem>();

            var rows = await resp.Content
                .ReadFromJsonAsync<List<PlanningEmployeeFieldCatalogItem>>(JsonOptions, timeout.Token)
                .ConfigureAwait(false);
            return rows ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catalogue champs employés Planning indisponible.");
            return Array.Empty<PlanningEmployeeFieldCatalogItem>();
        }
    }

    public async Task<IReadOnlyList<EmployeeFieldMatchHint>> MatchVariablesAsync(
        IReadOnlyList<TemplateVariableInput> variables,
        CancellationToken ct = default)
    {
        if (variables.Count == 0)
            return Array.Empty<EmployeeFieldMatchHint>();

        try
        {
            var payload = new
            {
                sources = variables.Select(v => new
                {
                    name = v.Name,
                    displayLabel = v.DisplayLabel,
                    normalizedName = v.NormalizedName,
                    rawPlaceholder = v.RawPlaceholder,
                }).ToList(),
            };

            using var req = CreateRequest(HttpMethod.Post, "api/users/fields/match-variables");
            req.Content = JsonContent.Create(payload);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await http.SendAsync(req, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<EmployeeFieldMatchHint>();

            var rows = await resp.Content
                .ReadFromJsonAsync<List<PlanningMatchJson>>(JsonOptions, timeout.Token)
                .ConfigureAwait(false);
            return (rows ?? [])
                .Select(r => new EmployeeFieldMatchHint(
                    r.SourceName ?? string.Empty,
                    r.SuggestedFieldKey,
                    r.SuggestedFieldLabel,
                    r.Score,
                    string.IsNullOrWhiteSpace(r.Confidence) ? "low" : r.Confidence,
                    r.IsEnabled))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Matching variables documentaires via Planning indisponible.");
            return Array.Empty<EmployeeFieldMatchHint>();
        }
    }

    public async Task<PlanningEmployeeFieldValues?> GetEmployeeFieldValuesAsync(
        Guid employeeGuid,
        IReadOnlyList<string>? includeKeys,
        CancellationToken ct = default)
    {
        if (employeeGuid == Guid.Empty)
            return null;

        try
        {
            var qs = includeKeys is { Count: > 0 }
                ? "?" + string.Join("&", includeKeys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => "includeKeys=" + Uri.EscapeDataString(k.Trim())))
                : string.Empty;
            using var req = CreateRequest(
                HttpMethod.Get,
                $"api/users/by-guid/{employeeGuid:D}/document-field-values{qs}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await http.SendAsync(req, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content
                .ReadFromJsonAsync<PlanningFieldValuesJson>(JsonOptions, timeout.Token)
                .ConfigureAwait(false);
            if (body is null)
                return null;

            return new PlanningEmployeeFieldValues
            {
                EmployeeGuid = body.EmployeeGuid,
                Values = body.Values ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                UnavailableKeys = body.UnavailableKeys ?? [],
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Valeurs champs employés Planning indisponibles pour {Guid}.", employeeGuid);
            return null;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        var auth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth))
            req.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
        return req;
    }

    private sealed class PlanningMatchJson
    {
        public string? SourceName { get; set; }
        public string? SuggestedFieldKey { get; set; }
        public string? SuggestedFieldLabel { get; set; }
        public double Score { get; set; }
        public string? Confidence { get; set; }
        public bool IsEnabled { get; set; }
    }

    private sealed class PlanningFieldValuesJson
    {
        public Guid EmployeeGuid { get; set; }
        public Dictionary<string, string?>? Values { get; set; }
        public List<string>? UnavailableKeys { get; set; }
    }
}
