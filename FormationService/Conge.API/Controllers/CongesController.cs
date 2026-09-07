using Conge.Application.Abstractions;
using Conge.Application.Commands.AnnulerConge;
using Conge.Application.Commands.ConfigConge;
using Conge.Application.Commands.DemanderConge;
using Conge.Application.Commands.RefuserConge;
using Conge.Application.Commands.ValiderConge;
using Conge.Application.Queries.GetDemandesByEmploye;
using Conge.Application.Queries.GetDemandesByManager;
using Conge.Application.Queries.GetHistorique;
using Conge.Application.Queries.GetHistoriqueRh;
using Conge.Application.Queries.GetPendingCongesRhCount;
using Conge.Application.Queries.GetSoldeByEmploye;
using Conge.Application.Services;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Kyntus.Messaging.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CongesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IEmployeSnapshotEnsure _ensure;
    private readonly IRebacClient? _rebac;
    private readonly IPolicyEvaluator _policy;

    public CongesController(
        IMediator mediator,
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo,
        IEmployeSnapshotEnsure ensure,
        IPolicyEvaluator policy,
        IRebacClient? rebac = null)
    {
        _mediator = mediator;
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
        _ensure = ensure;
        _policy = policy;
        _rebac = rebac;
    }

    // ── Demandes ─────────────────────────────────────────────────────────────

    /// <summary>POST /api/conges — Soumettre une demande de congé</summary>
    [HttpPost]
    public async Task<IActionResult> DemanderConge([FromBody] DemanderCongeCommand command, CancellationToken ct)
    {
        var access = await ResolveEmployeAccessAsync(command.EmployeId, ct);
        if (access.Error is not null)
            return access.Error;

        var employeId = access.CanonicalId;
        var id = await _mediator.Send(command with { EmployeId = employeId }, ct);
        return CreatedAtAction(nameof(GetDemandesByEmploye), new { employeId }, new { id });
    }

    /// <summary>GET /api/conges/employe/{employeId} — Consulter ses congés</summary>
    [HttpGet("employe/{employeId:guid}")]
    public async Task<IActionResult> GetDemandesByEmploye(
        Guid employeId,
        [FromQuery] StatutDemande? statut,
        CancellationToken ct)
    {
        var access = await ResolveEmployeAccessAsync(employeId, ct);
        if (access.Error is not null)
            return access.Error;
        employeId = access.CanonicalId;

        var result = await _mediator.Send(new GetDemandesByEmployeQuery(employeId, statut), ct);
        return Ok(result);
    }

    /// <summary>GET /api/conges/manager/{managerId} — File validation (superviseur ou RH)</summary>
    [HttpGet("manager/{managerId:guid}")]
    public async Task<IActionResult> GetDemandesByManager(Guid managerId, CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        if (!IsPrivilegedHrOrAdmin() && !IsSuperviseurRole() && actorId.Value != managerId)
            return Forbid();

        var result = await _mediator.Send(new GetDemandesByManagerQuery(managerId), ct);
        return Ok(result);
    }

    /// <summary>GET /api/conges/historique — Historique global par année (RH)</summary>
    [HttpGet("historique")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> GetHistoriqueRh(
        [FromQuery] int annee,
        CancellationToken ct)
    {
        if (annee < 2000 || annee > 2100)
            return BadRequest(new { message = "Année invalide." });

        var result = await _mediator.Send(new GetHistoriqueRhQuery(annee), ct);
        return Ok(result);
    }

    /// <summary>GET /api/conges/rh/pending-count — Demandes RH en attente (dashboard)</summary>
    [HttpGet("rh/pending-count")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> GetPendingRhCount(CancellationToken ct)
    {
        var count = await _mediator.Send(new GetPendingCongesRhCountQuery(), ct);
        return Ok(new { count });
    }

    /// <summary>GET /api/conges/employe/{employeId}/historique — Historique par année</summary>
    [HttpGet("employe/{employeId:guid}/historique")]
    public async Task<IActionResult> GetHistorique(
        Guid employeId,
        [FromQuery] int annee,
        CancellationToken ct)
    {
        var access = await ResolveEmployeAccessAsync(employeId, ct);
        if (access.Error is not null)
            return access.Error;
        employeId = access.CanonicalId;

        var result = await _mediator.Send(new GetHistoriqueQuery(employeId, annee), ct);
        return Ok(result);
    }

    /// <summary>GET /api/conges/disponibilite — Contrôle période interdite + quota avant demande</summary>
    [HttpGet("disponibilite")]
    public async Task<IActionResult> GetDisponibilite(
        [FromQuery] Guid employeId,
        [FromQuery] DateTime debut,
        [FromQuery] DateTime fin,
        CancellationToken ct)
    {
        var access = await ResolveEmployeAccessAsync(employeId, ct);
        if (access.Error is not null)
            return access.Error;
        employeId = access.CanonicalId;

        var result = await _mediator.Send(new GetDisponibiliteCongeQuery(employeId, debut, fin), ct);
        return Ok(result);
    }

    // ── Config ───────────────────────────────────────────────────────────────

    [HttpGet("config/periodes-interdites")]
    public async Task<IActionResult> GetPeriodesInterdites(
        [FromQuery] string? scopeKind,
        [FromQuery] string? scopeId,
        [FromQuery] Guid? employeId,
        [FromQuery] string? departmentId,
        [FromQuery] string? poleId,
        [FromQuery] string? celluleId,
        [FromQuery] string? serviceId,
        CancellationToken ct)
        => Ok(await _mediator.Send(new GetPeriodesInterditesQuery(
            scopeKind, scopeId, employeId, departmentId, poleId, celluleId, serviceId), ct));

    [HttpGet("config/periodes-interdites/scopes")]
    public async Task<IActionResult> GetPeriodesInterditesScopes(CancellationToken ct)
        => Ok(await _mediator.Send(new GetPeriodesInterditesScopesQuery(), ct));

    [HttpPut("config/periodes-interdites")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> UpdatePeriodesInterdites(
        [FromBody] UpdatePeriodesInterditesRequest body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdatePeriodesInterditesCommand(
                body.Mois ?? Array.Empty<int>(),
                body.UpdatedBy,
                body.ScopeKind,
                body.ScopeId),
            ct);
        return Ok(result);
    }

    [HttpDelete("config/periodes-interdites")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> DeletePeriodesInterditesScope(
        [FromQuery] string scopeKind,
        [FromQuery] string scopeId,
        CancellationToken ct)
    {
        var ok = await _mediator.Send(new DeletePeriodesInterditesScopeCommand(scopeKind, scopeId), ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("config/quotas-service")]
    public async Task<IActionResult> GetQuotasService([FromQuery] Guid superviseurId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetQuotasServiceQuery(superviseurId), ct));

    [HttpPut("config/quotas-service")]
    [Authorize(Roles = "Admin,RH,Superviseur,Manager")]
    public async Task<IActionResult> UpsertQuotaService(
        [FromBody] UpsertQuotaServiceRequest body,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpsertQuotaServiceCommand(
                body.ServiceId,
                body.MaxAbsentsSimultanes,
                body.SuperviseurId,
                body.ScopeKind), ct);
        return Ok(result);
    }

    [HttpGet("config/quotas-service/{serviceId}")]
    public async Task<IActionResult> GetQuotaService(string serviceId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetQuotaServiceByIdQuery(serviceId), ct));

    // ── Validation / Refus ───────────────────────────────────────────────────

    /// <summary>PUT /api/conges/{id}/valider-superviseur</summary>
    [HttpPut("{id:guid}/valider-superviseur")]
    [Authorize(Roles = "Admin,RH,Superviseur,Manager")]
    public async Task<IActionResult> ValiderSuperviseur(
        Guid id,
        [FromBody] ValiderCongeRequest? request,
        CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        var iam = await EnsureLeaveValidationIamAsync(ct);
        if (iam is not null)
            return iam;

        if (!IsPrivilegedHrOrAdmin())
        {
            var forbidden = await EnsureCanActOnDemandeAsync(id, actorId.Value, requireSuperviseurScope: true, ct);
            if (forbidden is not null)
                return forbidden;
        }

        await _mediator.Send(new ValiderCongeSuperviseurCommand(id, actorId.Value, request?.Commentaire), ct);
        return NoContent();
    }

    /// <summary>PUT /api/conges/{id}/valider-rh</summary>
    [HttpPut("{id:guid}/valider-rh")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> ValiderRh(
        Guid id,
        [FromBody] ValiderCongeRequest? request,
        CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        var iam = await EnsureLeaveValidationIamAsync(ct);
        if (iam is not null)
            return iam;

        await _mediator.Send(new ValiderCongeRhCommand(id, actorId.Value, request?.Commentaire), ct);
        return NoContent();
    }

    /// <summary>PUT /api/conges/{id}/valider — Compat (oriente selon statut)</summary>
    [HttpPut("{id:guid}/valider")]
    [Authorize(Roles = "Admin,RH,Superviseur,Manager")]
    public async Task<IActionResult> Valider(
        Guid id,
        [FromBody] ValiderCongeRequest? request,
        CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        var iam = await EnsureLeaveValidationIamAsync(ct);
        if (iam is not null)
            return iam;

        var demande = await _demandeRepo.GetByIdAsync(id, ct);
        if (demande is null)
            return NotFound();

        if (demande.Statut == StatutDemande.EnAttente)
        {
            if (!IsPrivilegedHrOrAdmin())
            {
                var forbidden = await EnsureCanActOnDemandeAsync(id, actorId.Value, requireSuperviseurScope: true, ct);
                if (forbidden is not null)
                    return forbidden;
            }
        }
        else if (demande.Statut == StatutDemande.EnAttenteRh && !IsPrivilegedHrOrAdmin())
        {
            return Forbid();
        }

        await _mediator.Send(new ValiderCongeCommand(id, actorId.Value, request?.Commentaire), ct);
        return NoContent();
    }

    /// <summary>PUT /api/conges/{id}/refuser — Refuser une demande</summary>
    [HttpPut("{id:guid}/refuser")]
    [Authorize(Roles = "Admin,RH,Superviseur,Manager")]
    public async Task<IActionResult> Refuser(
        Guid id,
        [FromBody] RefuserCongeRequest request,
        CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        var iam = await EnsureLeaveValidationIamAsync(ct);
        if (iam is not null)
            return iam;

        var demande = await _demandeRepo.GetByIdAsync(id, ct);
        if (demande is null)
            return NotFound();

        if (demande.Statut == StatutDemande.EnAttente)
        {
            if (!IsPrivilegedHrOrAdmin())
            {
                var forbidden = await EnsureCanActOnDemandeAsync(id, actorId.Value, requireSuperviseurScope: true, ct);
                if (forbidden is not null)
                    return forbidden;
            }
        }
        else if (!IsPrivilegedHrOrAdmin())
        {
            return Forbid();
        }

        await _mediator.Send(new RefuserCongeCommand(id, actorId.Value, request.Commentaire), ct);
        return NoContent();
    }

    /// <summary>PUT /api/conges/{id}/annuler — Annuler une demande (employé)</summary>
    [HttpPut("{id:guid}/annuler")]
    public async Task<IActionResult> Annuler(Guid id, [FromQuery] Guid employeId, CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return Unauthorized(new { message = "Sujet JWT manquant." });

        if (!IsPrivilegedHrOrAdmin())
        {
            var access = await ResolveEmployeAccessAsync(
                employeId == Guid.Empty ? actorId.Value : employeId,
                ct);
            if (access.Error is not null)
                return access.Error;
            employeId = access.CanonicalId;
        }

        await _mediator.Send(new AnnulerCongeCommand(id, employeId), ct);
        return NoContent();
    }

    // ── Solde ────────────────────────────────────────────────────────────────

    /// <summary>GET /api/conges/employe/{employeId}/solde — Consulter le solde</summary>
    [HttpGet("employe/{employeId:guid}/solde")]
    public async Task<IActionResult> GetSolde(
        Guid employeId,
        [FromQuery] int? annee,
        CancellationToken ct)
    {
        var access = await ResolveEmployeAccessAsync(employeId, ct);
        if (access.Error is not null)
            return access.Error;
        employeId = access.CanonicalId;

        var result = await _mediator.Send(new GetSoldeByEmployeQuery(employeId, annee), ct);
        return Ok(result);
    }

    private Guid? RequireActorId() => User.GetSubjectId();

    private async Task<IActionResult?> EnsureLeaveValidationIamAsync(CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "";
        var subject = RequireActorId() ?? Guid.Empty;
        var decision = await _policy.EvaluateAsync(
            new PolicyRequest(subject, role, "conges.validation.Validate", "platform"), ct);
        if (decision.Allowed)
            return null;
        return StatusCode(
            StatusCodes.Status403Forbidden,
            new { message = "La validation des congés n'est pas autorisée pour ce rôle." });
    }

    private bool IsPrivilegedHrOrAdmin()
    {
        var role = User.GetAuthRole();
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSuperviseurRole()
    {
        var role = User.GetAuthRole();
        return KyntusRoleNames.IsSuperviseur(role) || KyntusRoleNames.IsSupportManager(role);
    }

    private readonly record struct EmployeAccessResult(IActionResult? Error, Guid CanonicalId);

    /// <summary>
    /// JWT sub (Auth.SubjectId) peut diverger du Guid Planning/Directory.
    /// Autorise soi-même puis requête le Guid snapshot canonique.
    /// </summary>
    private async Task<EmployeAccessResult> ResolveEmployeAccessAsync(Guid requestedId, CancellationToken ct)
    {
        var actorId = RequireActorId();
        if (actorId is null)
            return new(Unauthorized(new { message = "Sujet JWT manquant." }), requestedId);

        if (requestedId == Guid.Empty)
            requestedId = actorId.Value;

        var email = User.GetEmail();
        var allowJwtStub = requestedId == actorId.Value;
        await _ensure.EnsureAsync(
            new EmployeSnapshotEnsureRequest(
                requestedId,
                email,
                User.GetAuthRole(),
                actorId.Value,
                allowJwtStub),
            ct);

        var requestedSnap = await _employeRepo.GetByEmployeIdAsync(requestedId, ct);
        var actorSnap = await _employeRepo.GetByEmployeIdOrEmailAsync(actorId.Value, email, ct);
        var isSelf = EmployeAccessResolver.IsSelf(requestedId, actorId.Value, email, requestedSnap, actorSnap);

        if (isSelf)
        {
            var canonical = EmployeAccessResolver.CanonicalId(requestedId, requestedSnap, actorSnap);
            return new(null, canonical);
        }

        if (IsPrivilegedHrOrAdmin())
            return new(null, requestedId);

        if (_rebac is null)
            return new(Forbid(), requestedId);

        try
        {
            var allowed = await _rebac.CanActOnAsync(actorId.Value, requestedId, ct);
            if (!allowed)
                return new(Forbid(), requestedId);
        }
        catch
        {
            return new(StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Contrôle d'autorisation temporairement indisponible.",
            }), requestedId);
        }

        return new(null, requestedId);
    }

    /// <summary>ReBAC obligatoire pour le périmètre superviseur (fail-closed).</summary>
    private async Task<IActionResult?> EnsureCanActOnDemandeAsync(
        Guid demandeId,
        Guid actorId,
        bool requireSuperviseurScope,
        CancellationToken ct)
    {
        if (!requireSuperviseurScope)
            return null;

        var demande = await _demandeRepo.GetByIdAsync(demandeId, ct);
        if (demande is null)
            return NotFound();

        if (_rebac is null)
            return Forbid();

        try
        {
            var allowed = await _rebac.CanActOnAsync(actorId, demande.EmployeId, ct);
            if (!allowed)
                return Forbid();
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Contrôle d'autorisation temporairement indisponible.",
            });
        }

        return null;
    }
}

public record ValiderCongeRequest(string? Commentaire);
public record RefuserCongeRequest(string Commentaire);
public record UpdatePeriodesInterditesRequest(
    IReadOnlyList<int>? Mois,
    Guid? UpdatedBy = null,
    string? ScopeKind = null,
    string? ScopeId = null);
public record UpsertQuotaServiceRequest(
    string ServiceId,
    int MaxAbsentsSimultanes,
    Guid SuperviseurId,
    string? ScopeKind = null);
