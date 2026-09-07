using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class HtelFusionService(
    DirectoryDbContext db,
    IHtelTechnicienClient htelClient,
    IOutboxWriter outbox,
    ILogger<HtelFusionService> logger) : IHtelFusionService
{
    public async Task<IReadOnlyList<HtelTechnicienDto>> ListTechniciensAsync(bool? actifOnly = null, CancellationToken ct = default)
    {
        var all = await htelClient.GetTechniciensAsync(ct);
        if (actifOnly == true)
            return all.Where(t => t.Actif == 1).ToList();
        return all;
    }

    public async Task<HtelLiaisonsReportDto> GetLiaisonsAsync(CancellationToken ct = default)
    {
        var techniciens = await htelClient.GetTechniciensAsync(ct);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return BuildReport(techniciens, employees);
    }

    public async Task<HtelSyncReportDto> SyncAsync(CancellationToken ct = default)
    {
        var techniciens = await htelClient.GetTechniciensAsync(ct);
        var byId = techniciens.ToDictionary(t => t.IdTechnicien);
        var employees = await db.Employees.Where(e => e.IsActive).ToListAsync(ct);

        var linkedUpdated = 0;
        var newlyLinked = 0;

        foreach (var employee in employees.Where(e => e.IdTechnicien.HasValue))
        {
            if (!byId.TryGetValue(employee.IdTechnicien!.Value, out var tech))
                continue;

            var changed = false;
            if (!string.Equals(employee.HtelCode, tech.Code, StringComparison.Ordinal))
            {
                employee.HtelCode = tech.Code;
                changed = true;
            }

            var shouldBeActive = tech.Actif == 1;
            if (!shouldBeActive && employee.IsActive)
            {
                employee.IsActive = false;
                changed = true;
            }

            if (changed)
            {
                employee.UpdatedAt = DateTime.UtcNow;
                await EnqueueEmployeeChangedAsync(employee, ct);
                linkedUpdated++;
            }
        }

        var takenIds = employees
            .Where(e => e.IdTechnicien.HasValue)
            .Select(e => e.IdTechnicien!.Value)
            .ToHashSet();

        foreach (var employee in employees.Where(e => !e.IdTechnicien.HasValue))
        {
            var available = techniciens.Where(t => !takenIds.Contains(t.IdTechnicien)).ToList();
            if (!TryResolveUniqueMatch(employee, available, out var tech))
                continue;

            if (employees.Any(e => e.IdTechnicien == tech.IdTechnicien && e.Id != employee.Id))
                continue;

            ApplyMirror(employee, tech);
            await EnqueueEmployeeChangedAsync(employee, ct);
            takenIds.Add(tech.IdTechnicien);
            newlyLinked++;
        }

        await db.SaveChangesAsync(ct);

        var report = BuildReport(techniciens, employees);
        logger.LogInformation(
            "HTEL sync: fetched={Fetched}, updated={Updated}, newlyLinked={New}, orphans={Orphans}, ambiguous={Ambiguous}",
            techniciens.Count, linkedUpdated, newlyLinked, report.OrphansHtel.Count, report.Ambiguous.Count);

        return new HtelSyncReportDto(
            techniciens.Count,
            linkedUpdated,
            newlyLinked,
            report.OrphansHtel.Count,
            report.Ambiguous.Count,
            report.UnlinkedEmployees.Count);
    }

    public async Task<bool> LinkAsync(Guid employeeId, int idTechnicien, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;

        var techniciens = await htelClient.GetTechniciensAsync(ct);
        var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == idTechnicien);
        if (tech is null)
            throw new InvalidOperationException($"Technicien HTEL {idTechnicien} introuvable.");

        var taken = await db.Employees.AnyAsync(
            e => e.IdTechnicien == idTechnicien && e.Id != employeeId, ct);
        if (taken)
            throw new InvalidOperationException($"Id technicien {idTechnicien} déjà lié à un autre employé.");

        ApplyMirror(employee, tech);
        await EnqueueEmployeeChangedAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnlinkAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;

        employee.IdTechnicien = null;
        employee.HtelCode = null;
        employee.HtelTechnicienName = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ApplyLinkOnEmployeeAsync(Employee employee, int? explicitIdTechnicien, CancellationToken ct = default)
    {
        try
        {
            var techniciens = await htelClient.GetTechniciensAsync(ct);

            if (explicitIdTechnicien.HasValue)
            {
                var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == explicitIdTechnicien.Value);
                if (tech is null)
                    throw new InvalidOperationException($"Technicien HTEL {explicitIdTechnicien.Value} introuvable.");

                var taken = await db.Employees.AnyAsync(
                    e => e.IdTechnicien == explicitIdTechnicien.Value && e.Id != employee.Id, ct);
                if (taken)
                    throw new InvalidOperationException(
                        $"Id technicien {explicitIdTechnicien.Value} déjà lié à un autre employé.");

                ApplyMirror(employee, tech);
                return;
            }

            if (employee.IdTechnicien.HasValue)
            {
                var tech = techniciens.FirstOrDefault(t => t.IdTechnicien == employee.IdTechnicien.Value);
                if (tech is not null)
                    employee.HtelCode = tech.Code;
                    employee.HtelTechnicienName = string.IsNullOrWhiteSpace(tech.Technicien)
                        ? employee.HtelTechnicienName
                        : tech.Technicien.Trim();
                return;
            }

            var takenIds = await db.Employees.AsNoTracking()
                .Where(e => e.IdTechnicien != null && e.Id != employee.Id)
                .Select(e => e.IdTechnicien!.Value)
                .ToListAsync(ct);
            var takenSet = takenIds.ToHashSet();
            var available = techniciens.Where(t => !takenSet.Contains(t.IdTechnicien)).ToList();
            if (TryResolveUniqueMatch(employee, available, out var matched))
                ApplyMirror(employee, matched);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTEL indisponible lors de la liaison automatique pour {EmployeeId}", employee.Id);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Timeout HTEL lors de la liaison automatique pour {EmployeeId}", employee.Id);
        }
    }

    private static void ApplyMirror(Employee employee, HtelTechnicienDto tech)
    {
        employee.IdTechnicien = tech.IdTechnicien;
        employee.HtelCode = tech.Code;
        employee.HtelTechnicienName = string.IsNullOrWhiteSpace(tech.Technicien)
            ? null
            : tech.Technicien.Trim();
        employee.UpdatedAt = DateTime.UtcNow;
    }

    private static bool TryResolveUniqueMatch(
        Employee employee,
        IReadOnlyList<HtelTechnicienDto> available,
        out HtelTechnicienDto tech)
    {
        tech = null!;
        var scored = available
            .Select(t => (
                Tech: t,
                Score: HtelNameNormalizer.ScoreEmployeeToTechnicien(
                    employee.FirstName, employee.LastName, t.Technicien)))
            .Where(x => x.Score >= HtelNameNormalizer.AutoLinkThreshold)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            return false;

        if (scored.Count == 1)
        {
            tech = scored[0].Tech;
            return true;
        }

        if (scored[0].Score - scored[1].Score >= HtelNameNormalizer.AmbiguityGap)
        {
            tech = scored[0].Tech;
            return true;
        }

        return false;
    }

    private static HtelLiaisonsReportDto BuildReport(
        IReadOnlyList<HtelTechnicienDto> techniciens,
        IReadOnlyList<Employee> employees)
    {
        var byId = techniciens.ToDictionary(t => t.IdTechnicien);
        var linkedIds = employees
            .Where(e => e.IdTechnicien.HasValue)
            .Select(e => e.IdTechnicien!.Value)
            .ToHashSet();

        var linked = employees
            .Where(e => e.IdTechnicien.HasValue)
            .Select(e =>
            {
                byId.TryGetValue(e.IdTechnicien!.Value, out var tech);
                return new HtelLinkedEmployeeDto(
                    e.Id.ToString(),
                    e.FirstName,
                    e.LastName,
                    e.Email,
                    e.IdTechnicien.Value,
                    e.HtelCode,
                    tech?.Technicien);
            })
            .ToList();

        var unlinked = employees.Where(e => !e.IdTechnicien.HasValue).ToList();
        var orphans = techniciens
            .Where(t => !linkedIds.Contains(t.IdTechnicien))
            .Where(t => CountNameMatches(t, unlinked) == 0)
            .Select(t => new HtelOrphanTechnicienDto(t.IdTechnicien, t.Technicien, t.Actif, t.Code))
            .ToList();

        var ambiguous = techniciens
            .Where(t => !linkedIds.Contains(t.IdTechnicien))
            .Select(t =>
            {
                var candidates = FindEmployeeCandidates(t, unlinked);
                return candidates.Count > 1
                    ? new HtelAmbiguousMatchDto(t.IdTechnicien, t.Technicien, t.Code, candidates)
                    : null;
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var unlinkedDtos = unlinked
            .Select(e => new HtelUnlinkedEmployeeDto(e.Id.ToString(), e.FirstName, e.LastName, e.Email))
            .ToList();

        return new HtelLiaisonsReportDto(linked, orphans, ambiguous, unlinkedDtos);
    }

    private static int CountNameMatches(HtelTechnicienDto tech, IReadOnlyList<Employee> employees) =>
        employees.Count(e =>
            HtelNameNormalizer.IsNameMatch(e.FirstName, e.LastName, tech.Technicien));

    private static IReadOnlyList<HtelEmployeeCandidateDto> FindEmployeeCandidates(
        HtelTechnicienDto tech,
        IReadOnlyList<Employee> employees) =>
        employees
            .Select(e => (
                Emp: e,
                Score: HtelNameNormalizer.ScoreEmployeeToTechnicien(
                    e.FirstName, e.LastName, tech.Technicien)))
            .Where(x => x.Score >= HtelNameNormalizer.AlertThreshold)
            .OrderByDescending(x => x.Score)
            .Select(x => new HtelEmployeeCandidateDto(
                x.Emp.Id.ToString(), x.Emp.FirstName, x.Emp.LastName, x.Emp.Email))
            .ToList();

    private async Task EnqueueEmployeeChangedAsync(Employee employee, CancellationToken ct)
    {
        var deptKind = employee.BusinessDepartmentId.HasValue
            ? (await db.BusinessDepartments.AsNoTracking()
                .Where(d => d.Id == employee.BusinessDepartmentId.Value)
                .Select(d => d.Kind)
                .FirstOrDefaultAsync(ct)).ToString()
            : null;

        await outbox.EnqueueAsync(new DirectoryEmployeeChangedMessage
        {
            EmployeeId = employee.Id,
            Email = employee.Email,
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            Role = employee.Role,
            ParentId = employee.ParentId,
            ServiceId = employee.ServiceId,
            CelluleId = employee.CelluleId,
            PoleId = employee.PoleId,
            BusinessDepartmentId = employee.BusinessDepartmentId,
            BusinessDepartmentKind = deptKind,
            IsActive = employee.IsActive,
            IsDeleted = false,
            HireDate = employee.HireDate,
            ChefDeProjetId = employee.ChefDeProjetId,
            SuperviseurId = employee.SuperviseurId,
            ReferentTechniqueId = employee.ReferentTechniqueId,
            IdTechnicien = employee.IdTechnicien,
            HtelCode = employee.HtelCode,
            HtelTechnicienName = employee.HtelTechnicienName,
        }, aggregateId: employee.Id.ToString(), ct: ct);
    }
}
