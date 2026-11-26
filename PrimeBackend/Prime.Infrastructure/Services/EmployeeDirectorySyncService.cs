using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed record EmployeeDirectoryUpsertRequest(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? PrimeServiceId = null,
    Guid SupervisorId = default,
    bool SkipOrgStructureFields = false,
    Guid? BusinessDepartmentId = null,
    string? BusinessDepartmentKind = null,
    Guid? ChefDeProjetId = null,
    Guid? SuperviseurId = null,
    Guid? ReferentTechniqueId = null);

public interface IEmployeeDirectorySyncService
{
    Task<string> UpsertAsync(EmployeeDirectoryUpsertRequest request, CancellationToken ct = default);
    Task<string> EnsureFromPlanningAsync(EmployeeDirectoryUpsertRequest request, CancellationToken ct = default);
    Task<int> DedupeByEmailAsync(CancellationToken ct = default);
    /// <summary>v73 — Départ ou désactivation signalé par l'annuaire : la fiche est conservée, marquée sortie.</summary>
    Task<bool> MarquerSortiAsync(Guid employeeId, CancellationToken ct = default);
}

public sealed class EmployeeDirectorySyncService(
    PrimeDbContext db,
    ILogger<EmployeeDirectorySyncService> logger) : IEmployeeDirectorySyncService
{
    public Task<string> UpsertAsync(EmployeeDirectoryUpsertRequest request, CancellationToken ct = default) =>
        UpsertCoreAsync(request, matchByEmail: true, forcePlanningId: false, ct);

    public Task<string> EnsureFromPlanningAsync(EmployeeDirectoryUpsertRequest request, CancellationToken ct = default) =>
        UpsertCoreAsync(request, matchByEmail: true, forcePlanningId: true, ct);

    /// <summary>
    /// v73 — Marque l'agent comme sorti sans rien supprimer : la fiche reste pour l'historique des
    /// primes, l'interface l'affiche grisée « Sorti » et ne le compte plus dans l'effectif planifiable.
    /// Son rattachement est conservé tel quel, pour que l'historique reste lisible.
    /// </summary>
    public async Task<bool> MarquerSortiAsync(Guid employeeId, CancellationToken ct = default)
    {
        var id = employeeId.ToString();
        var existing = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existing is null) return false;
        if (!existing.IsActive) return true;

        existing.IsActive = false;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("prime_employee sorti {Email} id={Id}", existing.Email, existing.Id);
        return true;
    }

    public static EmployeeDirectoryUpsertRequest FromEmployeMessage(EmployeCreatedMessage msg) =>
        new(
            EmployeeId: msg.EmployeId,
            FirstName: msg.Prenom,
            LastName: msg.Nom,
            Email: msg.Email,
            Role: msg.Role,
            PrimeServiceId: msg.PrimeServiceId,
            SupervisorId: msg.SupervisorId);

    public static EmployeeDirectoryUpsertRequest FromEmployeMessage(EmployeUpdatedMessage msg) =>
        new(
            EmployeeId: msg.EmployeId,
            FirstName: msg.Prenom,
            LastName: msg.Nom,
            Email: msg.Email,
            Role: msg.Role,
            PrimeServiceId: msg.PrimeServiceId,
            SupervisorId: msg.SupervisorId,
            SkipOrgStructureFields: msg.SkipOrgStructureFields);

    public async Task<int> DedupeByEmailAsync(CancellationToken ct = default)
    {
        var groups = await db.Employees
            .GroupBy(e => e.Email.ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var merged = 0;
        foreach (var emailKey in groups)
        {
            var rows = await db.Employees.Where(e => e.Email.ToLower() == emailKey).OrderBy(e => e.Id).ToListAsync(ct);
            if (rows.Count < 2) continue;

            var canonical = rows.FirstOrDefault(e => Guid.TryParse(e.Id, out _)) ?? rows[0];
            foreach (var dup in rows.Where(r => r.Id != canonical.Id))
            {
                MergeEmployeeFields(canonical, dup);
                db.Employees.Remove(dup);
                merged++;
            }
        }

        if (merged > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Dedupe employés : {Count} doublon(s) fusionné(s)", merged);
        }

        return merged;
    }

    private async Task<string> UpsertCoreAsync(
        EmployeeDirectoryUpsertRequest request,
        bool matchByEmail,
        bool forcePlanningId,
        CancellationToken ct)
    {
        var id = request.EmployeeId.ToString();
        var role = NormalizeRole(request.Role);
        var email = request.Email.Trim();

        var (serviceId, celluleId, poleId) = await ResolveOrgIdsAsync(request.PrimeServiceId, ct);
        var parentId = ResolveParentId(request);
        var managerIds = ResolveManagerIds(request);

        EmployeeEntity? existing = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existing is null && matchByEmail && !string.IsNullOrWhiteSpace(email))
            existing = await db.Employees.FirstOrDefaultAsync(e => e.Email.ToLower() == email.ToLower(), ct);

        if (existing is null)
        {
            db.Employees.Add(new EmployeeEntity
            {
                Id = id,
                Email = email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Role = role,
                ServiceId = serviceId,
                CelluleId = celluleId,
                PoleId = poleId ?? "",
                ParentId = parentId,
                ChefDeProjetId = managerIds.ChefDeProjetId,
                SuperviseurId = managerIds.SuperviseurId,
                ReferentTechniqueId = managerIds.ReferentTechniqueId,
                BusinessDepartmentId = request.BusinessDepartmentId?.ToString(),
                BusinessDepartmentKind = request.BusinessDepartmentKind,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("prime_employee créé {Email} id={Id} rôle={Role}", email, id, role);
            return id;
        }

        if (forcePlanningId && !string.Equals(existing.Id, id, StringComparison.Ordinal))
        {
            var merged = new EmployeeEntity
            {
                Id = id,
                Email = email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                // v73 — Même règle que plus bas : le rattachement suit l'annuaire, encadrement compris,
                // et l'ancien n'est conservé que si l'annuaire n'envoie rien.
                Role = role,
                ServiceId = serviceId ?? existing.ServiceId,
                CelluleId = celluleId ?? existing.CelluleId,
                PoleId = poleId ?? existing.PoleId,
                IsActive = true,
                ParentId = parentId ?? existing.ParentId,
                ChefDeProjetId = managerIds.ChefDeProjetId ?? existing.ChefDeProjetId,
                SuperviseurId = managerIds.SuperviseurId ?? existing.SuperviseurId,
                ReferentTechniqueId = managerIds.ReferentTechniqueId ?? existing.ReferentTechniqueId,
                Avatar = existing.Avatar
            };
            db.Employees.Remove(existing);
            db.Employees.Add(merged);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("prime_employee fusionné {Email} id={OldId}→{NewId}", email, existing.Id, id);
            return id;
        }

        existing.Email = email;
        existing.FirstName = request.FirstName;
        existing.LastName = request.LastName;
        // v73 — L'annuaire reçoit à nouveau cet agent : s'il était sorti, il redevient présent.
        existing.IsActive = true;
        // v73 — Le rattachement suit désormais l'annuaire pour tout le monde, encadrement compris.
        // Avant, dès qu'un Superviseur / Référent technique / Chef de projet avait un pôle, une cellule
        // ou un service, la synchro gardait l'ancien : réaffecté dans l'annuaire, il continuait
        // d'apparaître dans son ancien périmètre tant qu'on ne le déplaçait pas aussi à la main.
        // Le « ?? existing » ci-dessous protège toujours contre l'effacement : si l'annuaire n'envoie
        // aucun rattachement, l'actuel est conservé. Les cellules qu'un superviseur *encadre* sont
        // stockées à part (affectations d'organisation) et ne sont pas touchées ici.
        existing.Role = role;
        existing.ServiceId = serviceId ?? existing.ServiceId;
        existing.CelluleId = celluleId ?? existing.CelluleId;
        existing.PoleId = poleId ?? existing.PoleId;
        if (parentId is not null)
            existing.ParentId = parentId;
        ApplyManagerIds(existing, managerIds);
        if (request.BusinessDepartmentId.HasValue)
        {
            existing.BusinessDepartmentId = request.BusinessDepartmentId.Value.ToString();
            existing.BusinessDepartmentKind = request.BusinessDepartmentKind;
            if (string.Equals(request.BusinessDepartmentKind, "Support", StringComparison.OrdinalIgnoreCase))
            {
                existing.ServiceId = null;
                existing.CelluleId = null;
                existing.PoleId = "";
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("prime_employee synchronisé {Email} id={Id} rôle={Role}", email, existing.Id, existing.Role);
        return existing.Id;
    }

    /// <summary>
    /// Encadrement déjà rattaché à un pôle, une cellule ou un service. v73 : ne sert plus à geler le
    /// rattachement lors d'une synchro (la réaffectation doit suivre l'annuaire), seulement à départager
    /// deux fiches lors d'une fusion de doublons.
    /// </summary>
    internal static bool HasActiveStructureAssignment(EmployeeEntity e) =>
        (KyntusRoleNames.IsChefDeProjet(e.Role)
         || KyntusRoleNames.IsSuperviseur(e.Role)
         || KyntusRoleNames.IsReferentTechnique(e.Role))
        && (!string.IsNullOrWhiteSpace(e.PoleId)
            || !string.IsNullOrWhiteSpace(e.CelluleId)
            || !string.IsNullOrWhiteSpace(e.ServiceId));

    private static void MergeEmployeeFields(EmployeeEntity target, EmployeeEntity source)
    {
        if (string.IsNullOrWhiteSpace(target.FirstName)) target.FirstName = source.FirstName;
        if (string.IsNullOrWhiteSpace(target.LastName)) target.LastName = source.LastName;
        if (HasActiveStructureAssignment(source) && !HasActiveStructureAssignment(target))
        {
            target.Role = source.Role;
            target.PoleId = source.PoleId;
            target.CelluleId = source.CelluleId;
            target.ServiceId = source.ServiceId;
            target.ParentId = source.ParentId;
            target.ChefDeProjetId = source.ChefDeProjetId;
            target.SuperviseurId = source.SuperviseurId;
            target.ReferentTechniqueId = source.ReferentTechniqueId;
        }
    }

    private static string? ResolveParentId(EmployeeDirectoryUpsertRequest request)
    {
        var supervisorId = request.SuperviseurId ?? (request.SupervisorId != Guid.Empty ? request.SupervisorId : (Guid?)null);
        return supervisorId?.ToString();
    }

    private sealed record ManagerIds(string? ChefDeProjetId, string? SuperviseurId, string? ReferentTechniqueId);

    private static ManagerIds ResolveManagerIds(EmployeeDirectoryUpsertRequest request) =>
        new(
            request.ChefDeProjetId?.ToString(),
            request.SuperviseurId?.ToString(),
            request.ReferentTechniqueId?.ToString());

    private static void ApplyManagerIds(EmployeeEntity target, ManagerIds ids)
    {
        if (ids.ChefDeProjetId is not null)
            target.ChefDeProjetId = ids.ChefDeProjetId;
        if (ids.SuperviseurId is not null)
            target.SuperviseurId = ids.SuperviseurId;
        if (ids.ReferentTechniqueId is not null)
            target.ReferentTechniqueId = ids.ReferentTechniqueId;
    }

    private async Task<(string? ServiceId, string? CelluleId, string? PoleId)> ResolveOrgIdsAsync(
        string? primeServiceId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(primeServiceId))
            return (null, null, null);

        var svc = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == primeServiceId, ct);
        if (svc is null)
            return (primeServiceId, null, null);

        var celluleId = svc.CelluleId;
        var cell = await db.Cellules.AsNoTracking().FirstOrDefaultAsync(c => c.Id == celluleId, ct);
        return (primeServiceId, celluleId, cell?.PoleId);
    }

    private static string NormalizeRole(string role)
    {
        var normalized = KyntusRoleNames.NormalizePlanningRole(role);
        if (string.Equals(normalized, KyntusRoleNames.Manager, StringComparison.OrdinalIgnoreCase))
            return KyntusRoleNames.Superviseur;
        return normalized;
    }
}
