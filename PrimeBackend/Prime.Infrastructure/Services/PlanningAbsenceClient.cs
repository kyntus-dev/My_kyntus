using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Kyntus.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Prime.Infrastructure.Services;

public interface IPlanningAbsenceClient
{
    Task<IReadOnlyDictionary<string, int>> GetAbsenceDayCountsAsync(
        string period,
        IReadOnlyList<string> employeeGuids,
        CancellationToken ct = default);
}

public sealed class PlanningAbsenceClient(
    HttpClient http,
    IConfiguration configuration,
    ICacheService cache,
    ILogger<PlanningAbsenceClient> logger) : IPlanningAbsenceClient
{
    public async Task<IReadOnlyDictionary<string, int>> GetAbsenceDayCountsAsync(
        string period,
        IReadOnlyList<string> employeeGuids,
        CancellationToken ct = default)
    {
        if (employeeGuids.Count == 0)
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join(',', employeeGuids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)))));
        var key = KyntusCacheKeys.PrimeAbsences(period.Trim(), hash[..16]);

        return await cache.GetOrCreateAsync(
            key,
            token => FetchAsync(period, employeeGuids, token),
            KyntusCacheTtl.Short,
            ct);
    }

    private async Task<Dictionary<string, int>> FetchAsync(
        string period,
        IReadOnlyList<string> employeeGuids,
        CancellationToken ct)
    {
        var baseUrl = configuration["Planning:BaseUrl"] ?? "http://planning-backend:8080";
        var url = $"{baseUrl.TrimEnd('/')}/api/Conges/absence-days/bulk";

        try
        {
            using var response = await http.PostAsJsonAsync(
                url,
                new { period = period.Trim(), employeeGuids },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Planning absence-days/bulk a échoué ({Status}) pour la période {Period}",
                    response.StatusCode,
                    period);
                return employeeGuids.ToDictionary(g => g, _ => 0, StringComparer.OrdinalIgnoreCase);
            }

            var payload = await response.Content.ReadFromJsonAsync<BulkAbsenceDaysResponse>(cancellationToken: ct);
            if (payload?.Items is null)
                return employeeGuids.ToDictionary(g => g, _ => 0, StringComparer.OrdinalIgnoreCase);

            return payload.Items.ToDictionary(
                i => i.EmployeeGuid,
                i => i.AbsenceDayCount,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de récupérer les absences Planning pour {Period}", period);
            return employeeGuids.ToDictionary(g => g, _ => 0, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class BulkAbsenceDaysResponse
    {
        public List<BulkAbsenceDaysItem>? Items { get; set; }
    }

    private sealed class BulkAbsenceDaysItem
    {
        public string EmployeeGuid { get; set; } = "";
        public int AbsenceDayCount { get; set; }
    }
}
