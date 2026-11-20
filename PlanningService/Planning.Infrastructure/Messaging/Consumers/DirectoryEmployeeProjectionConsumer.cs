using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Infrastructure.Persistence;
using Planning.Domain.Entities;
using Planning.Infrastructure.Services;

namespace Planning.Infrastructure.Messaging.Consumers;

public sealed class DirectoryEmployeeProjectionConsumer(
    AppDbContext db,
    IUserService userService,
    ILogger<DirectoryEmployeeProjectionConsumer> logger,
    IDirectoryEmployeeWriteClient? directoryWrite = null) :
    IConsumer<DirectoryEmployeeChangedMessage>
{
    public async Task Consume(ConsumeContext<DirectoryEmployeeChangedMessage> context)
    {
        var msg = context.Message;
        if (msg.IsDeleted)
        {
            var del = await db.Users.FirstOrDefaultAsync(u => u.Guid == msg.EmployeeId, context.CancellationToken);
            if (del is not null) del.IsActive = false;
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Guid == msg.EmployeeId, context.CancellationToken);

        if (user is null)
        {
            user = await db.Users.FirstOrDefaultAsync(
                u => u.Email.ToLower() == msg.Email.Trim().ToLower(),
                context.CancellationToken);
        }

        var role = await db.Roles.FirstOrDefaultAsync(
            r => r.Name == KyntusRoleNames.NormalizePlanningRole(msg.Role),
            context.CancellationToken);
        if (role is null)
            role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Employee", context.CancellationToken);

        var roleChanged = false;
        var isNew = false;
        var reparerAnnuaire = false;

        if (user is null)
        {
            if (role is null) return;
            user = new User
            {
                Guid = msg.EmployeeId,
                Email = msg.Email.Trim(),
                FirstName = msg.FirstName.Trim(),
                LastName = msg.LastName.Trim(),
                RoleId = role.Id,
                IsActive = msg.IsActive,
                HireDate = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Planning.Infrastructure.Security.PasswordGenerator.Generate()),
                IdTechnicien = msg.IdTechnicien,
                HtelCode = msg.HtelCode,
                HtelTechnicienName = string.IsNullOrWhiteSpace(msg.HtelTechnicienName)
                    ? null
                    : msg.HtelTechnicienName.Trim(),
            };
            if (!string.IsNullOrWhiteSpace(msg.ServiceId))
            {
                // v51 : service de référence (déterministe même en cas de doublon dans le miroir).
                var subId = await SubServiceCanonique.ResoudreAsync(db, msg.ServiceId, context.CancellationToken);
                if (subId is int sid)
                    user.SubServiceId = sid;
            }
            else
            {
                // Directory sans service feuille (retrait pilote, clear placement, Support…)
                // → ne pas conserver un SubServiceId Planning obsolète.
                user.SubServiceId = null;
            }
            db.Users.Add(user);
            isNew = true;
            roleChanged = true;
        }
        else
        {
            user.Guid = msg.EmployeeId;
            user.Email = msg.Email.Trim();
            user.FirstName = msg.FirstName.Trim();
            user.LastName = msg.LastName.Trim();
            user.IsActive = msg.IsActive;
            user.IdTechnicien = msg.IdTechnicien;
            user.HtelCode = msg.HtelCode;
            user.HtelTechnicienName = string.IsNullOrWhiteSpace(msg.HtelTechnicienName)
                ? null
                : msg.HtelTechnicienName.Trim();
            // Promotions structurelles toujours appliquées ; ne pas écraser un rôle structure
            // par un message non structurel (évite les régressions Employee/Pilote).
            if (role is not null && (IsStructureRole(role.Name) || !IsStructureRole(user.Role?.Name)))
            {
                if (user.RoleId != role.Id)
                {
                    user.RoleId = role.Id;
                    roleChanged = true;
                }
            }
            if (!string.IsNullOrWhiteSpace(msg.ServiceId))
            {
                // v51 : service de référence (déterministe même en cas de doublon dans le miroir).
                var subId = await SubServiceCanonique.ResoudreAsync(db, msg.ServiceId, context.CancellationToken);
                if (subId is int sid)
                {
                    // v63 : jamais de retour automatique dans un service quitté par réaffectation ;
                    // l'annuaire, resté sur l'ancienne affectation, est réaligné.
                    if (await AffectationDeReference.RetourInterditAsync(db, user.Id, user.SubServiceId, sid, context.CancellationToken))
                    {
                        reparerAnnuaire = true;
                        logger.LogWarning(
                            "Annuaire : {Email} renvoyé vers son ancien service {Ancien} — affectation de référence {Actuel} conservée.",
                            user.Email, sid, user.SubServiceId);
                    }
                    else
                    {
                        user.SubServiceId = sid;
                    }
                }
            }
            else
            {
                user.SubServiceId = null;
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);

        await SyncManagerIdsAsync(user, msg.ChefDeProjetId, msg.SuperviseurId, msg.ReferentTechniqueId, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);

        if (reparerAnnuaire && directoryWrite is not null)
        {
            await db.Entry(user).Reference(u => u.Role).LoadAsync(context.CancellationToken);
            await directoryWrite.TryUpdateEmployeeAsync(user, context.CancellationToken);
        }

        if (roleChanged || isNew)
        {
            await userService.SyncUserRoleToAuthAsync(user.Id, context.CancellationToken);
            logger.LogInformation(
                "Directory employee {Email} projeté → Auth sync (new={IsNew}, roleChanged={RoleChanged})",
                user.Email,
                isNew,
                roleChanged);
        }
    }

    private async Task SyncManagerIdsAsync(
        User user,
        Guid? chefDeProjetId,
        Guid? superviseurId,
        Guid? referentTechniqueId,
        CancellationToken ct)
    {
        if (!chefDeProjetId.HasValue && !superviseurId.HasValue && !referentTechniqueId.HasValue)
            return;

        var profile = await db.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null)
        {
            profile = new UserHrProfile { UserId = user.Id };
            db.UserHrProfiles.Add(profile);
        }

        if (chefDeProjetId.HasValue) profile.ChefDeProjetId = chefDeProjetId;
        if (superviseurId.HasValue) profile.SuperviseurId = superviseurId;
        if (referentTechniqueId.HasValue) profile.ReferentTechniqueId = referentTechniqueId;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsStructureRole(string? role) =>
        KyntusRoleNames.IsChefDeProjet(role)
        || KyntusRoleNames.IsSuperviseur(role)
        || KyntusRoleNames.IsReferentTechnique(role);
}
