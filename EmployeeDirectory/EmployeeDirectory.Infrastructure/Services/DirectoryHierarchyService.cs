using EmployeeDirectory.Domain.Entities;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class DirectoryHierarchyService(DirectoryDbContext db)
{
    public async Task<Guid?> ResolveDefaultParentIdAsync(Employee employee, CancellationToken ct)
    {
        if (employee.ReferentTechniqueId.HasValue)
            return employee.ReferentTechniqueId;
        if (employee.SuperviseurId.HasValue)
            return employee.SuperviseurId;
        if (employee.ChefDeProjetId.HasValue)
            return employee.ChefDeProjetId;
        if (employee.ParentId.HasValue)
            return employee.ParentId;

        if (employee.BusinessDepartmentId.HasValue)
        {
            var dept = await db.BusinessDepartments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == employee.BusinessDepartmentId.Value, ct);
            if (dept?.Kind == BusinessDepartmentKind.Support && dept.ManagerEmployeeId.HasValue)
                return dept.ManagerEmployeeId;
        }

        if (!string.IsNullOrWhiteSpace(employee.ServiceId))
        {
            var referent = await FindActiveAssigneeAsync(DomainAssignmentKind.ReferentTechnique, employee.ServiceId!, ct);
            if (referent.HasValue) return referent;
        }

        if (!string.IsNullOrWhiteSpace(employee.CelluleId))
        {
            var sup = await FindActiveAssigneeAsync(DomainAssignmentKind.Superviseur, employee.CelluleId!, ct);
            if (sup.HasValue) return sup;
        }

        if (!string.IsNullOrWhiteSpace(employee.PoleId))
        {
            var cp = await FindActiveAssigneeAsync(DomainAssignmentKind.ChefDeProjet, employee.PoleId!, ct);
            if (cp.HasValue) return cp;
        }

        return null;
    }

    public async Task ApplyAssignmentToEmployeeAsync(Employee employee, DomainAssignmentKind kind, string nodeId, CancellationToken ct)
    {
        // ParentId est une dénormalisation d'affichage (organigramme) uniquement.
        // L'habilitation multi-responsables se résout via OrgAssignment / OrgResponsibilityResolver.
        switch (kind)
        {
            case DomainAssignmentKind.ChefDeProjet:
                employee.Role = KyntusRoleNames.ChefDeProjet;
                employee.PoleId = nodeId;
                employee.CelluleId = null;
                employee.ServiceId = null;
                employee.ParentId = null;
                break;
            case DomainAssignmentKind.Superviseur:
                employee.Role = KyntusRoleNames.Superviseur;
                employee.CelluleId = nodeId;
                var cell = await db.OrgCellules.AsNoTracking().FirstOrDefaultAsync(c => c.Id == nodeId, ct);
                employee.PoleId = cell?.PoleId;
                employee.ParentId = cell?.PoleId is null
                    ? null
                    : await FindActiveAssigneeAsync(DomainAssignmentKind.ChefDeProjet, cell.PoleId, ct);
                break;
            case DomainAssignmentKind.ReferentTechnique:
                employee.Role = KyntusRoleNames.ReferentTechnique;
                employee.ServiceId = nodeId;
                var svc = await db.OrgServices.AsNoTracking().Include(s => s.Cellule).FirstOrDefaultAsync(s => s.Id == nodeId, ct);
                if (svc is not null)
                {
                    employee.CelluleId = svc.CelluleId;
                    employee.PoleId = svc.Cellule.PoleId;
                    employee.ParentId = await FindActiveAssigneeAsync(DomainAssignmentKind.Superviseur, svc.CelluleId, ct);
                }
                break;
            case DomainAssignmentKind.Pilote:
                // Un membre de la cellule IT placé sur un service garde son rôle.
                if (!KyntusRoleNames.IsEquipeIt(employee.Role))
                    employee.Role = KyntusRoleNames.Pilote;
                employee.ServiceId = nodeId;
                var pilotSvc = await db.OrgServices.AsNoTracking().Include(s => s.Cellule).FirstOrDefaultAsync(s => s.Id == nodeId, ct);
                if (pilotSvc is not null)
                {
                    employee.CelluleId = pilotSvc.CelluleId;
                    employee.PoleId = pilotSvc.Cellule.PoleId;
                }
                employee.ParentId = await FindActiveAssigneeAsync(DomainAssignmentKind.ReferentTechnique, nodeId, ct)
                    ?? await ResolveDefaultParentIdAsync(employee, ct);
                break;
        }
    }

    /// <summary>
    /// Choix déterministe pour l'affichage : titulaire actif le plus ancien sur le nœud.
    /// </summary>
    private async Task<Guid?> FindActiveAssigneeAsync(DomainAssignmentKind kind, string nodeId, CancellationToken ct)
    {
        var row = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.Kind == kind && a.NodeId == nodeId && a.EffectiveTo == null)
            .OrderBy(a => a.EffectiveFrom)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(ct);
        return row?.EmployeeId;
    }
}
