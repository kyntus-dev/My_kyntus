using Microsoft.EntityFrameworkCore;
using Prime.Application.Abstractions;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class PrimeRbacReadService(PrimeDbContext db, PrimeOrgScopeService org)
{
    public async Task<bool> RoleHasActionAsync(string role, string action, CancellationToken ct = default)
    {
        var aliases = PermissionRoleAliases(role);
        var a = action.Trim();
        return await db.RbacPermissions.AsNoTracking()
            .AnyAsync(p => aliases.Contains(p.Role) && p.Action == a && p.IsAllowed, ct);
    }

    public async Task<List<string>> GetDistinctRolesAsync(CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Select(e => e.Role)
            .Where(r => r != null && r != "")
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync(ct);

    public async Task<List<string>> GetAllowedScopesAsync(string role, string action, CancellationToken ct = default)
    {
        var aliases = PermissionRoleAliases(role);
        var a = action.Trim();
        return await db.RbacPermissions.AsNoTracking()
            .Where(p => aliases.Contains(p.Role) && p.Action == a && p.IsAllowed)
            .Select(p => p.Scope)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Alias Coach ↔ Référent technique et RP ↔ Chef de projet pour la matrice RBAC.
    /// </summary>
    public static List<string> PermissionRoleAliases(string role)
    {
        var raw = (role ?? "").Trim();
        if (raw.Length == 0) return [];

        var expanded = IPrimeRequestUserResolver.ExpandRole(raw);
        var set = new HashSet<string>(StringComparer.Ordinal) { raw, expanded };
        if (IPrimeRequestUserResolver.RolesMatch(expanded, PrimeFicheValidationRoles.ReferentTechnique))
        {
            set.Add("Coach");
            set.Add(PrimeFicheValidationRoles.ReferentTechnique);
            set.Add("ReferentTechnique");
        }
        if (IPrimeRequestUserResolver.RolesMatch(expanded, PrimeFicheValidationRoles.ChefDeProjet))
        {
            set.Add("RP");
            set.Add(PrimeFicheValidationRoles.ChefDeProjet);
            set.Add("ChefDeProjet");
        }
        return set.ToList();
    }

    /// <summary>Correspondance rôle employé ↔ rôle valideur d’une étape workflow (RP → Chef de projet, etc.).</summary>
    public static bool RolesMatchWorkflowApprover(string employeeRole, string stepApproverRole)
    {
        if (string.Equals(employeeRole, stepApproverRole, StringComparison.Ordinal)) return true;
        if (string.Equals(employeeRole, "RP", StringComparison.Ordinal) &&
            string.Equals(stepApproverRole, "Chef de projet", StringComparison.Ordinal))
            return true;
        if (string.Equals(employeeRole, "Coach", StringComparison.Ordinal) &&
            string.Equals(stepApproverRole, PrimeFicheValidationRoles.ReferentTechnique, StringComparison.Ordinal))
            return true;
        if (string.Equals(employeeRole, PrimeFicheValidationRoles.ReferentTechnique, StringComparison.Ordinal) &&
            string.Equals(stepApproverRole, "Coach", StringComparison.Ordinal))
            return true;
        return IPrimeRequestUserResolver.RolesMatch(employeeRole, stepApproverRole);
    }

    /// <summary>Vrai si une étape active attend ce rôle sur le statut courant de la fiche.</summary>
    public async Task<bool> IsWorkflowValidationTurnAsync(
        Employee actor,
        EmployeePrimeServiceFiche fiche,
        CancellationToken ct)
    {
        var steps = await db.WorkflowSteps.AsNoTracking()
            .Where(s => s.IsActive && s.FromStatus == fiche.ValidationStatus)
            .ToListAsync(ct);
        return steps.Any(s => RolesMatchWorkflowApprover(actor.Role, s.ApproverRole));
    }

    private static bool IsReferentTechniqueRole(string role) =>
        PrimeOrgScopeService.IsReferentTechniqueRole(role);

    private static bool IsSuperviseurRole(string role) =>
        PrimeOrgScopeService.IsSuperviseurRole(role);

    private static bool IsChefDeProjetRole(string role) =>
        PrimeOrgScopeService.IsChefDeProjetRole(role);

    /// <summary>Pilote dans le périmètre du référent technique (responsabilité explicite ou hiérarchie RH).</summary>
    public Task<bool> IsPiloteUnderReferentAsync(
        Employee referent,
        EmployeePrimeServiceFiche fiche,
        CancellationToken ct = default) =>
        org.IsFicheUnderReferentResponsibilityAsync(referent.Id, fiche, referent.Role, ct);

    /// <summary>Clone employé avec le rôle d’action UI (validation en mode changement de rôle).</summary>
    public static Employee WithActingRole(Employee source, string actingRole) =>
        new()
        {
            Id = source.Id,
            FirstName = source.FirstName,
            LastName = source.LastName,
            Role = actingRole.Trim(),
            ParentId = source.ParentId,
            ServiceId = source.ServiceId,
            CelluleId = source.CelluleId,
            PoleId = source.PoleId,
            Email = source.Email,
            Avatar = source.Avatar,
            BusinessDepartmentId = source.BusinessDepartmentId,
            BusinessDepartmentKind = source.BusinessDepartmentKind,
        };

    public async Task<bool> CanAccessFicheAsync(Employee actor, EmployeePrimeServiceFiche fiche, string action, CancellationToken ct)
    {
        if (!await RoleHasActionAsync(actor.Role, action, ct)) return false;

        var isRead = string.Equals(action, "Read", StringComparison.OrdinalIgnoreCase);
        var isValidate = string.Equals(action, "Validate", StringComparison.OrdinalIgnoreCase);

        if (IsReferentTechniqueRole(actor.Role))
        {
            var inScope = await org.IsFicheUnderReferentResponsibilityAsync(actor.Id, fiche, actor.Role, ct);
            if (isRead) return inScope;
            if (isValidate) return inScope && await IsWorkflowValidationTurnAsync(actor, fiche, ct);
            return false;
        }

        if (IsSuperviseurRole(actor.Role))
        {
            var inScope = await org.IsFicheUnderSupervisorResponsibilityAsync(actor.Id, fiche, actor.Role, ct);
            if (isRead) return inScope;
            if (isValidate) return inScope && await IsWorkflowValidationTurnAsync(actor, fiche, ct);
            return false;
        }

        if (IsChefDeProjetRole(actor.Role))
        {
            var inScope = await org.IsFicheUnderChefResponsibilityAsync(actor.Id, fiche, actor.Role, ct);
            if (isRead) return inScope;
            if (isValidate) return inScope && await IsWorkflowValidationTurnAsync(actor, fiche, ct);
            return false;
        }

        var scopes = await GetAllowedScopesAsync(actor.Role, action, ct);
        foreach (var raw in scopes)
        {
            var scope = raw.Trim();
            if (scope == "Global") return true;
            if (scope == "Self" && string.Equals(fiche.EmployeeId, actor.Id, StringComparison.Ordinal)) return true;
            if (scope == "Service" && string.Equals(fiche.ServiceId, actor.ServiceId, StringComparison.Ordinal)) return true;
            if (scope == "Cellule" && string.Equals(fiche.CelluleId, actor.CelluleId, StringComparison.Ordinal)) return true;
            if (scope == "Pole")
            {
                var poleId = await db.Cellules.AsNoTracking()
                    .Where(c => c.Id == fiche.CelluleId)
                    .Select(c => c.PoleId)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrEmpty(poleId) && string.Equals(poleId, actor.PoleId, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}
