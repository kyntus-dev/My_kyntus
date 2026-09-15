using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Org;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Messaging;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using DomainNodeLevel = EmployeeDirectory.Domain.Enums.OrgNodeLevel;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class DirectoryWriteService(
    DirectoryDbContext db,
    IOutboxWriter outbox,
    DirectoryHierarchyService hierarchy,
    IOrgStructuralRoleExclusivityService exclusivity,
    IPilotRotationTenureService pilotRotation,
    IHtelFusionService htelFusion,
    IDirectoryCacheInvalidator? cacheInvalidator = null) : IDirectoryWriteService
{
    private async Task PersistAndInvalidateAsync(CancellationToken ct, Guid? employeeId = null)
    {
        await db.SaveChangesAsync(ct);
        if (cacheInvalidator is null) return;
        if (employeeId.HasValue)
            await cacheInvalidator.InvalidateEmployeeAsync(employeeId.Value, ct);
        else
            await cacheInvalidator.InvalidateOrgAsync(ct);
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeRequest request, Guid? changedBy, CancellationToken ct = default)
    {
        var id = request.EmployeeId ?? Guid.NewGuid();

        var existingById = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existingById is not null)
            return Map(existingById);

        if (await db.Employees.AnyAsync(e => e.Email.ToLower() == request.Email.Trim().ToLower() && e.IsActive, ct))
            throw new InvalidOperationException($"Email déjà utilisé : {request.Email}");

        var inactiveByEmail = await db.Employees.FirstOrDefaultAsync(
            e => e.Email.ToLower() == request.Email.Trim().ToLower() && !e.IsActive,
            ct);
        if (inactiveByEmail is not null && request.IdTechnicien.HasValue)
            throw new InvalidOperationException("Une fiche désactivée utilise déjà cet email. Vérifiez sa réactivation depuis la fiche existante.");
        if (inactiveByEmail is not null)
            return await ReactivateEmployeeAsync(inactiveByEmail, request, ct);

        var (poleId, celluleId, serviceId) = await ResolveOrgIdsAsync(request.ServiceId, ct);
        var role = KyntusRoleNames.NormalizePlanningRole(request.Role);
        var dept = await ResolveBusinessDepartmentAsync(request.BusinessDepartmentId, ct);

        var employee = new Employee
        {
            Id = id,
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Role = role,
            BusinessDepartmentId = request.BusinessDepartmentId,
            ServiceId = dept?.Kind == BusinessDepartmentKind.Support ? null : serviceId,
            CelluleId = dept?.Kind == BusinessDepartmentKind.Support ? null : celluleId,
            PoleId = dept?.Kind == BusinessDepartmentKind.Support ? null : poleId,
            ParentId = request.ParentId,
            HireDate = request.HireDate ?? DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        DirectoryHrProfileHelper.ApplyManagers(
            employee, request.ChefDeProjetId, request.SuperviseurId, request.ReferentTechniqueId);

        if (employee.ParentId is null)
            employee.ParentId = await hierarchy.ResolveDefaultParentIdAsync(employee, ct);

        await htelFusion.ApplyLinkOnEmployeeAsync(employee, request.IdTechnicien, ct);

        db.Employees.Add(employee);
        await DirectoryHrProfileHelper.UpsertAsync(
            db, outbox, employee.Id, request.HrProfile, employee.HireDate, ct);
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return Map(employee);
    }

    private async Task<EmployeeDto> ReactivateEmployeeAsync(
        Employee employee,
        CreateEmployeeRequest request,
        CancellationToken ct)
    {
        var (poleId, celluleId, serviceId) = await ResolveOrgIdsAsync(request.ServiceId, ct);
        var role = KyntusRoleNames.NormalizePlanningRole(request.Role);
        var dept = await ResolveBusinessDepartmentAsync(request.BusinessDepartmentId, ct);

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Email = request.Email.Trim();
        employee.Role = role;
        employee.BusinessDepartmentId = request.BusinessDepartmentId;
        employee.ServiceId = dept?.Kind == BusinessDepartmentKind.Support ? null : serviceId;
        employee.CelluleId = dept?.Kind == BusinessDepartmentKind.Support ? null : celluleId;
        employee.PoleId = dept?.Kind == BusinessDepartmentKind.Support ? null : poleId;
        employee.ParentId = request.ParentId;
        employee.HireDate = request.HireDate ?? employee.HireDate;
        employee.IsActive = true;
        employee.UpdatedAt = DateTime.UtcNow;

        DirectoryHrProfileHelper.ApplyManagers(
            employee, request.ChefDeProjetId, request.SuperviseurId, request.ReferentTechniqueId);

        if (employee.ParentId is null)
            employee.ParentId = await hierarchy.ResolveDefaultParentIdAsync(employee, ct);

        await htelFusion.ApplyLinkOnEmployeeAsync(employee, request.IdTechnicien, ct);

        await DirectoryHrProfileHelper.UpsertAsync(
            db, outbox, employee.Id, request.HrProfile, employee.HireDate, ct);
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return Map(employee);
    }

    public async Task<EmployeeDto?> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, Guid? changedBy, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return null;

        var (poleId, celluleId, serviceId) = await ResolveOrgIdsAsync(request.ServiceId, ct);
        var dept = await ResolveBusinessDepartmentAsync(request.BusinessDepartmentId ?? employee.BusinessDepartmentId, ct);
        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Email = request.Email.Trim();
        employee.Role = KyntusRoleNames.NormalizePlanningRole(request.Role);
        employee.BusinessDepartmentId = request.BusinessDepartmentId ?? employee.BusinessDepartmentId;
        if (dept?.Kind == BusinessDepartmentKind.Support)
        {
            employee.ServiceId = null;
            employee.CelluleId = null;
            employee.PoleId = null;
        }
        else
        {
            employee.ServiceId = serviceId ?? employee.ServiceId;
            employee.CelluleId = celluleId ?? employee.CelluleId;
            employee.PoleId = poleId ?? employee.PoleId;
        }
        employee.IsActive = request.IsActive;
        employee.ParentId = request.ParentId ?? employee.ParentId;
        employee.HireDate = request.HireDate ?? employee.HireDate;
        employee.UpdatedAt = DateTime.UtcNow;

        DirectoryHrProfileHelper.ApplyManagers(
            employee, request.ChefDeProjetId, request.SuperviseurId, request.ReferentTechniqueId);

        if (employee.ParentId is null)
            employee.ParentId = await hierarchy.ResolveDefaultParentIdAsync(employee, ct);

        await htelFusion.ApplyLinkOnEmployeeAsync(employee, request.IdTechnicien, ct);

        await DirectoryHrProfileHelper.UpsertAsync(
            db, outbox, employee.Id, request.HrProfile, employee.HireDate, ct);
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return Map(employee);
    }

    public async Task<EmployeeDto?> ClearOrgPlacementAsync(
        Guid id,
        ClearOrgPlacementRequest request,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return null;

        var level = (request.Level ?? "all").Trim().ToLowerInvariant();
        var nodeId = request.NodeId?.Trim();
        var reason = "Retrait du périmètre organisationnel";

        // Clôture les titulatures actives sur le nœud (et descendants si cellule/pôle).
        var nodeIdsToRevoke = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(nodeId))
            nodeIdsToRevoke.Add(nodeId);

        if (level is "cellule" or "pole" or "all")
        {
            if (level is "cellule" && !string.IsNullOrWhiteSpace(nodeId))
            {
                var serviceIds = await db.OrgServices.AsNoTracking()
                    .Where(s => s.CelluleId == nodeId)
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                foreach (var sid in serviceIds)
                    nodeIdsToRevoke.Add(sid);
            }
            else if (level is "pole" && !string.IsNullOrWhiteSpace(nodeId))
            {
                var celluleIds = await db.OrgCellules.AsNoTracking()
                    .Where(c => c.PoleId == nodeId)
                    .Select(c => c.Id)
                    .ToListAsync(ct);
                foreach (var cid in celluleIds)
                    nodeIdsToRevoke.Add(cid);
                var serviceIds = await db.OrgServices.AsNoTracking()
                    .Where(s => celluleIds.Contains(s.CelluleId))
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                foreach (var sid in serviceIds)
                    nodeIdsToRevoke.Add(sid);
            }
            else if (level == "all")
            {
                var activeNodes = await db.OrgAssignments.AsNoTracking()
                    .Where(a => a.EmployeeId == id && a.EffectiveTo == null)
                    .Select(a => a.NodeId)
                    .ToListAsync(ct);
                foreach (var n in activeNodes)
                    nodeIdsToRevoke.Add(n);
            }
        }

        foreach (var nid in nodeIdsToRevoke)
        {
            var kinds = await db.OrgAssignments
                .Where(a => a.EmployeeId == id && a.NodeId == nid && a.EffectiveTo == null)
                .Select(a => a.Kind)
                .Distinct()
                .ToListAsync(ct);
            foreach (var kind in kinds)
            {
                await RemoveStructureAssignmentAsync(kind.ToString(), nid, id, changedBy, reason, ct);
            }

            // Pilote projeté sans OrgAssignment
            if (employee.Role != null
                && string.Equals(employee.Role, KyntusRoleNames.Pilote, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(employee.ServiceId, nid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(employee.CelluleId, nid, StringComparison.OrdinalIgnoreCase)))
            {
                await RemoveStructurePilotAsync(nid, id, changedBy, reason, ct);
            }
        }

        // Recharger après éventuelles mutations d'affectation
        employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return null;

        switch (level)
        {
            case "service":
            {
                string? parentCelluleId = employee.CelluleId;
                string? parentPoleId = employee.PoleId;
                if (!string.IsNullOrWhiteSpace(nodeId))
                {
                    var svc = await db.OrgServices.AsNoTracking()
                        .Where(s => s.Id == nodeId)
                        .Select(s => new { s.CelluleId, PoleId = s.Cellule.PoleId })
                        .FirstOrDefaultAsync(ct);
                    if (svc is not null)
                    {
                        parentCelluleId = svc.CelluleId;
                        parentPoleId = svc.PoleId;
                    }
                }

                if (string.IsNullOrWhiteSpace(nodeId)
                    || string.Equals(employee.ServiceId, nodeId, StringComparison.OrdinalIgnoreCase)
                    || nodeIdsToRevoke.Contains(employee.ServiceId ?? ""))
                {
                    employee.ServiceId = null;
                    employee.CelluleId = parentCelluleId;
                    employee.PoleId = parentPoleId;
                }
                break;
            }
            case "cellule":
                if (string.IsNullOrWhiteSpace(nodeId)
                    || string.Equals(employee.CelluleId, nodeId, StringComparison.OrdinalIgnoreCase)
                    || await EmployeeServiceUnderCelluleAsync(employee.ServiceId, nodeId, ct))
                {
                    string? parentPoleId = employee.PoleId;
                    if (!string.IsNullOrWhiteSpace(nodeId))
                    {
                        parentPoleId = await db.OrgCellules.AsNoTracking()
                            .Where(c => c.Id == nodeId)
                            .Select(c => c.PoleId)
                            .FirstOrDefaultAsync(ct) ?? parentPoleId;
                    }
                    employee.ServiceId = null;
                    employee.CelluleId = null;
                    employee.PoleId = parentPoleId;
                }
                break;
            case "pole":
                if (string.IsNullOrWhiteSpace(nodeId)
                    || string.Equals(employee.PoleId, nodeId, StringComparison.OrdinalIgnoreCase)
                    || await EmployeeUnderPoleAsync(employee, nodeId, ct))
                {
                    employee.ServiceId = null;
                    employee.CelluleId = null;
                    employee.PoleId = null;
                }
                break;
            default: // all
                employee.ServiceId = null;
                employee.CelluleId = null;
                employee.PoleId = null;
                employee.BusinessDepartmentId = null;
                break;
        }

        // Si plus aucune titulature structurelle, retomber sur Employé
        var stillAssigned = await db.OrgAssignments.AnyAsync(
            a => a.EmployeeId == id && a.EffectiveTo == null, ct);
        if (!stillAssigned
            && (KyntusRoleNames.IsPilote(employee.Role)
                || KyntusRoleNames.IsReferentTechnique(employee.Role)
                || KyntusRoleNames.IsSuperviseur(employee.Role)
                || KyntusRoleNames.IsChefDeProjet(employee.Role)))
        {
            employee.Role = KyntusRoleNames.Employee;
            employee.ParentId = null;
        }

        employee.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return Map(employee);
    }

    private async Task<bool> EmployeeServiceUnderCelluleAsync(string? serviceId, string? celluleId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(celluleId))
            return false;
        return await db.OrgServices.AsNoTracking()
            .AnyAsync(s => s.Id == serviceId && s.CelluleId == celluleId, ct);
    }

    private async Task<bool> EmployeeUnderPoleAsync(Employee employee, string? poleId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(poleId)) return false;
        if (string.Equals(employee.PoleId, poleId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(employee.CelluleId))
        {
            var match = await db.OrgCellules.AsNoTracking()
                .AnyAsync(c => c.Id == employee.CelluleId && c.PoleId == poleId, ct);
            if (match) return true;
        }
        if (!string.IsNullOrWhiteSpace(employee.ServiceId))
        {
            return await db.OrgServices.AsNoTracking()
                .AnyAsync(s => s.Id == employee.ServiceId && s.Cellule!.PoleId == poleId, ct);
        }
        return false;
    }

    public async Task<bool> DeleteEmployeeAsync(Guid id, Guid? changedBy, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return false;
        employee.IsActive = false;
        employee.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(employee, isDeleted: true, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<StructuralRoleAssignmentResult> AssignStructureRoleAsync(
        string kind,
        string nodeId,
        Guid employeeId,
        Guid? changedBy,
        string? reason,
        IReadOnlyList<Guid>? revokeEmployeeIds = null,
        bool forceTenureOverride = false,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<DomainAssignmentKind>(kind, true, out var assignmentKind))
            throw new ArgumentException($"Kind invalide : {kind}");

        var trimmedNodeId = nodeId.Trim();
        string? previousServiceId = null;
        if (assignmentKind == DomainAssignmentKind.Pilote)
        {
            var eligibility = await pilotRotation.GetEligibilityAsync(employeeId, trimmedNodeId, ct);
            if (!eligibility.IsSameService && !string.IsNullOrWhiteSpace(eligibility.CurrentServiceId))
                previousServiceId = eligibility.CurrentServiceId;

            await pilotRotation.ValidateRotationAsync(
                employeeId, trimmedNodeId, forceTenureOverride, reason, ct);
        }

        var assignmentReason = reason;
        if (assignmentKind == DomainAssignmentKind.Pilote && forceTenureOverride && !string.IsNullOrWhiteSpace(reason))
            assignmentReason = PilotRotationTenureService.FormatOverrideReason(reason);
        var nodeLevel = assignmentKind switch
        {
            DomainAssignmentKind.ChefDeProjet => DomainNodeLevel.Pole,
            DomainAssignmentKind.Superviseur => DomainNodeLevel.Cellule,
            DomainAssignmentKind.ReferentTechnique => DomainNodeLevel.Service,
            DomainAssignmentKind.Pilote => DomainNodeLevel.Service,
            _ => DomainNodeLevel.Service,
        };

        // Éviction explicite uniquement (multi-responsables autorisés sur le même nœud).
        var revokedOnNode = new List<NodeIncumbentRevokedDto>();
        if (revokeEmployeeIds is { Count: > 0 })
        {
            foreach (var revokeId in revokeEmployeeIds.Where(id => id != Guid.Empty && id != employeeId).Distinct())
            {
                var removed = await RevokeNodeIncumbentAsync(
                    assignmentKind, trimmedNodeId, nodeLevel, revokeId, changedBy,
                    reason ?? "Révocation explicite avant nouvelle affectation", ct);
                if (!removed) continue;
                await ResetDisplacedEmployeeAsync(revokeId, changedBy, reason, ct);
                revokedOnNode.Add(new NodeIncumbentRevokedDto(
                    revokeId.ToString(), assignmentKind.ToString(), trimmedNodeId));
            }
        }

        var alreadyOnNode = await db.OrgAssignments.AnyAsync(
            a => a.Kind == assignmentKind
                 && a.NodeId == trimmedNodeId
                 && a.EmployeeId == employeeId
                 && a.EffectiveTo == null,
            ct);
        if (alreadyOnNode)
        {
            // Idempotence : déjà titulaire actif → no-op.
            return new StructuralRoleAssignmentResult([], revokedOnNode, employeeId.ToString());
        }

        // Exclusivité inter-kinds uniquement : un chef/superviseur/RT peut cumuler plusieurs nœuds.
        // Pilote reste mono-charge (géré dans RevokeConflicting).
        var revoked = (await exclusivity.RevokeConflictingStructuralRolesForEmployeeAsync(
            employeeId, assignmentKind, changedBy, reason ?? "Nouvelle affectation structurelle", ct)).ToList();

        var assignment = new OrgAssignment
        {
            Id = Guid.NewGuid(),
            Kind = assignmentKind,
            NodeId = trimmedNodeId,
            NodeLevel = nodeLevel,
            EmployeeId = employeeId,
            EffectiveFrom = DateTime.UtcNow,
            ChangedBy = changedBy,
            ChangeReason = assignmentReason,
        };
        db.OrgAssignments.Add(assignment);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new KeyNotFoundException("Employé introuvable.");
        await hierarchy.ApplyAssignmentToEmployeeAsync(employee, assignmentKind, trimmedNodeId, ct);
        employee.UpdatedAt = DateTime.UtcNow;

        await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
        {
            Kind = MessagingEnumMapper.ToMessage(assignmentKind),
            NodeId = trimmedNodeId,
            NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
            EmployeeId = employeeId,
            EmployeeEmail = employee.Email,
            NewRole = employee.Role,
            Removed = false,
        }, aggregateId: employeeId.ToString(), ct: ct);

        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await EnqueueResponsiblesChangedAsync(assignmentKind, trimmedNodeId, nodeLevel, ct);
        await PersistAndInvalidateAsync(ct);

        if (assignmentKind == DomainAssignmentKind.Pilote
            && !string.IsNullOrWhiteSpace(previousServiceId)
            && !string.Equals(previousServiceId, trimmedNodeId, StringComparison.OrdinalIgnoreCase))
        {
            await pilotRotation.ApplyRotationHrProfileAsync(employeeId, previousServiceId, ct);
        }

        return new StructuralRoleAssignmentResult(revoked, revokedOnNode, employeeId.ToString());
    }

    public async Task<StructuralAssignmentsReconcileResult> ReconcileEmployeeStructuralAssignmentsAsync(
        string kind,
        Guid employeeId,
        IReadOnlyList<string> nodeIds,
        string primaryNodeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<DomainAssignmentKind>(kind, true, out var assignmentKind))
            throw new ArgumentException($"Kind invalide : {kind}");

        if (assignmentKind is not (
            DomainAssignmentKind.ChefDeProjet
            or DomainAssignmentKind.Superviseur
            or DomainAssignmentKind.ReferentTechnique))
        {
            throw new ArgumentException(
                "La synchronisation multi-nœuds est réservée à ChefDeProjet, Superviseur et ReferentTechnique.");
        }

        var desired = nodeIds
            .Select(n => n?.Trim() ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (desired.Count == 0)
            throw new ArgumentException("Au moins un nœud est requis.");

        var primary = (primaryNodeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(primary))
            throw new ArgumentException("primaryNodeId est requis.");
        if (!desired.Contains(primary, StringComparer.Ordinal))
            throw new ArgumentException("primaryNodeId doit faire partie de nodeIds.");

        var nodeLevel = assignmentKind switch
        {
            DomainAssignmentKind.ChefDeProjet => DomainNodeLevel.Pole,
            DomainAssignmentKind.Superviseur => DomainNodeLevel.Cellule,
            DomainAssignmentKind.ReferentTechnique => DomainNodeLevel.Service,
            _ => DomainNodeLevel.Service,
        };

        await EnsureNodesExistAsync(assignmentKind, desired, ct);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new KeyNotFoundException("Employé introuvable.");

        var activeSameKind = await db.OrgAssignments
            .Where(a => a.EmployeeId == employeeId
                        && a.Kind == assignmentKind
                        && a.EffectiveTo == null)
            .ToListAsync(ct);

        var currentIds = activeSameKind
            .Select(a => a.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        var toRemove = activeSameKind
            .Where(a => !desired.Contains(a.NodeId, StringComparer.Ordinal))
            .ToList();
        var toAdd = desired
            .Where(id => !currentIds.Contains(id))
            .ToList();

        var revokedOther = (await exclusivity.RevokeConflictingStructuralRolesForEmployeeAsync(
            employeeId,
            assignmentKind,
            changedBy,
            reason ?? "Synchronisation multi-périmètres",
            ct)).ToList();

        var now = DateTime.UtcNow;
        var removedIds = new List<string>();
        foreach (var row in toRemove)
        {
            row.EffectiveTo = now;
            row.SupersededBy = Guid.NewGuid();
            db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
            {
                Id = Guid.NewGuid(),
                Kind = assignmentKind,
                NodeId = row.NodeId,
                NodeLevel = nodeLevel,
                PreviousEmployeeId = employeeId,
                NewEmployeeId = null,
                ChangedBy = changedBy,
                ChangeReason = reason ?? "Retrait lors de synchronisation multi-périmètres",
                ChangedAt = now,
            });

            await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
            {
                Kind = MessagingEnumMapper.ToMessage(assignmentKind),
                NodeId = row.NodeId,
                NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
                EmployeeId = employeeId,
                Removed = true,
            }, aggregateId: employeeId.ToString(), ct: ct);

            await EnqueueResponsiblesChangedAsync(assignmentKind, row.NodeId, nodeLevel, ct);
            removedIds.Add(row.NodeId);
        }

        var addedIds = new List<string>();
        var revokedOnNode = new List<NodeIncumbentRevokedDto>();
        foreach (var nodeId in toAdd)
        {
            // Multi-responsables : pas d'éviction automatique des co-titulaires.
            db.OrgAssignments.Add(new OrgAssignment
            {
                Id = Guid.NewGuid(),
                Kind = assignmentKind,
                NodeId = nodeId,
                NodeLevel = nodeLevel,
                EmployeeId = employeeId,
                EffectiveFrom = now,
                ChangedBy = changedBy,
                ChangeReason = reason ?? "Ajout lors de synchronisation multi-périmètres",
            });

            await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
            {
                Kind = MessagingEnumMapper.ToMessage(assignmentKind),
                NodeId = nodeId,
                NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
                EmployeeId = employeeId,
                EmployeeEmail = employee.Email,
                NewRole = null,
                Removed = false,
            }, aggregateId: employeeId.ToString(), ct: ct);

            await EnqueueResponsiblesChangedAsync(assignmentKind, nodeId, nodeLevel, ct);
            addedIds.Add(nodeId);
        }

        // Ancre primaire appliquée une seule fois après le diff.
        await hierarchy.ApplyAssignmentToEmployeeAsync(employee, assignmentKind, primary, ct);
        employee.UpdatedAt = now;

        // Republier l'événement du primary avec le NewRole final (ancre).
        await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
        {
            Kind = MessagingEnumMapper.ToMessage(assignmentKind),
            NodeId = primary,
            NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
            EmployeeId = employeeId,
            EmployeeEmail = employee.Email,
            NewRole = employee.Role,
            Removed = false,
        }, aggregateId: employeeId.ToString(), ct: ct);

        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);

        return new StructuralAssignmentsReconcileResult(
            assignmentKind.ToString(),
            employeeId.ToString(),
            desired,
            primary,
            addedIds,
            removedIds,
            revokedOther,
            revokedOnNode);
    }

    private async Task EnsureNodesExistAsync(
        DomainAssignmentKind kind,
        IReadOnlyList<string> nodeIds,
        CancellationToken ct)
    {
        switch (kind)
        {
            case DomainAssignmentKind.ChefDeProjet:
            {
                var existing = await db.OrgPoles.AsNoTracking()
                    .Where(p => nodeIds.Contains(p.Id))
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                var missing = nodeIds.Except(existing, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException($"Pôle(s) introuvable(s) : {string.Join(", ", missing)}");
                break;
            }
            case DomainAssignmentKind.Superviseur:
            {
                var existing = await db.OrgCellules.AsNoTracking()
                    .Where(c => nodeIds.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToListAsync(ct);
                var missing = nodeIds.Except(existing, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException($"Cellule(s) introuvable(s) : {string.Join(", ", missing)}");
                break;
            }
            case DomainAssignmentKind.ReferentTechnique:
            {
                var existing = await db.OrgServices.AsNoTracking()
                    .Where(s => nodeIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                var missing = nodeIds.Except(existing, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException($"Service(s) introuvable(s) : {string.Join(", ", missing)}");
                break;
            }
        }
    }

    public async Task<bool> RemoveStructureAssignmentAsync(
        string kind,
        string nodeId,
        Guid employeeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<DomainAssignmentKind>(kind, true, out var assignmentKind))
            throw new ArgumentException($"Kind invalide : {kind}");

        if (assignmentKind == DomainAssignmentKind.Pilote)
            return await RemoveStructurePilotAsync(nodeId, employeeId, changedBy, reason, ct);

        var trimmedNodeId = nodeId.Trim();
        var nodeLevel = assignmentKind switch
        {
            DomainAssignmentKind.ChefDeProjet => DomainNodeLevel.Pole,
            DomainAssignmentKind.Superviseur => DomainNodeLevel.Cellule,
            DomainAssignmentKind.ReferentTechnique => DomainNodeLevel.Service,
            _ => DomainNodeLevel.Service,
        };

        var removed = await RevokeNodeIncumbentAsync(
            assignmentKind, trimmedNodeId, nodeLevel, employeeId, changedBy, reason, ct);
        if (!removed) return false;

        await ResetDisplacedEmployeeAsync(employeeId, changedBy, reason, ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    /// <summary>
    /// Garantit au plus un titulaire actif par (kind, nodeId) en évincant les autres employés.
    /// Ne touche pas aux autres nœuds de l'employé assigné (multi-périmètre autorisé).
    /// </summary>
    private async Task<List<NodeIncumbentRevokedDto>> EvictOtherIncumbentsOnNodeAsync(
        DomainAssignmentKind assignmentKind,
        string nodeId,
        DomainNodeLevel nodeLevel,
        Guid keepEmployeeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct)
    {
        var otherIncumbentIds = await db.OrgAssignments
            .Where(a => a.Kind == assignmentKind
                        && a.NodeId == nodeId
                        && a.EmployeeId != keepEmployeeId
                        && a.EffectiveTo == null)
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

        var revokedOnNode = new List<NodeIncumbentRevokedDto>();
        foreach (var otherId in otherIncumbentIds)
        {
            var removed = await RevokeNodeIncumbentAsync(
                assignmentKind, nodeId, nodeLevel, otherId, changedBy, reason, ct);
            if (!removed) continue;

            await ResetDisplacedEmployeeAsync(otherId, changedBy, reason, ct);
            revokedOnNode.Add(new NodeIncumbentRevokedDto(
                otherId.ToString(),
                assignmentKind.ToString(),
                nodeId));
        }

        return revokedOnNode;
    }

    private async Task<bool> RevokeNodeIncumbentAsync(
        DomainAssignmentKind assignmentKind,
        string nodeId,
        DomainNodeLevel nodeLevel,
        Guid employeeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct)
    {
        var rows = await db.OrgAssignments
            .Where(a => a.Kind == assignmentKind
                && a.NodeId == nodeId
                && a.EmployeeId == employeeId
                && a.EffectiveTo == null)
            .ToListAsync(ct);
        if (rows.Count == 0) return false;

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.EffectiveTo = now;
            db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
            {
                Id = Guid.NewGuid(),
                Kind = assignmentKind,
                NodeId = nodeId,
                NodeLevel = nodeLevel,
                PreviousEmployeeId = employeeId,
                NewEmployeeId = null,
                ChangedBy = changedBy,
                ChangeReason = reason ?? "Retrait titulaire",
                ChangedAt = now,
            });

            await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
            {
                Kind = MessagingEnumMapper.ToMessage(assignmentKind),
                NodeId = nodeId,
                NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
                EmployeeId = employeeId,
                Removed = true,
            }, aggregateId: employeeId.ToString(), ct: ct);
        }

        await EnqueueResponsiblesChangedAsync(assignmentKind, nodeId, nodeLevel, ct);
        return true;
    }

    public async Task<bool> RemoveStructurePilotAsync(string serviceId, Guid employeeId, Guid? changedBy, string? reason, CancellationToken ct = default)
    {
        var trimmedServiceId = serviceId.Trim();
        var rows = await db.OrgAssignments
            .Where(a => a.Kind == DomainAssignmentKind.Pilote
                && a.NodeId == trimmedServiceId
                && a.EmployeeId == employeeId
                && a.EffectiveTo == null)
            .ToListAsync(ct);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);

        // Pilotes projetés sans ligne OrgAssignment : l'import/bootstrap Prime estampille
        // Role=Pilote + ServiceId/CelluleId sur l'employé sans tracer d'affectation. On tolère
        // ce cas pour que « Retirer » fonctionne au lieu de renvoyer 404 (Not Found).
        var projectedPilote = employee is not null
            && string.Equals(employee.Role, KyntusRoleNames.Pilote, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(employee.ServiceId, trimmedServiceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(employee.CelluleId, trimmedServiceId, StringComparison.OrdinalIgnoreCase));

        if (rows.Count == 0 && !projectedPilote)
            return false;

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.EffectiveTo = now;
            db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
            {
                Id = Guid.NewGuid(),
                Kind = DomainAssignmentKind.Pilote,
                NodeId = trimmedServiceId,
                NodeLevel = DomainNodeLevel.Service,
                PreviousEmployeeId = employeeId,
                NewEmployeeId = null,
                ChangedBy = changedBy,
                ChangeReason = reason,
                ChangedAt = now,
            });
        }

        if (employee is not null)
        {
            employee.Role = KyntusRoleNames.Employee;
            employee.ServiceId = null;
            employee.CelluleId = null;
            employee.PoleId = null;
            employee.ParentId = null;
            employee.UpdatedAt = now;
            await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        }

        // Notifie le retrait une fois (ligne tracée ou pilote projeté) pour resynchroniser l'aval.
        await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
        {
            Kind = MessagingEnumMapper.ToMessage(DomainAssignmentKind.Pilote),
            NodeId = trimmedServiceId,
            NodeLevel = MessagingEnumMapper.ToMessage(DomainNodeLevel.Service),
            EmployeeId = employeeId,
            Removed = true,
        }, aggregateId: employeeId.ToString(), ct: ct);

        await EnqueueResponsiblesChangedAsync(
            DomainAssignmentKind.Pilote, trimmedServiceId, DomainNodeLevel.Service, ct);

        await PersistAndInvalidateAsync(ct);
        return true;
    }

    private async Task ResetDisplacedEmployeeAsync(Guid employeeId, Guid? changedBy, string? reason, CancellationToken ct)
    {
        var remaining = await db.OrgAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return;

        if (remaining.Count > 0)
        {
            // Ré-ancrage primaire sur la charge restante la plus récente.
            var primary = remaining[0];
            await hierarchy.ApplyAssignmentToEmployeeAsync(employee, primary.Kind, primary.NodeId, ct);
            employee.UpdatedAt = DateTime.UtcNow;
            await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
            return;
        }

        var isManager = await db.BusinessDepartments
            .AnyAsync(d => d.ManagerEmployeeId == employeeId, ct);
        if (isManager) return;

        employee.Role = KyntusRoleNames.Employee;
        employee.PoleId = null;
        employee.CelluleId = null;
        employee.ServiceId = null;
        employee.BusinessDepartmentId = null;
        employee.ParentId = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
    }

    public async Task<int> DeduplicateActiveNodeIncumbentsAsync(
        Guid? changedBy = null,
        CancellationToken ct = default)
    {
        var active = await db.OrgAssignments
            .Where(a => a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

        var closed = 0;
        var displaced = new HashSet<Guid>();
        var now = DateTime.UtcNow;

        foreach (var group in active.GroupBy(a => (a.Kind, a.NodeId)))
        {
            var winners = group.ToList();
            if (winners.Count <= 1) continue;

            foreach (var loser in winners.Skip(1))
            {
                if (loser.EffectiveTo is not null) continue;

                loser.EffectiveTo = now;
                loser.SupersededBy = Guid.NewGuid();
                db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
                {
                    Id = Guid.NewGuid(),
                    Kind = loser.Kind,
                    NodeId = loser.NodeId,
                    NodeLevel = loser.NodeLevel,
                    PreviousEmployeeId = loser.EmployeeId,
                    NewEmployeeId = null,
                    ChangedBy = changedBy,
                    ChangeReason = "Dédoublonnage unicité par nœud",
                    ChangedAt = now,
                });

                await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
                {
                    Kind = MessagingEnumMapper.ToMessage(loser.Kind),
                    NodeId = loser.NodeId,
                    NodeLevel = MessagingEnumMapper.ToMessage(loser.NodeLevel),
                    EmployeeId = loser.EmployeeId,
                    Removed = true,
                }, aggregateId: loser.EmployeeId.ToString(), ct: ct);

                closed++;
                displaced.Add(loser.EmployeeId);
            }
        }

        foreach (var employeeId in displaced)
            await ResetDisplacedEmployeeAsync(employeeId, changedBy, "Dédoublonnage unicité par nœud", ct);

        if (closed > 0)
            await PersistAndInvalidateAsync(ct);

        return closed;
    }

    public async Task<UndoImportAssignmentWindowResult> UndoImportAssignmentWindowAsync(
        UndoImportAssignmentWindowRequest request,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var ids = request.EmployeeIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return new UndoImportAssignmentWindowResult(0, 0, 0);

        var rows = await db.OrgAssignments
            .Where(a => ids.Contains(a.EmployeeId))
            .ToListAsync(ct);

        var plan = ImportAssignmentWindowUndoPlanner.Plan(
            rows.Select(a => new AssignmentWindowRow(a.Id, a.EmployeeId, a.EffectiveFrom, a.EffectiveTo)).ToList(),
            request.WindowStart,
            request.WindowEnd);

        var deleted = 0;
        var restored = 0;
        var touched = new HashSet<Guid>();
        var byId = rows.ToDictionary(a => a.Id);

        foreach (var mutation in plan)
        {
            if (!byId.TryGetValue(mutation.AssignmentId, out var row))
                continue;

            touched.Add(row.EmployeeId);
            if (mutation.Kind == AssignmentWindowMutationKind.Delete)
            {
                db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
                {
                    Id = Guid.NewGuid(),
                    Kind = row.Kind,
                    NodeId = row.NodeId,
                    NodeLevel = row.NodeLevel,
                    PreviousEmployeeId = row.EmployeeId,
                    NewEmployeeId = null,
                    ChangedBy = changedBy,
                    ChangeReason = "Annulation import employés",
                    ChangedAt = DateTime.UtcNow,
                });
                db.OrgAssignments.Remove(row);
                deleted++;
                continue;
            }

            row.EffectiveTo = mutation.EffectiveTo;
            row.SupersededBy = mutation.EffectiveTo is null ? null : Guid.NewGuid();
            restored++;
        }

        if (deleted + restored > 0)
            await db.SaveChangesAsync(ct);

        foreach (var employeeId in touched)
            await ResetDisplacedEmployeeAsync(employeeId, changedBy, "Annulation import employés", ct);

        if (touched.Count > 0)
            await PersistAndInvalidateAsync(ct);

        return new UndoImportAssignmentWindowResult(deleted, restored, touched.Count);
    }

    public async Task ClearStructureRoleAsync(string kind, string nodeId, Guid? changedBy, string? reason, CancellationToken ct = default)
    {
        if (!Enum.TryParse<DomainAssignmentKind>(kind, true, out var assignmentKind))
            throw new ArgumentException($"Kind invalide : {kind}");

        var existing = await db.OrgAssignments
            .Where(a => a.Kind == assignmentKind && a.NodeId == nodeId.Trim() && a.EffectiveTo == null)
            .ToListAsync(ct);

        foreach (var row in existing)
        {
            row.EffectiveTo = DateTime.UtcNow;
            db.OrgAssignmentHistories.Add(new OrgAssignmentHistory
            {
                Id = Guid.NewGuid(),
                Kind = assignmentKind,
                NodeId = nodeId.Trim(),
                NodeLevel = row.NodeLevel,
                PreviousEmployeeId = row.EmployeeId,
                NewEmployeeId = null,
                ChangedBy = changedBy,
                ChangeReason = reason,
                ChangedAt = DateTime.UtcNow,
            });

            await outbox.EnqueueAsync(new DirectoryAssignmentChangedMessage
            {
                Kind = MessagingEnumMapper.ToMessage(assignmentKind),
                NodeId = nodeId.Trim(),
                NodeLevel = MessagingEnumMapper.ToMessage(row.NodeLevel),
                EmployeeId = row.EmployeeId,
                Removed = true,
            }, aggregateId: row.EmployeeId.ToString(), ct: ct);
        }

        await PersistAndInvalidateAsync(ct);
    }

    public async Task<string> CreatePoleAsync(string name, Guid businessDepartmentId, CancellationToken ct = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new InvalidOperationException("Le nom du pôle est requis.");
        if (businessDepartmentId == Guid.Empty)
            throw new InvalidOperationException("businessDepartmentId requis.");

        var dept = await db.BusinessDepartments
            .Include(d => d.PoleAssignments)
            .FirstOrDefaultAsync(d => d.Id == businessDepartmentId, ct)
            ?? throw new KeyNotFoundException("Département introuvable.");
        if (dept.Kind != BusinessDepartmentKind.Operational)
            throw new InvalidOperationException("Seuls les départements de production peuvent recevoir des pôles.");
        if (!dept.IsActive)
            throw new InvalidOperationException("Département inactif.");

        var id = NewOrgId("pole");
        db.OrgPoles.Add(new OrgPole
        {
            Id = id,
            Name = trimmedName,
            BusinessDepartmentId = businessDepartmentId,
        });
        await SyncJunctionForPoleAsync(dept, id, ct);
        dept.UpdatedAt = DateTime.UtcNow;

        await outbox.EnqueueAsync(new DirectoryOrgNodeChangedMessage
        {
            NodeId = id,
            Name = trimmedName,
            Level = MessagingEnumMapper.ToMessage(DomainNodeLevel.Pole),
        }, aggregateId: id, ct: ct);
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return id;
    }

    public async Task<bool> AttachPoleToBusinessDepartmentAsync(string poleId, Guid businessDepartmentId, CancellationToken ct = default)
    {
        var trimmedPoleId = poleId.Trim();
        var pole = await db.OrgPoles.FirstOrDefaultAsync(p => p.Id == trimmedPoleId, ct)
            ?? throw new KeyNotFoundException("Pôle introuvable.");

        var dept = await db.BusinessDepartments
            .Include(d => d.PoleAssignments)
            .FirstOrDefaultAsync(d => d.Id == businessDepartmentId, ct)
            ?? throw new KeyNotFoundException("Département introuvable.");
        if (dept.Kind != BusinessDepartmentKind.Operational)
            throw new InvalidOperationException("Seuls les départements de production peuvent recevoir des pôles.");
        if (!dept.IsActive)
            throw new InvalidOperationException("Département inactif.");

        if (pole.BusinessDepartmentId.HasValue && pole.BusinessDepartmentId != businessDepartmentId)
        {
            var oldDept = await db.BusinessDepartments
                .Include(d => d.PoleAssignments)
                .FirstOrDefaultAsync(d => d.Id == pole.BusinessDepartmentId.Value, ct);
            if (oldDept is not null)
            {
                var oldRow = oldDept.PoleAssignments.FirstOrDefault(p => p.PoleId == trimmedPoleId);
                if (oldRow is not null)
                    db.DepartmentPoleAssignments.Remove(oldRow);
                oldDept.UpdatedAt = DateTime.UtcNow;
                await EnqueueBusinessDepartmentChangedAsync(oldDept, ct);
            }
        }

        pole.BusinessDepartmentId = businessDepartmentId;
        await SyncJunctionForPoleAsync(dept, trimmedPoleId, ct);
        dept.UpdatedAt = DateTime.UtcNow;
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<string> CreateCelluleAsync(string poleId, string name, CancellationToken ct = default)
    {
        var id = NewOrgId("cell");
        db.OrgCellules.Add(new OrgCellule { Id = id, Name = name.Trim(), PoleId = poleId.Trim() });
        await outbox.EnqueueAsync(new DirectoryOrgNodeChangedMessage
        {
            NodeId = id,
            Name = name.Trim(),
            Level = MessagingEnumMapper.ToMessage(DomainNodeLevel.Cellule),
            ParentNodeId = poleId.Trim(),
        }, aggregateId: id, ct: ct);
        await PersistAndInvalidateAsync(ct);
        return id;
    }

    public async Task<string> CreateServiceAsync(string celluleId, string name, CancellationToken ct = default)
    {
        var id = NewOrgId("svc");
        db.OrgServices.Add(new OrgService { Id = id, Name = name.Trim(), CelluleId = celluleId.Trim() });
        await outbox.EnqueueAsync(new DirectoryOrgNodeChangedMessage
        {
            NodeId = id,
            Name = name.Trim(),
            Level = MessagingEnumMapper.ToMessage(DomainNodeLevel.Service),
            ParentNodeId = celluleId.Trim(),
        }, aggregateId: id, ct: ct);
        await PersistAndInvalidateAsync(ct);
        return id;
    }

    public async Task<bool> RenameOrgNodeAsync(DomainNodeLevel level, string nodeId, string name, CancellationToken ct = default)
    {
        var trimmedId = nodeId.Trim();
        var trimmedName = name.Trim();
        switch (level)
        {
            case DomainNodeLevel.Pole:
            {
                var row = await db.OrgPoles.FirstOrDefaultAsync(p => p.Id == trimmedId, ct);
                if (row is null) return false;
                row.Name = trimmedName;
                break;
            }
            case DomainNodeLevel.Cellule:
            {
                var row = await db.OrgCellules.FirstOrDefaultAsync(c => c.Id == trimmedId, ct);
                if (row is null) return false;
                row.Name = trimmedName;
                break;
            }
            case DomainNodeLevel.Service:
            {
                var row = await db.OrgServices.FirstOrDefaultAsync(s => s.Id == trimmedId, ct);
                if (row is null) return false;
                row.Name = trimmedName;
                break;
            }
            default:
                return false;
        }

        await outbox.EnqueueAsync(new DirectoryOrgNodeChangedMessage
        {
            NodeId = trimmedId,
            Name = trimmedName,
            Level = MessagingEnumMapper.ToMessage(level),
        }, aggregateId: trimmedId, ct: ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<bool> DeleteOrgNodeAsync(DomainNodeLevel level, string nodeId, CancellationToken ct = default)
    {
        var trimmedId = nodeId.Trim();
        switch (level)
        {
            case DomainNodeLevel.Pole:
            {
                var pole = await db.OrgPoles
                    .Include(p => p.Cellules).ThenInclude(c => c.Services)
                    .FirstOrDefaultAsync(p => p.Id == trimmedId, ct);
                if (pole is null) return false;
                foreach (var cellule in pole.Cellules.ToList())
                {
                    db.OrgServices.RemoveRange(cellule.Services);
                    db.OrgCellules.Remove(cellule);
                }
                db.OrgPoles.Remove(pole);
                break;
            }
            case DomainNodeLevel.Cellule:
            {
                var cellule = await db.OrgCellules
                    .Include(c => c.Services)
                    .FirstOrDefaultAsync(c => c.Id == trimmedId, ct);
                if (cellule is null) return false;
                db.OrgServices.RemoveRange(cellule.Services);
                db.OrgCellules.Remove(cellule);
                break;
            }
            case DomainNodeLevel.Service:
            {
                var service = await db.OrgServices.FirstOrDefaultAsync(s => s.Id == trimmedId, ct);
                if (service is null) return false;
                db.OrgServices.Remove(service);
                break;
            }
            default:
                return false;
        }

        await outbox.EnqueueAsync(new DirectoryOrgNodeChangedMessage
        {
            NodeId = trimmedId,
            Level = MessagingEnumMapper.ToMessage(level),
            IsDeleted = true,
        }, aggregateId: trimmedId, ct: ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<bool> SetAuthSubjectIdAsync(Guid employeeId, Guid authSubjectId, CancellationToken ct = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return false;
        employee.AuthSubjectId = authSubjectId;
        employee.UpdatedAt = DateTime.UtcNow;
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<BusinessDepartmentDto> CreateBusinessDepartmentAsync(CreateBusinessDepartmentRequest request, CancellationToken ct = default)
    {
        var kind = ParseKind(request.Kind);
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await GenerateBusinessDepartmentCodeAsync(kind, ct)
            : request.Code.Trim().ToUpperInvariant();
        if (await db.BusinessDepartments.AnyAsync(d => d.Code == code, ct))
            throw new InvalidOperationException($"Code département déjà utilisé : {code}");

        var dept = new BusinessDepartment
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Kind = kind,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.BusinessDepartments.Add(dept);
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return await MapBusinessDepartmentAsync(dept, ct);
    }

    public async Task<BusinessDepartmentDto?> UpdateBusinessDepartmentAsync(Guid id, UpdateBusinessDepartmentRequest request, CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.Include(d => d.PoleAssignments).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return null;
        dept.Name = request.Name.Trim();
        dept.Kind = ParseKind(request.Kind);
        dept.IsActive = request.IsActive;
        dept.UpdatedAt = DateTime.UtcNow;
        if (dept.Kind == BusinessDepartmentKind.Support)
        {
            db.DepartmentPoleAssignments.RemoveRange(dept.PoleAssignments);
            dept.PoleAssignments.Clear();
        }
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return await MapBusinessDepartmentAsync(dept, ct);
    }

    public async Task<bool> DeleteBusinessDepartmentAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.Include(d => d.PoleAssignments).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return false;
        dept.IsActive = false;
        dept.UpdatedAt = DateTime.UtcNow;
        await EnqueueBusinessDepartmentChangedAsync(dept, ct, isDeleted: true);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<bool> AssignPoleToBusinessDepartmentAsync(Guid departmentId, string poleId, CancellationToken ct = default)
    {
        return await AttachPoleToBusinessDepartmentAsync(poleId, departmentId, ct);
    }

    public async Task<bool> RemovePoleFromBusinessDepartmentAsync(Guid departmentId, string poleId, CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.Include(d => d.PoleAssignments).FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (dept is null) return false;
        var trimmedPoleId = poleId.Trim();
        var row = dept.PoleAssignments.FirstOrDefault(p => p.PoleId == trimmedPoleId);
        var pole = await db.OrgPoles.FirstOrDefaultAsync(p => p.Id == trimmedPoleId, ct);
        if (row is null && pole?.BusinessDepartmentId != departmentId) return false;

        if (row is not null)
            db.DepartmentPoleAssignments.Remove(row);
        if (pole is not null && pole.BusinessDepartmentId == departmentId)
            pole.BusinessDepartmentId = null;

        dept.UpdatedAt = DateTime.UtcNow;
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    public async Task<StructuralRoleAssignmentResult> SetBusinessDepartmentManagerAsync(
        Guid departmentId,
        Guid employeeId,
        Guid? changedBy = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.Include(d => d.PoleAssignments).FirstOrDefaultAsync(d => d.Id == departmentId, ct)
            ?? throw new KeyNotFoundException("Département introuvable.");
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive, ct)
            ?? throw new KeyNotFoundException("Employé introuvable.");

        var revoked = (await exclusivity.RevokeAllStructuralRolesForEmployeeAsync(
            employeeId, changedBy, reason ?? "Nomination manager département", ct)).ToList();

        if (dept.ManagerEmployeeId.HasValue && dept.ManagerEmployeeId != employeeId)
            await exclusivity.DemotePreviousDepartmentManagerAsync(departmentId, employeeId, changedBy, reason, ct);

        dept.ManagerEmployeeId = employeeId;
        dept.UpdatedAt = DateTime.UtcNow;

        employee.Role = KyntusRoleNames.Manager;
        employee.BusinessDepartmentId = dept.Id;
        employee.ParentId = null;
        // Le manager est rattaché uniquement au département : aucune affectation
        // à un pôle/cellule/service précis (ni Operational ni Support).
        employee.PoleId = null;
        employee.CelluleId = null;
        employee.ServiceId = null;
        employee.UpdatedAt = DateTime.UtcNow;

        if (dept.Kind == BusinessDepartmentKind.Support)
        {
            var teamMembers = await db.Employees
                .Where(e => e.BusinessDepartmentId == dept.Id && e.Id != employeeId && e.IsActive)
                .ToListAsync(ct);
            foreach (var member in teamMembers)
            {
                member.ParentId = employeeId;
                member.UpdatedAt = DateTime.UtcNow;
                await EnqueueEmployeeChangedAsync(member, isDeleted: false, emitLegacyCreate: false, ct);
            }
        }

        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await EnqueueEmployeeChangedAsync(employee, isDeleted: false, emitLegacyCreate: false, ct);
        await PersistAndInvalidateAsync(ct);
        return new StructuralRoleAssignmentResult(revoked, [], employeeId.ToString());
    }

    public async Task<bool> ClearBusinessDepartmentManagerAsync(Guid departmentId, CancellationToken ct = default)
    {
        var dept = await db.BusinessDepartments.Include(d => d.PoleAssignments).FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (dept is null) return false;
        dept.ManagerEmployeeId = null;
        dept.UpdatedAt = DateTime.UtcNow;
        await EnqueueBusinessDepartmentChangedAsync(dept, ct);
        await PersistAndInvalidateAsync(ct);
        return true;
    }

    private async Task EnqueueResponsiblesChangedAsync(
        DomainAssignmentKind kind,
        string nodeId,
        DomainNodeLevel nodeLevel,
        CancellationToken ct)
    {
        var rows = await (
            from a in db.OrgAssignments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.Kind == kind && a.NodeId == nodeId && a.EffectiveTo == null
            orderby a.EffectiveFrom
            select new ResponsibleEntry
            {
                ResponsibleId = e.Id,
                Email = e.Email,
                DisplayName = $"{e.FirstName} {e.LastName}".Trim(),
            }).ToListAsync(ct);

        await outbox.EnqueueAsync(new DirectoryEmployeeResponsiblesChangedMessage
        {
            Kind = MessagingEnumMapper.ToMessage(kind),
            NodeId = nodeId,
            NodeLevel = MessagingEnumMapper.ToMessage(nodeLevel),
            Responsibles = rows,
        }, aggregateId: nodeId, ct: ct);
    }

    private async Task EnqueueBusinessDepartmentChangedAsync(BusinessDepartment dept, CancellationToken ct, bool isDeleted = false)
    {
        var poleIds = await db.OrgPoles.AsNoTracking()
            .Where(p => p.BusinessDepartmentId == dept.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (poleIds.Count == 0)
            poleIds = dept.PoleAssignments.Select(p => p.PoleId).ToList();

        await outbox.EnqueueAsync(new DirectoryBusinessDepartmentChangedMessage
        {
            BusinessDepartmentId = dept.Id,
            Code = dept.Code,
            Name = dept.Name,
            Kind = dept.Kind.ToString(),
            ManagerEmployeeId = dept.ManagerEmployeeId,
            PoleIds = poleIds,
            IsDeleted = isDeleted,
        }, aggregateId: dept.Id.ToString(), ct: ct);
    }

    private Task SyncJunctionForPoleAsync(BusinessDepartment dept, string poleId, CancellationToken ct)
    {
        if (dept.PoleAssignments.Any(p => p.PoleId == poleId)) return Task.CompletedTask;
        var row = new DepartmentPoleAssignment
        {
            BusinessDepartmentId = dept.Id,
            PoleId = poleId,
            CreatedAt = DateTime.UtcNow,
        };
        db.DepartmentPoleAssignments.Add(row);
        dept.PoleAssignments.Add(row);
        return Task.CompletedTask;
    }

    private async Task<BusinessDepartment?> ResolveBusinessDepartmentAsync(Guid? id, CancellationToken ct)
    {
        if (!id.HasValue) return null;
        return await db.BusinessDepartments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id.Value, ct);
    }

    private static BusinessDepartmentKind ParseKind(string kind) =>
        Enum.TryParse<BusinessDepartmentKind>(kind, true, out var k) ? k : BusinessDepartmentKind.Operational;

    private async Task<string> GenerateBusinessDepartmentCodeAsync(BusinessDepartmentKind kind, CancellationToken ct)
    {
        var prefix = kind == BusinessDepartmentKind.Support ? "SUP" : "OP";
        var existingCodes = await db.BusinessDepartments.AsNoTracking()
            .Where(d => d.Kind == kind && d.Code.StartsWith(prefix))
            .Select(d => d.Code)
            .ToListAsync(ct);

        var maxNum = 0;
        foreach (var existing in existingCodes)
        {
            var suffix = existing.Length > prefix.Length + 1 && existing[prefix.Length] == '-'
                ? existing[(prefix.Length + 1)..]
                : existing[prefix.Length..];
            if (int.TryParse(suffix, out var n) && n > maxNum)
                maxNum = n;
        }

        var next = maxNum + 1;
        string code;
        do
        {
            code = $"{prefix}-{next:D3}";
            next++;
        } while (await db.BusinessDepartments.AnyAsync(d => d.Code == code, ct));

        return code;
    }

    private async Task<BusinessDepartmentDto> MapBusinessDepartmentAsync(BusinessDepartment d, CancellationToken ct)
    {
        var poleIds = await db.OrgPoles.AsNoTracking()
            .Where(p => p.BusinessDepartmentId == d.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (poleIds.Count == 0)
            poleIds = d.PoleAssignments.Select(p => p.PoleId).ToList();
        return MapBusinessDepartment(d, poleIds);
    }

    private static BusinessDepartmentDto MapBusinessDepartment(BusinessDepartment d, IReadOnlyList<string> poleIds) => new(
        d.Id.ToString(),
        d.Code,
        d.Name,
        d.Kind.ToString(),
        d.ManagerEmployeeId?.ToString(),
        d.IsActive,
        poleIds);

    private async Task EnqueueEmployeeChangedAsync(Employee employee, bool isDeleted, bool emitLegacyCreate, CancellationToken ct)
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
            IsDeleted = isDeleted,
            HireDate = employee.HireDate,
            ChefDeProjetId = employee.ChefDeProjetId,
            SuperviseurId = employee.SuperviseurId,
            ReferentTechniqueId = employee.ReferentTechniqueId,
            IdTechnicien = employee.IdTechnicien,
            HtelCode = employee.HtelCode,
            HtelTechnicienName = employee.HtelTechnicienName,
        }, aggregateId: employee.Id.ToString(), ct: ct);

        if (!emitLegacyCreate || isDeleted)
            return;

        await outbox.EnqueueAsync(new EmployeCreatedMessage
        {
            EmployeId = employee.Id,
            Nom = employee.LastName,
            Prenom = employee.FirstName,
            Email = employee.Email,
            Role = employee.Role,
            ManagerId = employee.ParentId ?? Guid.Empty,
            SupervisorId = employee.ParentId ?? Guid.Empty,
            ServiceId = Guid.TryParse(employee.ServiceId, out var sid) ? sid : Guid.Empty,
            ServiceNom = employee.ServiceId ?? "",
            PrimeServiceId = employee.ServiceId,
            DateEmbauche = employee.HireDate,
        }, aggregateId: employee.Id.ToString(), ct: ct);
    }

    private async Task<(string? PoleId, string? CelluleId, string? ServiceId)> ResolveOrgIdsAsync(string? serviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serviceId)) return (null, null, null);
        var svc = await db.OrgServices.AsNoTracking().Include(s => s.Cellule).FirstOrDefaultAsync(s => s.Id == serviceId.Trim(), ct);
        if (svc is null) return (null, null, serviceId.Trim());
        return (svc.Cellule.PoleId, svc.CelluleId, svc.Id);
    }

    private static string NewOrgId(string prefix)
    {
        var s = Guid.NewGuid().ToString("N");
        return $"{prefix}-{s[..Math.Min(12, s.Length)]}";
    }

    private static EmployeeDto Map(Employee e) => new(
        e.Id.ToString(),
        e.FirstName,
        e.LastName,
        e.Role,
        e.ParentId?.ToString(),
        e.ServiceId,
        e.PoleId ?? "",
        e.CelluleId,
        e.Email,
        null,
        e.BusinessDepartmentId?.ToString(),
        null,
        e.ChefDeProjetId?.ToString(),
        e.SuperviseurId?.ToString(),
        e.ReferentTechniqueId?.ToString(),
        null,
        e.IdTechnicien,
        e.HtelCode);
}
