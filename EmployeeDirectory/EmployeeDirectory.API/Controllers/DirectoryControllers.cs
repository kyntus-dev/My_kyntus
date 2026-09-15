using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.BusinessDepartments;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Employees;
using EmployeeDirectory.Application.Exceptions;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Application.Org;
using EmployeeDirectory.Application.Queries.Health;
using EmployeeDirectory.Application.Rebac;
using EmployeeDirectory.Application.Reconcile;
using EmployeeDirectory.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EmployeeDirectory.API.Controllers;

[ApiController]
[Route("api/directory")]
[Authorize]
public class DirectoryEmployeesController(IMediator mediator) : ControllerBase
{
    [HttpGet("employees")]
    public async Task<ActionResult<IReadOnlyList<EmployeeDto>>> List(
        [FromQuery] string? role,
        [FromQuery] string? poleId,
        CancellationToken ct) =>
        Ok(await mediator.Send(new ListEmployeesQuery(role, poleId), ct));

    [HttpGet("employees/by-email")]
    public async Task<ActionResult<EmployeeDto>> GetByEmail([FromQuery] string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email requis." });

        var jwtEmail = User.GetEmail();
        var role = User.GetAuthRole();
        var privileged = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
        if (!privileged
            && (string.IsNullOrWhiteSpace(jwtEmail)
                || !string.Equals(jwtEmail, email.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Forbid();

        var e = await mediator.Send(new GetEmployeeByEmailQuery(email.Trim()), ct);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpGet("employees/check-email")]
    public async Task<ActionResult<object>> CheckEmail([FromQuery] string email, [FromQuery] Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email requis." });
        var isUsed = await mediator.Send(new CheckEmployeeEmailQuery(email, excludeId), ct);
        return Ok(new { isUnique = !isUsed });
    }

    [HttpGet("employees/{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Get(Guid id, CancellationToken ct)
    {
        var e = await mediator.Send(new GetEmployeeByIdQuery(id), ct);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpPost("employees")]
    public async Task<ActionResult<EmployeeDto>> Create([FromBody] CreateEmployeeRequest body, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        try
        {
            var result = await mediator.Send(new CreateEmployeeCommand(body, changedBy), ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (HttpRequestException ex) when (body.IdTechnicien.HasValue)
        {
            return StatusCode(502, new { error = "La liaison HTEL n'a pas pu être vérifiée. Aucune fiche n'a été créée.", detail = ex.Message });
        }
        catch (OperationCanceledException) when (body.IdTechnicien.HasValue && !ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Timeout HTEL. Aucune fiche n'a été créée." });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (body.IdTechnicien.HasValue && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Conflict(new { error = "Une fiche utilise déjà cet identifiant HTEL ou cet email. Actualisez la comparaison." });
        }
    }

    [HttpPost("employees/bulk")]
    public async Task<ActionResult<IReadOnlyList<BulkCreateEmployeeResult>>> BulkCreate(
        [FromBody] BulkCreateEmployeesRequest body,
        CancellationToken ct)
    {
        if (body.Items is null || body.Items.Count == 0)
            return BadRequest(new { error = "items requis." });

        var changedBy = User.GetSubjectId();
        var results = await mediator.Send(new BulkCreateEmployeesCommand(body, changedBy), ct);
        return Ok(results);
    }

    [HttpPut("employees/{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(Guid id, [FromBody] UpdateEmployeeRequest body, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        var result = await mediator.Send(new UpdateEmployeeCommand(id, body, changedBy), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Détache l'employé du périmètre (pole / cellule / service) et clôture les titulatures sur le nœud.
    /// </summary>
    [HttpPost("employees/{id:guid}/clear-org-placement")]
    public async Task<ActionResult<EmployeeDto>> ClearOrgPlacement(
        Guid id,
        [FromBody] ClearOrgPlacementRequest? body,
        CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        var result = await mediator.Send(
            new ClearOrgPlacementCommand(id, body ?? new ClearOrgPlacementRequest(), changedBy),
            ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("employees/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        return await mediator.Send(new DeleteEmployeeCommand(id, changedBy), ct) ? NoContent() : NotFound();
    }

    [HttpPatch("employees/{id:guid}/auth-subject")]
    public async Task<IActionResult> SetAuthSubject(Guid id, [FromBody] SetAuthSubjectRequest body, CancellationToken ct)
    {
        if (body.AuthSubjectId == Guid.Empty)
            return BadRequest(new { error = "authSubjectId requis" });
        return await mediator.Send(new SetAuthSubjectCommand(id, body.AuthSubjectId), ct) ? NoContent() : NotFound();
    }

    [HttpGet("employees/{id:guid}/assignment-history")]
    public async Task<ActionResult<IReadOnlyList<AssignmentHistoryEntryDto>>> History(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetAssignmentHistoryQuery(id), ct));

    [HttpGet("employees/{id:guid}/pilot-rotation-history")]
    public async Task<ActionResult<IReadOnlyList<PilotRotationHistoryEntryDto>>> PilotRotationHistory(
        Guid id,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetPilotRotationHistoryQuery(id), ct));

    [HttpGet("pilot-rotations")]
    public async Task<ActionResult<IReadOnlyList<PilotRotationSummaryDto>>> ListPilotRotations(
        [FromQuery] string? serviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? minRotations,
        [FromQuery] int? maxRotations,
        [FromQuery] string? sort,
        CancellationToken ct) =>
        Ok(await mediator.Send(
            new ListPilotRotationsQuery(serviceId, from, to, minRotations, maxRotations, sort),
            ct));

    [HttpGet("employees/{id:guid}/pilot-rotation-eligibility")]
    public async Task<ActionResult<PilotRotationEligibilityDto>> PilotRotationEligibility(
        Guid id,
        [FromQuery] string targetServiceId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetServiceId))
            return BadRequest(new { error = "targetServiceId requis." });
        return Ok(await mediator.Send(new GetPilotRotationEligibilityQuery(id, targetServiceId), ct));
    }
}

[ApiController]
[Route("api/directory/org")]
[Authorize]
public class DirectoryOrgController(IMediator mediator, ILogger<DirectoryOrgController> logger) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<OrgOverviewDto>> Overview(CancellationToken ct)
    {
        // Mutations org (pilotes, cellules, services…) doivent être visibles immédiatement :
        // interdire le cache navigateur / proxy sur cet endpoint.
        Response.Headers.CacheControl = "private, no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        return Ok(await mediator.Send(new GetOrgOverviewQuery(), ct));
    }

    [HttpGet("assignments/as-of")]
    public async Task<ActionResult<OrgAssignmentAsOfDto>> AsOf([FromQuery] DateTime date, CancellationToken ct) =>
        Ok(await mediator.Send(new GetAssignmentsAsOfQuery(date), ct));

    [HttpPost("assignments/created-after")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> AssignmentsCreatedAfter(
        [FromBody] EmployeeIdsCreatedAfterRequest body,
        CancellationToken ct)
    {
        if (body is null || body.EmployeeIds is null || body.EmployeeIds.Count == 0)
            return Ok(Array.Empty<Guid>());
        return Ok(await mediator.Send(
            new GetEmployeeIdsWithAssignmentsAfterQuery(body.EmployeeIds, body.After), ct));
    }

    [HttpPost("assignments/undo-import-window")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<UndoImportAssignmentWindowResult>> UndoImportWindow(
        [FromBody] UndoImportAssignmentWindowRequest body,
        CancellationToken ct)
    {
        if (body.EmployeeIds is null || body.EmployeeIds.Count == 0)
            return BadRequest(new { error = "employeeIds requis." });
        var changedBy = User.GetSubjectId();
        return Ok(await mediator.Send(new UndoImportAssignmentWindowCommand(body, changedBy), ct));
    }

    [HttpPost("structure/poles")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<object>> CreatePole([FromBody] CreatePoleRequest? body, CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new { error = "Corps de requête requis." });
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { error = "Le nom du pôle est requis." });
        if (body.BusinessDepartmentId == Guid.Empty)
            return BadRequest(new { error = "businessDepartmentId requis." });

        try
        {
            var id = await mediator.Send(new CreatePoleCommand(body.Name.Trim(), body.BusinessDepartmentId), ct);
            return Ok(new { id });
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "CreatePole DbUpdateException (dept={DeptId}, name={Name})", body.BusinessDepartmentId, body.Name);
            var detail = ex.InnerException?.Message ?? ex.Message;
            if (ex is DbUpdateConcurrencyException)
            {
                return Conflict(new { error = "Conflit de concurrence lors de la création du pôle. Réessayez." });
            }
            if (detail.Contains("BusinessDepartmentId", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("42703", StringComparison.Ordinal))
            {
                return StatusCode(500, new
                {
                    error = "Schéma base de données obsolète (colonne BusinessDepartmentId). Redémarrez le service Directory.",
                });
            }

            if (detail.Contains("23505", StringComparison.Ordinal)
                || detail.Contains("unique", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { error = "Contrainte d'unicité violée lors de la création du pôle." });
            }

            return StatusCode(500, new { error = "Erreur base de données lors de la création du pôle." });
        }
    }

    [HttpPatch("structure/poles/{nodeId}/business-department")]
    public async Task<IActionResult> AttachPoleToDepartment(string nodeId, [FromBody] AttachPoleToDepartmentRequest body, CancellationToken ct)
    {
        try
        {
            return await mediator.Send(new AttachPoleToDepartmentCommand(nodeId, body.BusinessDepartmentId), ct)
                ? NoContent()
                : NotFound();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
    }

    [HttpPost("structure/poles/{poleId}/cellules")]
    public async Task<ActionResult<object>> CreateCellule(string poleId, [FromBody] CreateNodeRequest body, CancellationToken ct)
    {
        var id = await mediator.Send(new CreateCelluleCommand(poleId, body.Name), ct);
        return Ok(new { id });
    }

    [HttpPost("structure/cellules/{celluleId}/services")]
    public async Task<ActionResult<object>> CreateService(string celluleId, [FromBody] CreateNodeRequest body, CancellationToken ct)
    {
        var id = await mediator.Send(new CreateServiceCommand(celluleId, body.Name), ct);
        return Ok(new { id });
    }

    [HttpPut("structure/poles/{nodeId}")]
    public async Task<IActionResult> RenamePole(string nodeId, [FromBody] CreateNodeRequest body, CancellationToken ct) =>
        await mediator.Send(new RenameOrgNodeCommand(OrgNodeLevel.Pole, nodeId, body.Name), ct) ? NoContent() : NotFound();

    [HttpPut("structure/poles/{poleId}/cellules/{nodeId}")]
    public async Task<IActionResult> RenameCellule(string poleId, string nodeId, [FromBody] CreateNodeRequest body, CancellationToken ct) =>
        await mediator.Send(new RenameOrgNodeCommand(OrgNodeLevel.Cellule, nodeId, body.Name), ct) ? NoContent() : NotFound();

    [HttpPut("structure/cellules/{celluleId}/services/{nodeId}")]
    public async Task<IActionResult> RenameService(string celluleId, string nodeId, [FromBody] CreateNodeRequest body, CancellationToken ct) =>
        await mediator.Send(new RenameOrgNodeCommand(OrgNodeLevel.Service, nodeId, body.Name), ct) ? NoContent() : NotFound();

    [HttpDelete("structure/poles/{nodeId}")]
    public async Task<IActionResult> DeletePole(string nodeId, CancellationToken ct) =>
        await mediator.Send(new DeleteOrgNodeCommand(OrgNodeLevel.Pole, nodeId), ct) ? NoContent() : NotFound();

    [HttpDelete("structure/poles/{poleId}/cellules/{nodeId}")]
    public async Task<IActionResult> DeleteCellule(string poleId, string nodeId, CancellationToken ct) =>
        await mediator.Send(new DeleteOrgNodeCommand(OrgNodeLevel.Cellule, nodeId), ct) ? NoContent() : NotFound();

    [HttpDelete("structure/cellules/{celluleId}/services/{nodeId}")]
    public async Task<IActionResult> DeleteService(string celluleId, string nodeId, CancellationToken ct) =>
        await mediator.Send(new DeleteOrgNodeCommand(OrgNodeLevel.Service, nodeId), ct) ? NoContent() : NotFound();

    [HttpPost("assignments/{kind}/{nodeId}")]
    public async Task<ActionResult<StructuralRoleAssignmentResult>> Assign(string kind, string nodeId, [FromBody] AssignRequest body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.EmployeeId, out var employeeId))
            return BadRequest(new { error = "employeeId invalide" });
        var changedBy = User.GetSubjectId();
        IReadOnlyList<Guid>? revokeIds = null;
        if (body.RevokeEmployeeIds is { Count: > 0 })
        {
            revokeIds = body.RevokeEmployeeIds
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
        }

        var forceOverride = body.ForceTenureOverride == true
            && User.IsInRole("Admin");

        try
        {
            var result = await mediator.Send(
                new AssignStructureRoleCommand(
                    kind, nodeId, employeeId, changedBy, body.Reason, revokeIds, forceOverride),
                ct);
            return Ok(result);
        }
        catch (PilotRotationTenureException ex)
        {
            return UnprocessableEntity(new
            {
                error = ex.Message,
                currentServiceId = ex.CurrentServiceId,
                currentSince = ex.CurrentSince,
                eligibleAt = ex.EligibleAt,
                daysRemaining = ex.DaysRemaining,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Synchronise exactement les nœuds managés d'un employé pour un kind
    /// (ChefDeProjet / Superviseur / ReferentTechnique).
    /// </summary>
    [HttpPut("assignments/{kind}/employees/{employeeId:guid}")]
    public async Task<ActionResult<StructuralAssignmentsReconcileResult>> ReconcileEmployeeAssignments(
        string kind,
        Guid employeeId,
        [FromBody] ReconcileEmployeeAssignmentsRequest body,
        CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new { error = "Corps de requête requis." });

        var changedBy = User.GetSubjectId();
        try
        {
            var result = await mediator.Send(
                new ReconcileEmployeeStructuralAssignmentsCommand(
                    kind,
                    employeeId,
                    body.NodeIds ?? [],
                    body.PrimaryNodeId ?? string.Empty,
                    changedBy,
                    body.Reason),
                ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("assignments/Pilote/{serviceId}/employees/{employeeId:guid}")]
    public async Task<IActionResult> RemovePilot(string serviceId, Guid employeeId, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        return await mediator.Send(new RemoveStructurePilotCommand(serviceId, employeeId, changedBy), ct)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("assignments/{kind}/{nodeId}/employees/{employeeId:guid}")]
    public async Task<IActionResult> RemoveAssignment(string kind, string nodeId, Guid employeeId, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        return await mediator.Send(
            new RemoveStructureAssignmentCommand(kind, nodeId, employeeId, changedBy, null), ct)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("assignments/{kind}/{nodeId}")]
    public async Task<IActionResult> Clear(string kind, string nodeId, CancellationToken ct)
    {
        var changedBy = User.GetSubjectId();
        await mediator.Send(new ClearStructureRoleCommand(kind, nodeId, changedBy), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/directory/rebac")]
[Authorize]
public class DirectoryRebacController(IMediator mediator) : ControllerBase
{
    [HttpGet("is-descendant")]
    public async Task<ActionResult<object>> IsDescendant([FromQuery] Guid viewerId, [FromQuery] Guid targetId, CancellationToken ct)
    {
        var isDescendant = await mediator.Send(new IsDescendantQuery(viewerId, targetId), ct);
        return Ok(new { isDescendant });
    }

    [HttpGet("managed-nodes")]
    public async Task<ActionResult<RebacManagedNodesDto>> ManagedNodes(
        [FromQuery] Guid employeeId,
        [FromQuery] string kind,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetManagedNodesQuery(employeeId, kind), ct));

    [HttpGet("responsibles")]
    public async Task<ActionResult<object>> Responsibles(
        [FromQuery] string kind,
        [FromQuery] string nodeId,
        CancellationToken ct)
    {
        var list = await mediator.Send(new GetResponsiblesQuery(kind, nodeId), ct);
        return Ok(new { responsibles = list });
    }

    [HttpGet("managed-employees")]
    public async Task<ActionResult<object>> ManagedEmployees(
        [FromQuery] Guid employeeId,
        CancellationToken ct)
    {
        var ids = await mediator.Send(new GetManagedEmployeesQuery(employeeId), ct);
        return Ok(new { employeeIds = ids });
    }

    [HttpPost("can-act")]
    public async Task<ActionResult<CanActOnResultDto>> CanAct(
        [FromBody] CanActOnRequest body,
        CancellationToken ct)
    {
        // L'identité Auth (sub) et l'identité Directory n'utilisent pas toujours
        // le même Guid. Résoudre l'acteur depuis le JWT empêche à la fois les 403
        // sur l'accès à soi-même et l'usurpation d'un ActorId fourni par le client.
        EmployeeDto? actor = null;
        var email = (User.FindFirst("email")?.Value
                     ?? User.FindFirst(ClaimTypes.Email)?.Value)?.Trim();
        if (!string.IsNullOrWhiteSpace(email))
            actor = await mediator.Send(new GetEmployeeByEmailQuery(email), ct);

        var subjectId = User.GetSubjectId();
        if (actor is null && subjectId is { } id && id != Guid.Empty)
            actor = await mediator.Send(new GetEmployeeByIdQuery(id), ct);

        var actorId = actor is not null && Guid.TryParse(actor.Id, out var resolvedId)
            ? resolvedId
            : Guid.Empty;

        return Ok(await mediator.Send(new CanActOnCommand(actorId, body.TargetEmployeeId), ct));
    }

    [HttpGet("hierarchy/{employeeId:guid}/subtree")]
    public async Task<ActionResult<RebacSubtreeDto>> Subtree(Guid employeeId, CancellationToken ct) =>
        Ok(await mediator.Send(new GetSubtreeQuery(employeeId), ct));
}

public record CanActOnRequest(Guid ActorId, Guid TargetEmployeeId);

[ApiController]
[Route("api/iam")]
[Authorize]
public class IamController(IMediator mediator) : ControllerBase
{
    [HttpGet("effective-permissions")]
    public async Task<ActionResult<EffectivePermissionsDto>> EffectivePermissions(
        [FromQuery] string? role,
        CancellationToken ct)
    {
        var subjectId = User.GetSubjectId() ?? Guid.Empty;
        var callerRole = User.GetAuthRole() ?? "Employee";
        var preview = !string.IsNullOrWhiteSpace(role)
                      && (string.Equals(callerRole, "Admin", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(callerRole, "RH", StringComparison.OrdinalIgnoreCase));
        return Ok(await mediator.Send(new GetEffectivePermissionsQuery(subjectId, preview ? role!.Trim() : callerRole), ct));
    }

    [HttpGet("leave-workflow")]
    public async Task<ActionResult<LeaveWorkflowDto>> LeaveWorkflow(CancellationToken ct) =>
        Ok(await mediator.Send(new GetLeaveWorkflowQuery(), ct));

    [HttpGet("catalog")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<IamCatalogDto>> Catalog(CancellationToken ct) =>
        Ok(await mediator.Send(new GetIamCatalogQuery(), ct));

    [HttpGet("permissions")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<IReadOnlyList<IamPermissionRowDto>>> Permissions(
        [FromQuery] string? role,
        CancellationToken ct) =>
        Ok(await mediator.Send(new ListIamPermissionsQuery(role), ct));

    [HttpGet("matrix")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<IamMatrixDto>> Matrix(
        [FromQuery] string role,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role)) return BadRequest(new { error = "role requis" });
        return Ok(await mediator.Send(new GetIamMatrixQuery(role.Trim(), scope), ct));
    }

    [HttpPut("permissions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IamPermissionRowDto>> Upsert(
        [FromBody] UpsertIamPermissionRequest body,
        CancellationToken ct)
    {
        try
        {
            return Ok(await mediator.Send(new UpsertIamPermissionCommand(body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("permissions/bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> UpsertBulk(
        [FromBody] UpsertIamPermissionsBulkRequest body,
        CancellationToken ct)
    {
        try
        {
            var upserted = await mediator.Send(new UpsertIamPermissionsBulkCommand(body.Items ?? []), ct);
            return Ok(new { upserted });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<object>> Evaluate([FromBody] EvaluatePolicyRequest body, CancellationToken ct)
    {
        var subjectId = User.GetSubjectId() ?? Guid.Empty;
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await mediator.Send(
            new EvaluatePolicyCommand(subjectId, role, body.Action, body.ResourceType, body.ResourceId),
            ct);
        return Ok(new { allowed = decision.Allowed, reason = decision.Reason });
    }
}

[ApiController]
[Route("api/directory/reconcile")]
public class DirectoryReconcileController(IMediator mediator) : ControllerBase
{
    [HttpGet("verify")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<DirectoryReconcileVerifyDto>> Verify(CancellationToken ct) =>
        Ok(await mediator.Send(new VerifyDirectoryReconcileQuery(), ct));

    [HttpPost]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<DirectoryReconcileReportDto>> Reconcile(CancellationToken ct) =>
        Ok(await mediator.Send(new ReconcileDirectoryCommand(), ct));

    /// <summary>Clôture les doublons de titulaires actifs par nœud (Kind, NodeId).</summary>
    [HttpPost("dedupe-node-incumbents")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<object>> DedupeNodeIncumbents(CancellationToken ct)
    {
        var closed = await mediator.Send(new DeduplicateActiveNodeIncumbentsCommand(null), ct);
        return Ok(new { closed });
    }
}

[ApiController]
[Route("api/directory/business-departments")]
[Authorize]
public class DirectoryBusinessDepartmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BusinessDepartmentDto>>> List(CancellationToken ct) =>
        Ok(await mediator.Send(new ListBusinessDepartmentsQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BusinessDepartmentDto>> Get(Guid id, CancellationToken ct)
    {
        var row = await mediator.Send(new GetBusinessDepartmentByIdQuery(id), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<BusinessDepartmentDto>> Create([FromBody] CreateBusinessDepartmentRequest body, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateBusinessDepartmentCommand(body), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<BusinessDepartmentDto>> Update(Guid id, [FromBody] UpdateBusinessDepartmentRequest body, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateBusinessDepartmentCommand(id, body), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await mediator.Send(new DeleteBusinessDepartmentCommand(id), ct) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/poles/{poleId}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> AssignPole(Guid id, string poleId, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new AssignPoleToBusinessDepartmentCommand(id, poleId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
    }

    [HttpDelete("{id:guid}/poles/{poleId}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> RemovePole(Guid id, string poleId, CancellationToken ct) =>
        await mediator.Send(new RemovePoleFromBusinessDepartmentCommand(id, poleId), ct) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/manager")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<StructuralRoleAssignmentResult>> SetManager(Guid id, [FromBody] SetBusinessDepartmentManagerRequest body, CancellationToken ct)
    {
        if (!Guid.TryParse(body.EmployeeId, out var employeeId))
            return BadRequest(new { error = "employeeId invalide" });
        try
        {
            var changedBy = User.GetSubjectId();
            var result = await mediator.Send(new SetBusinessDepartmentManagerCommand(id, employeeId, changedBy), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("{id:guid}/manager")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> ClearManager(Guid id, CancellationToken ct) =>
        await mediator.Send(new ClearBusinessDepartmentManagerCommand(id), ct) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/directory/htel")]
[Authorize]
public class DirectoryHtelController(IHtelFusionService htelFusion) : ControllerBase
{
    [HttpGet("techniciens")]
    public async Task<ActionResult<IReadOnlyList<HtelTechnicienDto>>> ListTechniciens(
        [FromQuery] bool? actifOnly,
        CancellationToken ct)
    {
        try
        {
            return Ok(await htelFusion.ListTechniciensAsync(actifOnly, ct));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Timeout HTEL. Réessayez la comparaison." });
        }
    }

    [HttpGet("liaisons")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<HtelLiaisonsReportDto>> Liaisons(CancellationToken ct)
    {
        try
        {
            return Ok(await htelFusion.GetLiaisonsAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "HTEL indisponible.", detail = ex.Message });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "Timeout HTEL." });
        }
    }

    [HttpPost("techniciens/sync")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<HtelSyncReportDto>> Sync(CancellationToken ct)
    {
        try
        {
            return Ok(await htelFusion.SyncAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "HTEL indisponible.", detail = ex.Message });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "Timeout HTEL." });
        }
    }

    [HttpPost("liaisons/link")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> Link([FromBody] HtelLinkRequest body, CancellationToken ct)
    {
        if (body.EmployeeId == Guid.Empty || body.IdTechnicien <= 0)
            return BadRequest(new { error = "employeeId et idTechnicien requis." });
        try
        {
            return await htelFusion.LinkAsync(body.EmployeeId, body.IdTechnicien, ct)
                ? NoContent()
                : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Conflict(new { error = "Cet identifiant HTEL vient d'être lié. Actualisez la comparaison." });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Timeout HTEL." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "HTEL indisponible.", detail = ex.Message });
        }
    }

    [HttpPost("liaisons/identity")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> ReviewIdentity([FromBody] HtelIdentityReviewRequest body, CancellationToken ct)
    {
        if (body.EmployeeId == Guid.Empty || body.IdTechnicien <= 0)
            return BadRequest(new { error = "employeeId et idTechnicien requis." });
        try
        {
            return await htelFusion.ReviewIdentityAsync(body, ct) ? NoContent() : NotFound();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        { return Conflict(new { error = "La fiche a changé. Actualisez la comparaison." }); }
        catch (HttpRequestException ex) { return StatusCode(502, new { error = ex.Message }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return StatusCode(504, new { error = "Timeout HTEL." }); }
    }

    [HttpPost("liaisons/unlink")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> Unlink([FromBody] HtelUnlinkRequest body, CancellationToken ct)
    {
        if (body.EmployeeId == Guid.Empty)
            return BadRequest(new { error = "employeeId requis." });
        return await htelFusion.UnlinkAsync(body.EmployeeId, ct) ? NoContent() : NotFound();
    }
}

[ApiController]
[Route("api/directory")]
public class DirectoryHealthController(IMediator mediator) : ControllerBase
{
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var dto = await mediator.Send(new GetHealthQuery(), ct);
        return Ok(new { status = dto.Status, service = dto.Service });
    }
}

public record CreateNodeRequest(string Name);
public record AssignRequest(
    string EmployeeId,
    string? Reason,
    IReadOnlyList<string>? RevokeEmployeeIds,
    bool? ForceTenureOverride);
public record ReconcileEmployeeAssignmentsRequest(
    IReadOnlyList<string>? NodeIds,
    string? PrimaryNodeId,
    string? Reason);
public record SetAuthSubjectRequest(Guid AuthSubjectId);
public record EvaluatePolicyRequest(string Action, string ResourceType, string? ResourceId);

internal static class ClaimsExtensions
{
    public static Guid? GetSubjectId(this System.Security.Claims.ClaimsPrincipal user) =>
        Kyntus.Identity.Jwt.KyntusClaimsPrincipalExtensions.GetSubjectId(user);
    public static string? GetAuthRole(this System.Security.Claims.ClaimsPrincipal user) =>
        Kyntus.Identity.Jwt.KyntusClaimsPrincipalExtensions.GetAuthRole(user);
    public static string? GetEmail(this System.Security.Claims.ClaimsPrincipal user) =>
        Kyntus.Identity.Jwt.KyntusClaimsPrincipalExtensions.GetEmail(user);
}
