using Documentation.Infrastructure.Context;
using Documentation.Infrastructure.Persistence;
using Documentation.Domain.Entities;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Documentation.Infrastructure.Services;

public sealed class DirectoryUserSyncService(
    DocumentationDbContext db,
    IConfiguration configuration,
    ILogger<DirectoryUserSyncService> logger)
{
    public Task UpsertFromEmployeMessageAsync(EmployeUpdatedMessage msg, bool skipOrgStructureFields, CancellationToken ct) =>
        UpsertFromEmployeMessageAsync(new EmployeCreatedMessage
        {
            EmployeId = msg.EmployeId,
            Nom = msg.Nom,
            Prenom = msg.Prenom,
            Email = msg.Email,
            ManagerId = msg.ManagerId,
            ServiceId = msg.ServiceId,
            ServiceNom = msg.ServiceNom,
            DateEmbauche = DateTime.UtcNow,
            EstMineur = false,
            Role = msg.Role,
            SubServiceId = msg.SubServiceId,
            PrimeServiceId = msg.PrimeServiceId,
            SupervisorId = msg.SupervisorId
        }, skipOrgStructureFields, ct);

    public async Task UpsertFromEmployeMessageAsync(
        EmployeCreatedMessage msg,
        bool skipOrgStructureFields,
        CancellationToken ct)
    {
        var tenantId = DocumentationTenantAccessor.ResolveConfiguredDefault(configuration);
        var email = msg.Email.Trim().ToLowerInvariant();
        var roleName = KyntusPortalRoleMapping.ToDocumentationRoleName(msg.Role);
        if (!Enum.TryParse<AppRole>(roleName, ignoreCase: true, out var appRole))
            appRole = AppRole.Pilote;

        var defaultPole = ParseGuid(configuration["Documentation:Sync:DefaultPoleId"], "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa01");
        var defaultCellule = ParseGuid(configuration["Documentation:Sync:DefaultCelluleId"], "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa02");
        var defaultDept = ParseGuid(configuration["Documentation:Sync:DefaultDepartementId"], "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa03");

        Guid poleId = defaultPole;
        Guid celluleId = defaultCellule;
        Guid departementId = defaultDept;

        if (!skipOrgStructureFields && !string.IsNullOrWhiteSpace(msg.PrimeServiceId))
        {
            departementId = KyntusStableGuid.FromPrimeOrgId(msg.PrimeServiceId);
            var deptUnit = await db.OrganisationUnits.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == departementId, ct);
            if (deptUnit?.ParentId is Guid cellId)
            {
                celluleId = cellId;
                var cellUnit = await db.OrganisationUnits.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == cellId, ct);
                if (cellUnit?.ParentId is Guid pId)
                    poleId = pId;
            }
        }

        Guid? managerId = msg.SupervisorId != Guid.Empty ? msg.SupervisorId : null;
        Guid? coachId = null;
        Guid? rpId = null;

        if (!skipOrgStructureFields)
        {
            if (appRole == AppRole.Pilote)
                managerId = msg.SupervisorId != Guid.Empty ? msg.SupervisorId : managerId;
            else if (appRole == AppRole.Coach)
                managerId = msg.SupervisorId != Guid.Empty ? msg.SupervisorId : null;
            else if (appRole == AppRole.Manager)
                rpId = msg.SupervisorId != Guid.Empty ? msg.SupervisorId : null;
        }

        var now = DateTimeOffset.UtcNow;
        var row = await db.DirectoryUsers.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Email.ToLower() == email, ct);

        if (row is null)
        {
            db.DirectoryUsers.Add(new DirectoryUser
            {
                Id = msg.EmployeId,
                TenantId = tenantId,
                Prenom = msg.Prenom,
                Nom = msg.Nom,
                Email = email,
                Role = appRole,
                ManagerId = skipOrgStructureFields ? null : managerId,
                CoachId = skipOrgStructureFields ? null : coachId,
                RpId = skipOrgStructureFields ? null : rpId,
                PoleId = skipOrgStructureFields ? defaultPole : poleId,
                CelluleId = skipOrgStructureFields ? defaultCellule : celluleId,
                DepartementId = skipOrgStructureFields ? defaultDept : departementId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            row.Id = msg.EmployeId;
            row.Prenom = msg.Prenom;
            row.Nom = msg.Nom;
            row.Email = email;
            if (!skipOrgStructureFields)
            {
                row.Role = appRole;
                row.ManagerId = managerId;
                row.CoachId = coachId;
                row.RpId = rpId;
                row.PoleId = poleId;
                row.CelluleId = celluleId;
                row.DepartementId = departementId;
            }
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DOCUMENTATION directory_users sync {Email} rôle={Role}", email, appRole);
    }

    public async Task ApplyHrProfileFromMessageAsync(DirectoryEmployeeHrProfileChangedMessage msg, CancellationToken ct)
    {
        if (msg.IsDeleted)
            return;

        var tenantId = DocumentationTenantAccessor.ResolveConfiguredDefault(configuration);
        var row = await db.DirectoryUsers.FirstOrDefaultAsync(
            u => u.TenantId == tenantId && u.Id == msg.EmployeeId, ct);
        if (row is null)
        {
            logger.LogWarning(
                "DOCUMENTATION HR profile sync : directory_user absent id={EmployeeId}",
                msg.EmployeeId);
            return;
        }

        row.Cin = msg.Cin;
        row.Rib = msg.Rib;
        row.ImmatriculationCnss = msg.ImmatriculationCnss;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("DOCUMENTATION HR profile sync {EmployeeId}", msg.EmployeeId);
    }

    public async Task ApplyOrgAssignmentAsync(OrgAssignmentChangedMessage msg, CancellationToken ct)
    {
        if (msg.Removed || string.IsNullOrWhiteSpace(msg.EmployeeId))
            return;

        var tenantId = DocumentationTenantAccessor.ResolveConfiguredDefault(configuration);
        var roleName = !string.IsNullOrWhiteSpace(msg.NewRole)
            ? msg.NewRole
            : msg.Kind switch
            {
                OrgAssignmentKind.ChefDeProjet => KyntusRoleNames.ChefDeProjet,
                OrgAssignmentKind.Superviseur => KyntusRoleNames.Superviseur,
                OrgAssignmentKind.ReferentTechnique => KyntusRoleNames.ReferentTechnique,
                OrgAssignmentKind.Pilote => KyntusRoleNames.Pilote,
                _ => KyntusRoleNames.Employee
            };

        var docRoleName = KyntusPortalRoleMapping.ToDocumentationRoleName(roleName);
        if (!Enum.TryParse<AppRole>(docRoleName, ignoreCase: true, out var appRole))
            appRole = AppRole.Pilote;

        var employeeId = msg.EmployeeId.Trim();
        var email = msg.EmployeeEmail?.Trim().ToLowerInvariant();
        Guid? employeeGuid = Guid.TryParse(employeeId, out var parsed) ? parsed : null;

        var row = await db.DirectoryUsers.FirstOrDefaultAsync(
            u => u.TenantId == tenantId
                 && ((employeeGuid.HasValue && u.Id == employeeGuid.Value)
                     || (email != null && u.Email.ToLower() == email)),
            ct);

        if (row is null)
        {
            logger.LogWarning("DOCUMENTATION OrgAssignment : directory_user absent id={Id}", employeeId);
            return;
        }

        if (employeeGuid.HasValue)
            row.Id = employeeGuid.Value;
        if (email is not null)
            row.Email = email;
        row.Role = appRole;

        if (!string.IsNullOrWhiteSpace(msg.ParentEmployeeId)
            && Guid.TryParse(msg.ParentEmployeeId.Trim(), out var parentId)
            && parentId != Guid.Empty)
        {
            ApplyHierarchyFromParent(row, appRole, parentId);
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("DOCUMENTATION OrgAssignment sync {Email} rôle={Role}", row.Email, appRole);
    }

    private static void ApplyHierarchyFromParent(DirectoryUser row, AppRole appRole, Guid parentId)
    {
        switch (appRole)
        {
            case AppRole.Pilote:
                row.ManagerId = parentId;
                break;
            case AppRole.Coach:
                row.ManagerId = parentId;
                break;
            case AppRole.Manager:
                row.RpId = parentId;
                break;
        }
    }

    public async Task UpsertOrganisationUnitAsync(OrgNodeCreatedMessage msg, CancellationToken ct)
    {
        var tenantId = DocumentationTenantAccessor.ResolveConfiguredDefault(configuration);
        var id = KyntusStableGuid.FromPrimeOrgId(msg.NodeId);
        var unitType = msg.Level switch
        {
            OrgNodeLevel.Pole => "pole",
            OrgNodeLevel.Cellule => "cellule",
            OrgNodeLevel.Service => "departement",
            _ => "departement"
        };
        Guid? parentId = string.IsNullOrWhiteSpace(msg.ParentNodeId)
            ? null
            : KyntusStableGuid.FromPrimeOrgId(msg.ParentNodeId);

        var now = DateTimeOffset.UtcNow;
        var row = await db.OrganisationUnits.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (row is null)
        {
            db.OrganisationUnits.Add(new OrganisationUnit
            {
                Id = id,
                TenantId = tenantId,
                ParentId = parentId,
                UnitType = unitType,
                Code = string.IsNullOrWhiteSpace(msg.Code) ? msg.NodeId : msg.Code,
                Name = msg.Name,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            row.Name = msg.Name;
            row.ParentId = parentId;
            row.UnitType = unitType;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DOCUMENTATION organisation_units sync {Type} {Name}", unitType, msg.Name);
    }

    private static Guid ParseGuid(string? value, string fallback) =>
        Guid.TryParse(value, out var g) ? g : Guid.Parse(fallback);
}
