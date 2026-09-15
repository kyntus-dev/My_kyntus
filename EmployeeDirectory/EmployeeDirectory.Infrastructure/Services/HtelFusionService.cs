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
    ILogger<HtelFusionService> logger,
    IDirectoryCacheInvalidator? cacheInvalidator = null) : IHtelFusionService
{
    public async Task<IReadOnlyList<HtelTechnicienDto>> ListTechniciensAsync(bool? actifOnly = null, CancellationToken ct = default)
    {
        var all = await ReadSnapshotAsync(ct);
        return actifOnly == true ? all.Where(t => t.Actif == 1).ToList() : all;
    }

    public async Task<HtelLiaisonsReportDto> GetLiaisonsAsync(CancellationToken ct = default)
    {
        var techniciens = await ReadSnapshotAsync(ct);
        // Un compte désactivé conserve son identité HTEL et doit empêcher un doublon.
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(ct);
        return HtelReconciliation.BuildReport(techniciens, employees);
    }

    public async Task<HtelSyncReportDto> SyncAsync(CancellationToken ct = default)
    {
        var techniciens = await ReadSnapshotAsync(ct);
        var byId = techniciens.ToDictionary(t => t.IdTechnicien);
        var employees = await db.Employees.ToListAsync(ct);
        var changedIds = new List<Guid>();
        foreach (var employee in employees)
        {
            if (employee.IdTechnicien is not int id || !byId.TryGetValue(id, out var tech)) continue;
            if (!ApplyMirror(employee, tech)) continue;
            await EnqueueEmployeeChangedAsync(employee, ct);
            changedIds.Add(employee.Id);
        }
        await SaveAsync(changedIds, ct);
        var report = HtelReconciliation.BuildReport(techniciens, employees);
        logger.LogInformation("HTEL: {Fetched} techniciens, {Updated} miroirs actualisés. Statuts et identités RH conservés.",
            techniciens.Count, changedIds.Count);
        return new HtelSyncReportDto(techniciens.Count, changedIds.Count, 0,
            report.OrphansHtel.Count, report.Ambiguous.Count, report.UnlinkedEmployees.Count);
    }

    public async Task<bool> LinkAsync(Guid employeeId, int idTechnicien, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;
        // Une liaison différente doit être déliée explicitement depuis sa fiche.
        if (employee.IdTechnicien.HasValue && employee.IdTechnicien != idTechnicien)
            throw new InvalidOperationException("Cet employé est déjà lié à un autre identifiant HTEL. Vérifiez sa liaison avant de la remplacer.");
        var tech = await ResolveAvailableAsync(employee, idTechnicien, ct);
        if (ApplyMirror(employee, tech))
        {
            await EnqueueEmployeeChangedAsync(employee, ct);
            await SaveAsync([employee.Id], ct);
        }
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
        await SaveAsync([employee.Id], ct);
        return true;
    }

    public async Task<bool> ReviewIdentityAsync(HtelIdentityReviewRequest request, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null) return false;
        if (employee.IdTechnicien != request.IdTechnicien ||
            employee.FirstName != request.ExpectedFirstName || employee.LastName != request.ExpectedLastName)
            throw new InvalidOperationException("La fiche a changé. Actualisez la comparaison avant de confirmer.");
        var tech = (await ReadSnapshotAsync(ct)).SingleOrDefault(t => t.IdTechnicien == request.IdTechnicien)
            ?? throw new InvalidOperationException("Ce technicien est désormais absent de HTEL. Actualisez la comparaison.");
        if (tech.Technicien != request.ExpectedTechnicien)
            throw new InvalidOperationException("Le nom HTEL a changé. Actualisez la comparaison avant de confirmer.");
        var first = request.FirstName?.Trim() ?? "";
        var last = request.LastName?.Trim() ?? "";
        if (first.Length is < 1 or > 128 || last.Length is < 1 or > 128)
            throw new ArgumentException("Nom et prénom sont obligatoires (128 caractères maximum chacun).");
        if (HtelNameNormalizer.Normalize($"{last} {first}") != HtelNameNormalizer.Normalize(tech.Technicien))
            throw new ArgumentException("La combinaison Nom + Prénom doit correspondre au nom complet HTEL affiché.");
        var changed = ApplyMirror(employee, tech);
        if (employee.FirstName != first || employee.LastName != last)
        {
            employee.FirstName = first;
            employee.LastName = last;
            employee.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }
        if (changed)
        {
            employee.RowVersion = Guid.NewGuid().ToByteArray();
            await EnqueueEmployeeChangedAsync(employee, ct);
            await SaveAsync([employee.Id], ct);
            logger.LogInformation("Identité HTEL confirmée pour l'employé {EmployeeId}, technicien {TechnicienId}", employee.Id, tech.IdTechnicien);
        }
        return true;
    }

    public async Task ApplyLinkOnEmployeeAsync(Employee employee, int? explicitIdTechnicien, CancellationToken ct = default)
    {
        if (explicitIdTechnicien.HasValue)
        {
            if (explicitIdTechnicien <= 0) throw new InvalidOperationException("Identifiant HTEL invalide.");
            if (employee.IdTechnicien.HasValue && employee.IdTechnicien != explicitIdTechnicien)
                throw new InvalidOperationException("La fiche est déjà liée à un autre technicien HTEL.");
            // Une sélection explicite doit être vérifiée avant toute création de compte.
            var tech = await ResolveAvailableAsync(employee, explicitIdTechnicien.Value, ct);
            if (tech.Actif != 1 && !employee.IdTechnicien.HasValue)
                throw new InvalidOperationException("Ce technicien est inactif dans HTEL. Vérifiez sa situation avant de créer le compte.");
            if (!employee.IdTechnicien.HasValue && HtelNameNormalizer.Normalize($"{employee.LastName} {employee.FirstName}")
                    != HtelNameNormalizer.Normalize(tech.Technicien))
                throw new InvalidOperationException("Le nom et le prénom saisis doivent correspondre au nom complet HTEL sélectionné.");
            ApplyMirror(employee, tech);
            return;
        }
        if (employee.IdTechnicien is not int existingId) return;
        try
        {
            var tech = (await ReadSnapshotAsync(ct)).SingleOrDefault(t => t.IdTechnicien == existingId);
            if (tech is not null) ApplyMirror(employee, tech);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTEL indisponible : miroir conservé pour {EmployeeId}", employee.Id);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Timeout HTEL : miroir conservé pour {EmployeeId}", employee.Id);
        }
    }

    private async Task<HtelTechnicienDto> ResolveAvailableAsync(Employee employee, int idTechnicien, CancellationToken ct)
    {
        var tech = (await ReadSnapshotAsync(ct)).SingleOrDefault(t => t.IdTechnicien == idTechnicien)
            ?? throw new InvalidOperationException($"Technicien HTEL {idTechnicien} introuvable.");
        if (await db.Employees.AnyAsync(e => e.IdTechnicien == idTechnicien && e.Id != employee.Id, ct))
            throw new InvalidOperationException($"Le technicien HTEL {idTechnicien} est déjà lié à une fiche MyKyntus (active ou désactivée).");
        return tech;
    }

    private async Task<IReadOnlyList<HtelTechnicienDto>> ReadSnapshotAsync(CancellationToken ct)
    {
        var rows = await htelClient.GetTechniciensAsync(ct);
        HtelReconciliation.ValidateSnapshot(rows);
        return rows;
    }

    private static bool ApplyMirror(Employee employee, HtelTechnicienDto tech)
    {
        var name = tech.Technicien.Trim();
        // Un code absent en amont n'efface pas une information déjà renseignée.
        var code = string.IsNullOrWhiteSpace(tech.Code) ? employee.HtelCode : tech.Code.Trim();
        if (employee.IdTechnicien == tech.IdTechnicien && employee.HtelCode == code && employee.HtelTechnicienName == name)
            return false;
        employee.IdTechnicien = tech.IdTechnicien;
        employee.HtelCode = code;
        employee.HtelTechnicienName = name;
        employee.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private async Task SaveAsync(IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (cacheInvalidator is null) return;
        foreach (var id in employeeIds)
            await cacheInvalidator.InvalidateEmployeeAsync(id, ct);
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
