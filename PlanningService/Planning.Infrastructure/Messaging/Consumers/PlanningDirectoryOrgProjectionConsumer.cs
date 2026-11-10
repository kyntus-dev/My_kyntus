using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Infrastructure.Persistence;
using Planning.Domain.Entities;
using Planning.Infrastructure.Services;

namespace Planning.Infrastructure.Messaging.Consumers;

/// <summary>Projection org canonique Directory → miroir Planning (Floor/Service/SubService).</summary>
public sealed class PlanningDirectoryOrgProjectionConsumer(
    AppDbContext db,
    IUserService userService,
    ILogger<PlanningDirectoryOrgProjectionConsumer> logger) :
    IConsumer<DirectoryOrgNodeChangedMessage>,
    IConsumer<DirectoryAssignmentChangedMessage>
{
    public async Task Consume(ConsumeContext<DirectoryOrgNodeChangedMessage> context)
    {
        var msg = context.Message;
        if (msg.IsDeleted)
        {
            // Nœud supprimé dans l'Organisation RH : masqué dans Planning, historique conservé.
            await PlanningOrgMirrorArchive.ArchiveAsync(db, msg.Level, msg.NodeId, context.CancellationToken);
            return;
        }

        switch (msg.Level)
        {
            case OrgNodeLevel.Pole:
                await UpsertFloorAsync(msg.NodeId, msg.Name);
                break;
            case OrgNodeLevel.Cellule:
                await UpsertServiceAsync(msg.NodeId, msg.Name, msg.ParentNodeId);
                break;
            case OrgNodeLevel.Service:
                await UpsertSubServiceAsync(msg.NodeId, msg.Name, msg.ParentNodeId);
                break;
        }
    }

    public async Task Consume(ConsumeContext<DirectoryAssignmentChangedMessage> context)
    {
        var msg = context.Message;
        if (msg.Removed)
        {
            await HandleRemovalAsync(msg, context.CancellationToken);
            return;
        }

        if (msg.EmployeeId == Guid.Empty)
            return;

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Guid == msg.EmployeeId, context.CancellationToken);
        if (user is null && !string.IsNullOrWhiteSpace(msg.EmployeeEmail))
        {
            var email = msg.EmployeeEmail.Trim().ToLowerInvariant();
            user = await db.Users.FirstOrDefaultAsync(
                u => u.Email.ToLower() == email, context.CancellationToken);
        }

        if (user is null)
        {
            logger.LogWarning("Directory assignment: user {Id} not found in Planning", msg.EmployeeId);
            return;
        }

        var roleName = !string.IsNullOrWhiteSpace(msg.NewRole)
            ? KyntusRoleNames.NormalizePlanningRole(msg.NewRole!)
            : msg.Kind switch
            {
                OrgAssignmentKind.ChefDeProjet => KyntusRoleNames.ChefDeProjet,
                OrgAssignmentKind.Superviseur => KyntusRoleNames.Superviseur,
                OrgAssignmentKind.ReferentTechnique => KyntusRoleNames.ReferentTechnique,
                OrgAssignmentKind.Pilote => KyntusRoleNames.Pilote,
                _ => KyntusRoleNames.Employee,
            };

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, context.CancellationToken);
        if (role is not null)
            user.RoleId = role.Id;

        if (msg.Kind == OrgAssignmentKind.Superviseur && !string.IsNullOrWhiteSpace(msg.NodeId))
        {
            var planningService = await db.Services.FirstOrDefaultAsync(
                s => s.PrimeCelluleId == msg.NodeId.Trim(), context.CancellationToken);
            if (planningService is not null)
            {
                user.SubServiceId = null;
                var alreadyLinked = await db.UserManagedServices.AnyAsync(
                    us => us.UserId == user.Id && us.ServiceId == planningService.Id,
                    context.CancellationToken);
                if (!alreadyLinked)
                {
                    db.UserManagedServices.Add(new UserManagedService
                    {
                        UserId = user.Id,
                        ServiceId = planningService.Id,
                    });
                }
            }
        }
        else if (msg.Kind == OrgAssignmentKind.ReferentTechnique && !string.IsNullOrWhiteSpace(msg.NodeId))
        {
            var sub = await db.SubServices.FirstOrDefaultAsync(
                s => s.PrimeServiceId == msg.NodeId.Trim(), context.CancellationToken);
            if (sub is not null)
            {
                user.SubServiceId = sub.Id;
                var alreadyLinked = await db.UserSubServices.AnyAsync(
                    us => us.UserId == user.Id && us.SubServiceId == sub.Id,
                    context.CancellationToken);
                if (!alreadyLinked)
                    db.UserSubServices.Add(new UserSubService { UserId = user.Id, SubServiceId = sub.Id });
            }
        }
        else if (msg.Kind == OrgAssignmentKind.ChefDeProjet)
        {
            user.SubServiceId = null;
        }

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Planning Directory assignment {Kind} applied to user {UserId}", msg.Kind, user.Id);

        // Toujours pousser Auth : corrige aussi les désync JWT (rôle Planning déjà à jour).
        await userService.SyncUserRoleToAuthAsync(user.Id, context.CancellationToken);
    }

    private async Task HandleRemovalAsync(DirectoryAssignmentChangedMessage msg, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Guid == msg.EmployeeId, ct);
        if (user is null)
        {
            logger.LogWarning("Directory assignment removal: user {Id} not found in Planning", msg.EmployeeId);
            return;
        }

        switch (msg.Kind)
        {
            case OrgAssignmentKind.Superviseur when !string.IsNullOrWhiteSpace(msg.NodeId):
            {
                var planningService = await db.Services.FirstOrDefaultAsync(
                    s => s.PrimeCelluleId == msg.NodeId.Trim(), ct);
                if (planningService is not null)
                {
                    var links = await db.UserManagedServices
                        .Where(us => us.UserId == user.Id && us.ServiceId == planningService.Id)
                        .ToListAsync(ct);
                    db.UserManagedServices.RemoveRange(links);
                }
                break;
            }
            case OrgAssignmentKind.ReferentTechnique when !string.IsNullOrWhiteSpace(msg.NodeId):
            {
                var sub = await db.SubServices.FirstOrDefaultAsync(
                    s => s.PrimeServiceId == msg.NodeId.Trim(), ct);
                if (sub is not null)
                {
                    var links = await db.UserSubServices
                        .Where(us => us.UserId == user.Id && us.SubServiceId == sub.Id)
                        .ToListAsync(ct);
                    db.UserSubServices.RemoveRange(links);
                    if (user.SubServiceId == sub.Id)
                    {
                        var remaining = await db.UserSubServices
                            .Where(us => us.UserId == user.Id)
                            .OrderBy(us => us.SubServiceId)
                            .Select(us => (int?)us.SubServiceId)
                            .FirstOrDefaultAsync(ct);
                        user.SubServiceId = remaining;
                    }
                }
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Planning Directory assignment removal {Kind} applied for user {UserId}", msg.Kind, user.Id);
    }

    private async Task UpsertFloorAsync(string primePoleId, string name)
    {
        var floor = await db.Floors.FirstOrDefaultAsync(f => f.PrimePoleId == primePoleId);
        if (floor is null)
        {
            db.Floors.Add(new Floor
            {
                Name = name,
                FloorNumber = await db.Floors.CountAsync() + 1,
                PrimePoleId = primePoleId,
            });
        }
        else
        {
            floor.Name = name;
            floor.IsArchived = false;
        }

        await db.SaveChangesAsync();
    }

    private async Task UpsertServiceAsync(string primeCelluleId, string name, string? parentPoleId)
    {
        if (string.IsNullOrWhiteSpace(parentPoleId)) return;

        var floor = await db.Floors.FirstOrDefaultAsync(f => f.PrimePoleId == parentPoleId);
        if (floor is null)
        {
            floor = new Floor
            {
                Name = $"Pôle {parentPoleId}",
                FloorNumber = await db.Floors.CountAsync() + 1,
                PrimePoleId = parentPoleId,
            };
            db.Floors.Add(floor);
            await db.SaveChangesAsync();
        }

        var service = await db.Services.FirstOrDefaultAsync(s => s.PrimeCelluleId == primeCelluleId);
        if (service is null)
        {
            db.Services.Add(new Service
            {
                FloorId = floor.Id,
                Name = name,
                Code = PlanningOrgMirrorCodes.ForCellule(primeCelluleId),
                PrimeCelluleId = primeCelluleId,
            });
        }
        else
        {
            service.Name = name;
            service.FloorId = floor.Id;
            service.IsArchived = false;
        }

        await db.SaveChangesAsync();
    }

    private async Task UpsertSubServiceAsync(string primeServiceId, string name, string? parentCelluleId)
    {
        if (string.IsNullOrWhiteSpace(parentCelluleId)) return;

        var service = await db.Services.FirstOrDefaultAsync(s => s.PrimeCelluleId == parentCelluleId);
        if (service is null)
            throw new InvalidOperationException($"Parent cellule {parentCelluleId} not mirrored yet");

        var sub = await db.SubServices.FirstOrDefaultAsync(s => s.PrimeServiceId == primeServiceId);
        if (sub is null)
        {
            db.SubServices.Add(new SubService
            {
                ServiceId = service.Id,
                Name = name,
                Code = PlanningOrgMirrorCodes.ForLeafService(primeServiceId),
                PrimeServiceId = primeServiceId,
            });
        }
        else
        {
            sub.Name = name;
            sub.ServiceId = service.Id;
            sub.IsArchived = false;
        }

        await db.SaveChangesAsync();
    }
}
