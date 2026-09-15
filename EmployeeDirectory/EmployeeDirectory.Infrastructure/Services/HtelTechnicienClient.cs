using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class HtelTechnicienClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HtelTechnicienClient> logger) : IHtelTechnicienClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<HtelTechnicienDto>> GetTechniciensAsync(CancellationToken ct = default)
    {
        // Comme b8781ae : l’appel part même sans ApiKey (header X-Api-Key ajouté seulement si présent).
        var client = httpClientFactory.CreateClient("Htel");
        var techniciensUrl = configuration["Htel:TechniciensUrl"]?.Trim();
        var requestUri = string.IsNullOrEmpty(techniciensUrl)
            ? (configuration["Htel:TechniciensPath"]?.Trim() ?? "api/v1/techniciens")
            : techniciensUrl;
        using var response = await client.GetAsync(requestUri, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("HTEL techniciens HTTP {Status}: {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        List<HtelTechnicienJson?> rows;
        try
        {
            rows = await response.Content.ReadFromJsonAsync<List<HtelTechnicienJson?>>(JsonOptions, ct)
                ?? throw new JsonException("Catalogue HTEL null.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Réponse HTEL invalide : un tableau complet de techniciens est attendu.", ex);
        }
        if (rows.Any(r => r is null || r.IdTechnicien is null || r.Actif is null))
            throw new HttpRequestException("Réponse HTEL incomplète : identifiant ou statut manquant.");
        var result = rows
            .Select(r => new HtelTechnicienDto(
                r!.IdTechnicien!.Value,
                (r.Technicien ?? string.Empty).Trim(),
                r.Actif!.Value,
                (r.Code ?? string.Empty).Trim()))
            .ToList();
        HtelReconciliation.ValidateSnapshot(result);
        return result;
    }

    private sealed class HtelTechnicienJson
    {
        [JsonPropertyName("id_technicien")]
        public int? IdTechnicien { get; set; }

        [JsonPropertyName("technicien")]
        public string? Technicien { get; set; }

        [JsonPropertyName("actif")]
        public int? Actif { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
