using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conge.Application.Abstractions;
using Conge.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Conge.Infrastructure.Services;

public sealed class DirectoryEmployeeLookup(
    HttpClient http,
    ILogger<DirectoryEmployeeLookup> logger) : IDirectoryEmployeeLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<DirectoryEmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => GetAsync($"api/directory/employees/{id}", ct);

    public Task<DirectoryEmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult<DirectoryEmployeeRecord?>(null);
        return GetAsync(
            $"api/directory/employees/by-email?email={Uri.EscapeDataString(email.Trim())}",
            ct);
    }

    private async Task<DirectoryEmployeeRecord?> GetAsync(string relativeUrl, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await http.GetAsync(relativeUrl, timeout.Token);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogDebug("Directory employee lookup HTTP {Status} for {Url}", (int)resp.StatusCode, relativeUrl);
                return null;
            }

            var dto = await resp.Content.ReadFromJsonAsync<DirectoryEmployeeJson>(JsonOptions, timeout.Token);
            return Map(dto);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Directory employee lookup failed for {Url}", relativeUrl);
            return null;
        }
    }

    internal static DirectoryEmployeeRecord? Map(DirectoryEmployeeJson? dto)
    {
        if (dto is null || !Guid.TryParse(dto.Id, out var id) || id == Guid.Empty)
            return null;

        Guid? superviseur = Guid.TryParse(dto.SuperviseurId, out var s) && s != Guid.Empty ? s : null;
        Guid? parent = Guid.TryParse(dto.ParentId, out var p) && p != Guid.Empty ? p : null;
        Guid? business = Guid.TryParse(dto.BusinessDepartmentId, out var b) && b != Guid.Empty ? b : null;
        DateTime? hire = ToUtcHireDate(dto.HrProfile?.DateAnciennete)
            ?? ToUtcHireDate(dto.HrProfile?.DateEmbauche)
            ?? ToUtcHireDate(dto.HrProfile?.DateEntree);

        return new DirectoryEmployeeRecord(
            id,
            dto.FirstName ?? string.Empty,
            dto.LastName ?? string.Empty,
            dto.Email ?? string.Empty,
            dto.Role ?? string.Empty,
            superviseur,
            parent,
            dto.ServiceId,
            dto.PoleId,
            dto.CelluleId,
            business,
            hire,
            EmployeSnapshot.ComputeEstMineur(dto.HrProfile?.DateNaissance));
    }

    private static DateTime? ToUtcHireDate(DateOnly? date) =>
        date is null
            ? null
            : DateTime.SpecifyKind(date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    internal sealed class DirectoryEmployeeJson
    {
        public string? Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public string? ParentId { get; set; }
        public string? ServiceId { get; set; }
        public string? PoleId { get; set; }
        public string? CelluleId { get; set; }
        public string? Email { get; set; }
        public string? SuperviseurId { get; set; }
        public string? BusinessDepartmentId { get; set; }
        public DirectoryHrProfileJson? HrProfile { get; set; }
    }

    internal sealed class DirectoryHrProfileJson
    {
        public DateOnly? DateNaissance { get; set; }
        public DateOnly? DateEntree { get; set; }
        public DateOnly? DateEmbauche { get; set; }
        public DateOnly? DateAnciennete { get; set; }
    }
}
