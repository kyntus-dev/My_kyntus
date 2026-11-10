using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Exceptions;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using DomainNodeLevel = EmployeeDirectory.Domain.Enums.OrgNodeLevel;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class PilotRotationTenureService(DirectoryDbContext db) : IPilotRotationTenureService
{
    public const int MinimumMonthsOnService = 6;
    public const string OverrideReasonPrefix = "[Dérogation]";

    public async Task BootstrapProjectedPilotsAsync(CancellationToken ct = default)
    {
        var pilots = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive
                && e.Role == KyntusRoleNames.Pilote
                && e.ServiceId != null
                && e.ServiceId != "")
            .Select(e => new { e.Id, e.ServiceId, e.HireDate, e.CreatedAt })
            .ToListAsync(ct);

        if (pilots.Count == 0) return;

        var employeeIds = pilots.Select(p => p.Id).ToList();
        var withActive = await db.OrgAssignments.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                && a.Kind == DomainAssignmentKind.Pilote
                && a.EffectiveTo == null)
            .Select(a => a.EmployeeId)
            .ToListAsync(ct);
        var hasActive = withActive.ToHashSet();

        var now = DateTime.UtcNow;
        foreach (var pilot in pilots)
        {
            if (hasActive.Contains(pilot.Id)) continue;

            var effectiveFrom = pilot.HireDate == default
                ? pilot.CreatedAt
                : pilot.HireDate;
            if (effectiveFrom > now) effectiveFrom = now;

            db.OrgAssignments.Add(new OrgAssignment
            {
                Id = Guid.NewGuid(),
                Kind = DomainAssignmentKind.Pilote,
                NodeId = pilot.ServiceId!.Trim(),
                NodeLevel = DomainNodeLevel.Service,
                EmployeeId = pilot.Id,
                EffectiveFrom = effectiveFrom,
                ChangeReason = "Bootstrap affectation pilote projetée",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<PilotRotationEligibilityDto> GetEligibilityAsync(
        Guid employeeId,
        string targetServiceId,
        CancellationToken ct = default)
    {
        var target = targetServiceId.Trim();
        var current = await ResolveCurrentPlacementAsync(employeeId, ct);
        if (current is null || string.IsNullOrWhiteSpace(current.ServiceId))
        {
            return new PilotRotationEligibilityDto(
                Eligible: true,
                IsSameService: false,
                CurrentServiceId: null,
                CurrentServiceName: null,
                CurrentSince: null,
                EligibleAt: null,
                DaysRemaining: 0);
        }

        if (string.Equals(current.ServiceId, target, StringComparison.OrdinalIgnoreCase))
        {
            var sameName = await ResolveServiceNameAsync(current.ServiceId, ct);
            return new PilotRotationEligibilityDto(
                Eligible: true,
                IsSameService: true,
                CurrentServiceId: current.ServiceId,
                CurrentServiceName: sameName,
                CurrentSince: current.EffectiveFrom,
                EligibleAt: null,
                DaysRemaining: 0);
        }

        var eligibleAt = current.EffectiveFrom.AddMonths(MinimumMonthsOnService);
        var now = DateTime.UtcNow;
        var eligible = eligibleAt <= now;
        var daysRemaining = eligible ? 0 : Math.Max(1, (int)Math.Ceiling((eligibleAt - now).TotalDays));
        var serviceName = await ResolveServiceNameAsync(current.ServiceId, ct);

        return new PilotRotationEligibilityDto(
            Eligible: eligible,
            IsSameService: false,
            CurrentServiceId: current.ServiceId,
            CurrentServiceName: serviceName,
            CurrentSince: current.EffectiveFrom,
            EligibleAt: eligible ? null : eligibleAt,
            DaysRemaining: daysRemaining);
    }

    public async Task ValidateRotationAsync(
        Guid employeeId,
        string targetServiceId,
        bool forceTenureOverride,
        string? reason,
        CancellationToken ct = default)
    {
        // La règle des 6 mois vise la rotation des pilotes : elle ne s'applique ni à la cellule IT
        // ni à un compte Admin.
        var role = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.Role)
            .FirstOrDefaultAsync(ct);
        if (EstExempte(role)) return;

        var eligibility = GetEligibilityAsync(employeeId, targetServiceId, ct);
        await ValidateFromEligibilityAsync(eligibility, forceTenureOverride, reason);
    }

    /// <summary>Rôles non soumis à la règle des 6 mois (employé concerné ou auteur du changement).</summary>
    public static bool EstExempte(string? role) =>
        KyntusRoleNames.IsEquipeIt(role)
        || string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase);

    private static async Task ValidateFromEligibilityAsync(
        Task<PilotRotationEligibilityDto> eligibilityTask,
        bool forceTenureOverride,
        string? reason)
    {
        var eligibility = await eligibilityTask;
        if (eligibility.Eligible || eligibility.IsSameService)
            return;

        if (forceTenureOverride)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidOperationException(
                    "Un motif est obligatoire pour une dérogation à la règle des 6 mois.");
            }
            return;
        }

        var serviceLabel = eligibility.CurrentServiceName ?? eligibility.CurrentServiceId ?? "service actuel";
        throw new PilotRotationTenureException(
            $"Rotation impossible : l'employé doit rester au moins {MinimumMonthsOnService} mois sur « {serviceLabel} » " +
            $"({eligibility.DaysRemaining} jour(s) restant(s)).",
            eligibility.CurrentServiceId,
            eligibility.CurrentSince,
            eligibility.EligibleAt ?? DateTime.UtcNow,
            eligibility.DaysRemaining);
    }

    public async Task<IReadOnlyList<PilotRotationHistoryEntryDto>> GetRotationHistoryAsync(
        Guid employeeId,
        CancellationToken ct = default)
    {
        var segments = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Kind == DomainAssignmentKind.Pilote)
            .OrderByDescending(a => a.EffectiveFrom)
            .Take(100)
            .ToListAsync(ct);

        var serviceIds = segments.Select(s => s.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var names = await db.OrgServices.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase, ct);

        return segments.Select(s =>
        {
            int? duration = null;
            if (s.EffectiveTo is { } end)
                duration = Math.Max(0, (int)Math.Floor((end - s.EffectiveFrom).TotalDays));
            else
                duration = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - s.EffectiveFrom).TotalDays));

            return new PilotRotationHistoryEntryDto(
                s.NodeId,
                names.GetValueOrDefault(s.NodeId) ?? s.NodeId,
                s.EffectiveFrom,
                s.EffectiveTo,
                duration,
                s.ChangeReason,
                IsOverrideReason(s.ChangeReason));
        }).ToList();
    }

    public async Task<IReadOnlyList<PilotRotationSummaryDto>> ListRotationSummariesAsync(
        string? serviceId,
        DateTime? from,
        DateTime? to,
        int? minRotations,
        int? maxRotations,
        string? sort,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var serviceFilter = string.IsNullOrWhiteSpace(serviceId) ? null : serviceId.Trim();

        var assignments = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.Kind == DomainAssignmentKind.Pilote)
            .ToListAsync(ct);

        if (from.HasValue || to.HasValue)
        {
            var rangeStart = from ?? DateTime.MinValue;
            var rangeEnd = to ?? DateTime.MaxValue;
            assignments = assignments
                .Where(a =>
                {
                    var end = a.EffectiveTo ?? now;
                    return a.EffectiveFrom <= rangeEnd && end >= rangeStart;
                })
                .ToList();
        }

        if (serviceFilter is not null)
        {
            assignments = assignments
                .Where(a => string.Equals(a.NodeId, serviceFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var employeeIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        if (employeeIds.Count == 0)
            return Array.Empty<PilotRotationSummaryDto>();

        var employees = await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var allServiceIds = assignments.Select(a => a.NodeId)
            .Concat(employees.Values.Where(e => !string.IsNullOrWhiteSpace(e.ServiceId)).Select(e => e.ServiceId!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serviceNames = await db.OrgServices.AsNoTracking()
            .Where(s => allServiceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase, ct);

        var currentAssignments = await db.OrgAssignments.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                        && a.Kind == DomainAssignmentKind.Pilote
                        && a.EffectiveTo == null)
            .ToListAsync(ct);
        var currentByEmployee = currentAssignments
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.EffectiveFrom).First());

        var summaries = new List<PilotRotationSummaryDto>();
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            if (!employees.TryGetValue(group.Key, out var employee))
                continue;

            var segments = group
                .OrderByDescending(a => a.EffectiveFrom)
                .Select(a =>
                {
                    var end = a.EffectiveTo;
                    var duration = end is { } e
                        ? Math.Max(0, (int)Math.Floor((e - a.EffectiveFrom).TotalDays))
                        : Math.Max(0, (int)Math.Floor((now - a.EffectiveFrom).TotalDays));
                    return new PilotRotationHistoryEntryDto(
                        a.NodeId,
                        serviceNames.GetValueOrDefault(a.NodeId) ?? a.NodeId,
                        a.EffectiveFrom,
                        a.EffectiveTo,
                        duration,
                        a.ChangeReason,
                        IsOverrideReason(a.ChangeReason));
                })
                .ToList();

            var count = segments.Count;
            if (minRotations.HasValue && count < minRotations.Value) continue;
            if (maxRotations.HasValue && count > maxRotations.Value) continue;

            string? currentServiceId = null;
            string? currentServiceName = null;
            if (currentByEmployee.TryGetValue(group.Key, out var current))
            {
                currentServiceId = current.NodeId;
                currentServiceName = serviceNames.GetValueOrDefault(current.NodeId) ?? current.NodeId;
            }
            else if (!string.IsNullOrWhiteSpace(employee.ServiceId))
            {
                currentServiceId = employee.ServiceId;
                currentServiceName = serviceNames.GetValueOrDefault(employee.ServiceId) ?? employee.ServiceId;
            }

            summaries.Add(new PilotRotationSummaryDto(
                employee.Id,
                employee.FirstName,
                employee.LastName,
                employee.Email,
                count,
                currentServiceId,
                currentServiceName,
                segments.MinBy(s => s.EffectiveFrom)?.EffectiveFrom,
                segments.MaxBy(s => s.EffectiveFrom)?.EffectiveFrom,
                segments));
        }

        var sortKey = (sort ?? "rotationCountDesc").Trim().ToLowerInvariant();
        IEnumerable<PilotRotationSummaryDto> ordered = sortKey switch
        {
            "rotationcountasc" => summaries.OrderBy(s => s.RotationCount).ThenBy(s => s.LastName),
            "name" => summaries.OrderBy(s => s.LastName).ThenBy(s => s.FirstName),
            _ => summaries.OrderByDescending(s => s.RotationCount).ThenBy(s => s.LastName),
        };

        return ordered.ToList();
    }

    public async Task ApplyRotationHrProfileAsync(
        Guid employeeId,
        string previousServiceId,
        CancellationToken ct = default)
    {
        var previousName = await ResolveServiceNameAsync(previousServiceId, ct) ?? previousServiceId;
        var profile = await db.EmployeeHrProfiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, ct);
        if (profile is null)
        {
            profile = new EmployeeHrProfile
            {
                EmployeeId = employeeId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.EmployeeHrProfiles.Add(profile);
        }

        profile.DateEvolutionPoste = DateOnly.FromDateTime(DateTime.UtcNow);
        profile.AncienPoste = KyntusRoleNames.Pilote;
        profile.AncienService = previousName;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static string FormatOverrideReason(string reason) =>
        reason.TrimStart().StartsWith(OverrideReasonPrefix, StringComparison.Ordinal)
            ? reason.Trim()
            : $"{OverrideReasonPrefix} {reason.Trim()}";

    public static bool IsOverrideReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && reason.TrimStart().StartsWith(OverrideReasonPrefix, StringComparison.Ordinal);

    private async Task<PlacementSegment?> ResolveCurrentPlacementAsync(Guid employeeId, CancellationToken ct)
    {
        var active = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                && a.Kind == DomainAssignmentKind.Pilote
                && a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (active is not null)
        {
            return new PlacementSegment(active.NodeId, active.EffectiveFrom);
        }

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null
            || !KyntusRoleNames.IsPilote(employee.Role)
            || string.IsNullOrWhiteSpace(employee.ServiceId))
        {
            return null;
        }

        var effectiveFrom = employee.HireDate == default ? employee.CreatedAt : employee.HireDate;
        return new PlacementSegment(employee.ServiceId.Trim(), effectiveFrom);
    }

    private async Task<string?> ResolveServiceNameAsync(string serviceId, CancellationToken ct)
    {
        var trimmed = serviceId.Trim();
        return await db.OrgServices.AsNoTracking()
            .Where(s => s.Id == trimmed)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);
    }

    private sealed record PlacementSegment(string ServiceId, DateTime EffectiveFrom);
}
