using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;

namespace Planning.Infrastructure.Services.Pointage;

/// <summary>Pointage tel que renvoyé par /iclock/api/transactions/.</summary>
public sealed record BioTimeTransaction(
    long Id,
    string EmpCode,
    DateTime PunchTime,
    DateTime? UploadTime,
    string? TerminalSn,
    string? TerminalAlias,
    int? VerifyType,
    string? FirstName,
    string? LastName,
    string? Department);

/// <summary>Collaborateur tel que renvoyé par /personnel/api/employees/.</summary>
public sealed record BioTimeEmployee(string EmpCode, string? FirstName, string? LastName, string? Department);

/// <summary>
/// Client de l'API REST ZKBio Time 9 — <b>lecture seule</b>.
/// Authentification : POST /api-token-auth/ puis en-tête « Authorization: Token … ».
/// Pagination : { count, next, previous, data }.
/// </summary>
public sealed class BioTimeClient(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<PointageOptions> options,
    ILogger<BioTimeClient> logger)
{
    public const string HttpClientName = "BioTime";
    private const int PageSize = 500;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;

    public async Task<IReadOnlyList<BioTimeTransaction>> GetTransactionsAsync(DateTime debut, DateTime fin, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["start_time"] = debut.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["end_time"] = fin.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
        var rows = new List<BioTimeTransaction>();
        await foreach (var el in PagesAsync("/iclock/api/transactions/", query, ct))
        {
            var tx = ParseTransaction(el);
            if (tx is not null)
                rows.Add(tx);
        }
        return rows;
    }

    public async Task<IReadOnlyList<BioTimeEmployee>> GetEmployeesAsync(CancellationToken ct)
    {
        var rows = new List<BioTimeEmployee>();
        await foreach (var el in PagesAsync("/personnel/api/employees/", new Dictionary<string, string>(), ct))
        {
            var code = ReadString(el, "emp_code");
            if (!string.IsNullOrWhiteSpace(code))
                rows.Add(new BioTimeEmployee(code.Trim(), ReadString(el, "first_name"), ReadString(el, "last_name"), ReadDepartment(el)));
        }
        return rows;
    }

    private async IAsyncEnumerable<JsonElement> PagesAsync(
        string path,
        IDictionary<string, string> query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var page = 1; page < 10_000; page++)
        {
            var qs = string.Join("&", query
                .Append(new KeyValuePair<string, string>("page_size", PageSize.ToString(CultureInfo.InvariantCulture)))
                .Append(new KeyValuePair<string, string>("page", page.ToString(CultureInfo.InvariantCulture)))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            using var doc = await GetJsonAsync($"{path}?{qs}", ct);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d
                : root.TryGetProperty("results", out var r) ? r
                : default;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                yield break;
            foreach (var el in data.EnumerateArray())
                yield return el.Clone();
            var hasNext = root.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(next.GetString());
            if (!hasNext)
                yield break;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(forceRefresh: attempt > 0, ct);
            var http = httpFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUri(pathAndQuery));
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 0)
                continue;
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"ZKBio Time {pathAndQuery.Split('?')[0]} → HTTP {(int)resp.StatusCode}");
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        throw new HttpRequestException("ZKBio Time : authentification refusée après renouvellement du jeton.");
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _token is not null)
            return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null)
                return _token;
            var o = options.CurrentValue;
            var http = httpFactory.CreateClient(HttpClientName);
            // Corps avec Content-Length (pas d'envoi « chunked », mal accepté par certains serveurs WSGI).
            using var body = new StringContent(
                JsonSerializer.Serialize(new { username = o.Username, password = o.Password }),
                System.Text.Encoding.UTF8,
                "application/json");
            using var resp = await http.PostAsync(BaseUri("/api-token-auth/"), body, ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"ZKBio Time : connexion du compte « {o.Username} » refusée (HTTP {(int)resp.StatusCode}).");
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            _token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(_token))
                throw new HttpRequestException("ZKBio Time : jeton absent de la réponse d'authentification.");
            logger.LogInformation("ZKBio Time : jeton obtenu pour le compte {User}.", o.Username);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BaseUri(string pathAndQuery)
    {
        var baseUrl = (options.CurrentValue.BaseUrl ?? string.Empty).TrimEnd('/');
        return new Uri(baseUrl + pathAndQuery);
    }

    // ── Lecture des réponses ────────────────────────────────────────────────────

    public static BioTimeTransaction? ParseTransaction(JsonElement el)
    {
        var idText = ReadString(el, "id");
        var code = ReadString(el, "emp_code");
        var punch = ParseDate(ReadString(el, "punch_time"));
        if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || string.IsNullOrWhiteSpace(code)
            || punch is null)
            return null;
        int? verify = int.TryParse(ReadString(el, "verify_type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new BioTimeTransaction(
            id,
            code.Trim(),
            punch.Value,
            ParseDate(ReadString(el, "upload_time")),
            ReadString(el, "terminal_sn"),
            ReadString(el, "terminal_alias"),
            verify,
            ReadString(el, "first_name"),
            ReadString(el, "last_name"),
            ReadDepartment(el));
    }

    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim().Replace('T', ' ');
        if (text.Length > 19)
            text = text[..19];
        return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    /// <summary>« department » : libellé (transactions) ou objet { dept_name } (collaborateurs).</summary>
    private static string? ReadDepartment(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("department", out var d))
            return null;
        if (d.ValueKind == JsonValueKind.String)
            return d.GetString();
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("dept_name", out var n) && n.ValueKind == JsonValueKind.String)
            return n.GetString();
        return null;
    }

    // ── TLS ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Certificat de ZKBio Time : avec une empreinte configurée, seule cette empreinte est acceptée
    /// (certificat auto-signé ou expiré) ; sinon, vérification standard par autorité.
    /// </summary>
    public static HttpMessageHandler CreateHandler(PointageOptions o)
    {
        var expected = (o.CertificateSha256 ?? string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(expected))
        {
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null
                && string.Equals(Convert.ToHexString(SHA256.HashData(cert.RawData)), expected, StringComparison.OrdinalIgnoreCase);
        }
        return handler;
    }
}
