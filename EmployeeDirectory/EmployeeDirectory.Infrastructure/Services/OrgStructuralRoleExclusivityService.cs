using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Messaging;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class OrgStructuralRoleExclusivityService(
    DirectoryDbContext db,
    IOutboxWriter outbox) : IOrgStructuralRoleExclusivityService
{
    public Task<IReadOnlyList<RevokedStructuralRoleDto>> RevokeAllStructuralRolesForEmployeeAsync(
        Guid employeeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default) =>
        RevokeStructuralRolesCoreAsync(
            employeeId,
            keepKind: null,
            exclusiveWithinKind: false,
            clearAllIncludingSameKind: true,
            changedBy,
            reason,
            ct);

    public Task<IReadOnlyList<RevokedStructuralRoleDto>> RevokeConflictingStructuralRolesForEmployeeAsync(
        Guid employeeId,
        DomainAssignmentKind keepKind,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default) =>
        RevokeStructuralRolesCoreAsync(
            employeeId,
            keepKind,
            exclusiveWithinKind: keepKind == DomainAssignmentKind.Pilote,
            clearAllIncludingSameKind: false,
            changedBy,
            reason,
            ct);

    private async Task<IReadOnlyList<RevokedStructuralRoleDto>> RevokeStructuralRolesCoreAsync(
        Guid employeeId,
        DomainAssignmentKind? keepKind,
        bool exclusiveWithinKind,
        bool clearAllIncludingSameKind,
        Guid? changedBy,
        string? reason,
        CancellationToken ct)
    {
        var revoked = new List<RevokedStructuralRoleDto>();
        var now = DateTime.UtcNow;

        var activeAssignments = await db.OrgAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .ToListAsync(ct);

        var alreadyHasKeepKind = keepKind.HasValue
            && activeAssignments.Any(a => a.Kind == keepKind.Value);

        // clearAll / Pilote → tout révoquer ; sinon → uniquement les kinds incompatibles.
        var toRevoke = clearAllIncludingSameKind || exclusiveWithinKind
            ? activeAssignments
            : activeAssignments.Where(a => keepKind.HasValue && a.Kind != keepKind.Value).ToList();

        foreach (var row in toRevoke)
        {
            row.EffectiveTo = now;
            row.SupersededBy = Guid.NewGuid();
            db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
            {
                Id = Guid.NewGuid(),
                Kind = row.Kind,
                NodeId = row.NodeId,
                NodeLevel = row.NodeLevel,
                PreviousEmployeeId = employeeId,
                NewEmployeeId = null,
                ChangedBy = changedBy,
                ChangeReason = reason ?? "Révocation pour nouvelle affectation",
                ChangedAt = now,
            });

            var label = await ResolveNodeLabelAsync(row.Kind, row.NodeId, ct);
            revoked.Add(new RevokedStructuralRoleDto(
                RoleLabel(row.Kind),
                row.NodeId,
                label,
                null));

            await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
            {
                Kind = MessagingEnumMapper.ToMessage(row.Kind),
                NodeId = row.NodeId,
                NodeLevel = MessagingEnumMapper.ToMessage(row.NodeLevel),
                EmployeeId = employeeId,
                Removed = true,
            }, aggregateId: employeeId.ToString(), ct: ct);
        }

        // Départements managés : seulement lors d'une transition vers un kind structurel
        // (pas quand on ajoute un 2e nœud du même kind).
        var shouldClearManagedDepts = clearAllIncludingSameKind || !alreadyHasKeepKind;
        var managedDepts = shouldClearManagedDepts
            ? await db.BusinessDepartments
                .Where(d => d.ManagerEmployeeId == employeeId)
                .ToListAsync(ct)
            : [];

        foreach (var dept in managedDepts)
        {
            dept.ManagerEmployeeId = null;
            dept.UpdatedAt = now;
            revoked.Add(new RevokedStructuralRoleDto(
                "Manager",
                dept.Id.ToString(),
                dept.Name,
                dept.Code));

            if (dept.Kind == BusinessDepartmentKind.Support)
            {
                var teamMembers = await db.Employees
                    .Where(e => e.BusinessDepartmentId == dept.Id && e.ParentId == employeeId && e.IsActive)
                    .ToListAsync(ct);
                foreach (var member in teamMembers)
                {
                    member.ParentId = null;
                    member.UpdatedAt = now;
                    await EnqueueEmployeeChangedAsync(member, ct);
                }
            }

            await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        }

        var remainingSameKind = keepKind.HasValue
            && activeAssignments.Any(a => a.Kind == keepKind.Value && a.EffectiveTo == null);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is not null && (toRevoke.Count > 0 || managedDepts.Count > 0))
        {
            // Ne pas écraser le home si des charges du même kind restent actives
            // (ApplyAssignmentToEmployeeAsync mettra à jour l'ancre primaire ensuite).
            if (!remainingSameKind)
            {
                ResetEmployeeStructuralFields(employee);
                employee.UpdatedAt = now;
                await EnqueueEmployeeChangedAsync(employee, ct);
            }
        }

        return revoked;
    }

    public async Task DemotePreviousDepartmentManagerAsync(
        Guid departmentId,
        Guid newManagerId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (dept?.ManagerEmployeeId is null || dept.ManagerEmployeeId == newManagerId)
            return;

        var prevId = dept.ManagerEmployeeId.Value;
        var prev = await db.Employees.FirstOrDefaultAsync(e => e.Id == prevId, ct);
        if (prev is null) return;

        ResetEmployeeStructuralFields(prev);
        prev.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(prev, ct);
    }

    private static void ResetEmployeeStructuralFields(Employee employee)
    {
        employee.Role = KyntusRoleNames.Employee;
        employee.PoleId = null;
        employee.CelluleId = null;
        employee.ServiceId = null;
        employee.BusinessDepartmentId = null;
        employee.ParentId = null;
    }

    private static string RoleLabel(DomainAssignmentKind kind) => kind switch
    {
        DomainAssignmentKind.ChefDeProjet => "Chef de projet",
        DomainAssignmentKind.Superviseur => "Superviseur",
        DomainAssignmentKind.ReferentTechnique => "Référent technique",
        DomainAssignmentKind.Pilote => "Pilote",
        _ => kind.ToString(),
    };

    private async Task<string?> ResolveNodeLabelAsync(DomainAssignmentKind kind, string nodeId, CancellationToken ct) =>
        kind switch
        {
            DomainAssignmentKind.ChefDeProjet => await db.OrgPoles.AsNoTracking()
                .Where(p => p.Id == nodeId).Select(p => p.Name).FirstOrDefaultAsync(ct),
            DomainAssignmentKind.Superviseur => await db.OrgCellules.AsNoTracking()
                .Where(c => c.Id == nodeId).Select(c => c.Name).FirstOrDefaultAsync(ct),
            DomainAssignmentKind.ReferentTechnique or DomainAssignmentKind.Pilote => await db.OrgServices.AsNoTracking()
                .Where(s => s.Id == nodeId).Select(s => s.Name).FirstOrDefaultAsync(ct),
            _ => null,
        };

    private async Task EnqueueBusinessDepartmentChangedAsync(BusinessDepartment dept, CancellationToken ct)
    {
        var poleIds = await db.OrgPoles.AsNoTracking()
            .Where(p => p.BusinessDepartmentId == dept.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        await outbox.EnqueueAsync(new DirectoryBusinessDepartmentChangedMessage
        {
            BusinessDepartmentId = dept.Id,
            Code = dept.Code,
            Name = dept.Name,
            Kind = dept.Kind.ToString(),
            ManagerEmployeeId = dept.ManagerEmployeeId,
            PoleIds = poleIds,
        }, aggregateId: dept.Id.ToString(), ct: ct);
    }

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
            IdTechnicien = employee.IdTechnicien,
            HtelCode = employee.HtelCode,
            HtelTechnicienName = employee.HtelTechnicienName,
        }, aggregateId: employee.Id.ToString(), ct: ct);
    }
}
