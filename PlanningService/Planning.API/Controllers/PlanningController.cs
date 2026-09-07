using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Planning.Application.DTOs.Planning;
using Planning.Application.Abstractions;
using Planning.Application.Exceptions;
using Planning.Application.PilotPlanning;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/planning")]
public class PlanningController(
    IPlanningService planningService,
    IUserService userService,
    IPlanningPendingRequestsAlertService pendingAlerts,
    IPolicyEvaluator policyEvaluator,
    IRebacClient rebacClient,
    IPlanningEmailSender planningEmails) : ControllerBase
{
    private readonly IPlanningService _planningService = planningService;
    private readonly IUserService _userService = userService;
    private readonly IPlanningPendingRequestsAlertService _pendingAlerts = pendingAlerts;
    private readonly IPolicyEvaluator _policyEvaluator = policyEvaluator;
    private readonly IRebacClient _rebacClient = rebacClient;
    private readonly IPlanningEmailSender _planningEmails = planningEmails;

    private const string CapShiftConfigure = "planning.shift-config.Configure";
    private const string CapGenerate = "planning.generate.Act";
    private const string CapPublish = "planning.publish.Validate";

    private async Task<int?> ResolvePlanningUserIdAsync()
    {
        var authUserId = User.GetAuthUserId();
        if (authUserId is not > 0) return null;
        var user = await _userService.GetUserByAuthIdAsync(authUserId.Value);
        return user?.Id;
    }

    private async Task<(int Id, Guid Guid)?> ResolvePlanningUserAsync()
    {
        var authUserId = User.GetAuthUserId();
        if (authUserId is not > 0) return null;
        var user = await _userService.GetUserByAuthIdAsync(authUserId.Value);
        return user is null ? null : (user.Id, user.Guid);
    }

    private async Task<IActionResult?> ForbidOutsidePerimeterAsync(int subServiceId)
    {
        var viewerId = await ResolvePlanningUserIdAsync();
        if (viewerId is not > 0) return null;
        try
        {
            await _planningService.EnsureViewerCanManageSubServiceAsync(viewerId.Value, subServiceId);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private async Task<IActionResult?> ForbidUnlessAsync(string action, CancellationToken ct = default)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var subjectId = User.GetSubjectId() ?? Guid.Empty;
        var decision = await _policyEvaluator.EvaluateAsync(
            new PolicyRequest(subjectId, role, action, "platform"), ct);
        return decision.Allowed ? null : Forbid();
    }

    // ----------------------------------------------------
    // CRUD PLANNING
    // ----------------------------------------------------

    // GET api/planning/subservice/3
    [HttpGet("subservice/{subServiceId}")]
    public async Task<IActionResult> GetBySubService(int subServiceId)
    {
        var result = await _planningService.GetPlanningsBySubServiceAsync(subServiceId);
        return Ok(result);
    }

    // GET api/planning/5
    [HttpGet("{id:int}")]  // ? UNIQUEMENT les entiers
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _planningService.GetPlanningByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    // GET api/planning/equipe?authUserId=13
    [HttpGet("equipe")]
    public async Task<IActionResult> GetEquipePlannings([FromQuery] int authUserId)
    {
        // Always 200: empty list = no published plannings / no perimeter (not a missing route).
        var result = await _planningService.GetEquipePlanningsByAuthUserIdAsync(authUserId);
        return Ok(result);
    }

    // POST api/planning
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWeeklyPlanningDto dto)
    {
        try
        {
            var result = await _planningService.CreatePlanningAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("existe d�j�"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // DELETE api/planning/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _planningService.DeletePlanningAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ----------------------------------------------------
    // CONFIG SHIFTS (template + snapshot)
    // ----------------------------------------------------

    // POST api/planning/config — template si WeekCode vide
    [HttpPost("config")]
    public async Task<IActionResult> SaveShiftConfig([FromBody] SaveShiftConfigDto dto)
    {
        if (await ForbidUnlessAsync(CapShiftConfigure) is { } denied) return denied;
        if (await ForbidOutsidePerimeterAsync(dto.SubServiceId) is { } outside) return outside;
        try
        {
            var result = await _planningService.SaveShiftConfigAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    // GET api/planning/config/status — avancement modèles (rouge/vert)
    [HttpGet("config/status")]
    public async Task<IActionResult> GetShiftConfigStatus()
    {
        return Ok(await _planningService.GetShiftConfigStatusAsync());
    }

    // GET api/planning/config/3 — modèle permanent
    [HttpGet("config/{subServiceId:int}")]
    public async Task<IActionResult> GetShiftTemplate(int subServiceId)
    {
        var result = await _planningService.GetShiftTemplateAsync(subServiceId);
        return result == null ? NotFound() : Ok(result);
    }

    // GET api/planning/config/3/2026-W10 — snapshot semaine
    [HttpGet("config/{subServiceId:int}/{weekCode}")]
    public async Task<IActionResult> GetShiftConfig(int subServiceId, string weekCode)
    {
        var result = await _planningService.GetShiftConfigAsync(subServiceId, weekCode);
        return result == null ? NotFound() : Ok(result);
    }

    // GET api/planning/config/weekly-modes/{subServiceId}/{weekCode}?weekStartDate=yyyy-MM-dd
    [HttpGet("config/weekly-modes/{subServiceId:int}/{weekCode}")]
    public async Task<IActionResult> GetWeeklyShiftModes(
        int subServiceId,
        string weekCode,
        [FromQuery] DateOnly weekStartDate)
    {
        try
        {
            var result = await _planningService.GetWeeklyShiftModePlanAsync(
                subServiceId, weekCode, weekStartDate);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT api/planning/config/weekly-modes
    [HttpPut("config/weekly-modes")]
    public async Task<IActionResult> SaveWeeklyShiftModes([FromBody] SaveWeeklyShiftModePlanDto dto)
    {
        try
        {
            if (dto.ActorUserId is null or <= 0)
            {
                var authUserId = User.GetAuthUserId();
                if (authUserId is > 0)
                {
                    var user = await _userService.GetUserByAuthIdAsync(authUserId.Value);
                    if (user != null)
                        dto.ActorUserId = user.Id;
                }
            }

            var result = await _planningService.SaveWeeklyShiftModePlanAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ----------------------------------------------------
    // VALIDATION / AUTO-GÉNÉRATION
    // ----------------------------------------------------

    [HttpGet("week/{weekCode}")]
    public async Task<IActionResult> GetWeekOverview(string weekCode, [FromQuery] int? viewerUserId = null)
    {
        // RH/Admin voient toutes les cellules, indépendamment d'un rôle Planning
        // local éventuellement désynchronisé. Les autres rôles restent filtrés.
        var role = User.GetAuthRole();
        var seesAll = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
        var authenticatedViewer = seesAll ? null : await ResolvePlanningUserAsync();

        PlanningWeekListDto result;
        if (!seesAll
            && authenticatedViewer is { } supervisor
            && (string.Equals(role, "Superviseur", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<string> cells;
            try
            {
                cells = await _rebacClient.GetManagedNodeIdsAsync(
                    supervisor.Guid,
                    "Superviseur");
            }
            catch
            {
                // En cas d'indisponibilité Directory, ne jamais montrer la cellule
                // d'appartenance ni un ancien périmètre local.
                cells = [];
            }

            result = await _planningService.GetWeekOverviewForManagedCellsAsync(
                weekCode,
                supervisor.Id,
                cells);
        }
        else
        {
            result = await _planningService.GetWeekOverviewAsync(
                weekCode,
                seesAll ? null : authenticatedViewer?.Id ?? viewerUserId);
        }
        return Ok(result);
    }

    [HttpPost("{id:int}/consult")]
    public async Task<IActionResult> Consult(int id, [FromQuery] int userId)
    {
        try
        {
            await _planningService.RecordConsultationAsync(id, userId);
            return Ok(new { consulted = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("auto-generate-settings")]
    public async Task<IActionResult> GetAutoGenerateSettings()
    {
        return Ok(await _planningService.GetAutoGenerateSettingsAsync());
    }

    [HttpPut("auto-generate-settings")]
    public async Task<IActionResult> SaveAutoGenerateSettings(
        [FromBody] AutoGenerateSettingsDto dto,
        [FromQuery] int? updatedByUserId = null)
    {
        if (await ForbidUnlessAsync(CapShiftConfigure) is { } denied) return denied;
        try
        {
            var result = await _planningService.SaveAutoGenerateSettingsAsync(dto, updatedByUserId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("auto-generate-settings/test-email")]
    public async Task<IActionResult> SendTestPlanningEmail(
        [FromBody] TestPlanningEmailRequest? body,
        CancellationToken ct)
    {
        if (await ForbidUnlessAsync(CapShiftConfigure) is { } denied) return denied;
        try
        {
            var settings = await _planningService.GetAutoGenerateSettingsAsync();
            var viewerId = await ResolvePlanningUserIdAsync();
            var user = viewerId is > 0 ? await _userService.GetUserByIdAsync(viewerId.Value) : null;

            var to = PlanningSenderAddress.NormalizeEmailOrNull(body?.To)
                ?? PlanningSenderAddress.NormalizeEmailOrNull(settings.SenderFromEmail)
                ?? PlanningSenderAddress.NormalizeEmailOrNull(user?.Email)
                ?? PlanningSenderAddress.NormalizeEmailOrNull(User.GetEmail());
            if (to is null)
                return BadRequest(new { message = "Indiquez une adresse de test ou renseignez l’adresse RH « De : »." });

            await _planningEmails.SendRequiredHtmlAsync(
                to,
                "Test mail Kyntus Planning",
                "<p>Envoi OK — Kyntus Planning.</p>",
                ct);
            return Ok(new { sent = true, to });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    [HttpPost("week/{weekCode}/auto-generate")]
    public async Task<IActionResult> AutoGenerateWeek(
        string weekCode,
        [FromQuery] bool force = false)
    {
        if (await ForbidUnlessAsync(CapGenerate) is { } denied) return denied;
        try
        {
            var actorId = await ResolvePlanningUserIdAsync();
            var result = await _planningService.AutoGenerateWeekAsync(weekCode, force, actorId);
            try
            {
                await _pendingAlerts.SendValidationRemindersAsync(weekCode);
            }
            catch
            {
                // rappel non bloquant
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    /// <summary>Synthèse des demandes switch / exceptionnelles non traitées (scoped viewer).</summary>
    [HttpGet("pending-requests-summary")]
    public async Task<IActionResult> GetPendingRequestsSummary([FromQuery] int? authUserId = null)
    {
        int? viewerId = null;
        if (authUserId is > 0)
        {
            var user = await _userService.GetUserByAuthIdAsync(authUserId.Value);
            viewerId = user?.Id;
        }

        var summary = await _pendingAlerts.GetSummaryAsync(viewerId);
        return Ok(summary);
    }

    // ----------------------------------------------------
    // GÉNÉRATION
    // ----------------------------------------------------

    // POST api/planning/generate
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GeneratePlanningDto dto)
    {
        if (await ForbidUnlessAsync(CapGenerate) is { } denied) return denied;
        try
        {
            var result = await _planningService.GeneratePlanningAsync(dto);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    // POST api/planning/generate-from-config
    [HttpPost("generate-from-config")]
    public async Task<IActionResult> GenerateFromConfig(
        [FromBody] GeneratePlanningFromConfigDto dto)
    {
        if (await ForbidUnlessAsync(CapGenerate) is { } denied) return denied;
        if (await ForbidOutsidePerimeterAsync(dto.SubServiceId) is { } outside) return outside;
        try
        {
            var result = await _planningService.GeneratePlanningFromConfigAsync(dto);
            return Ok(result);
        }
        catch (SupervisorModesPendingException ex)
        {
            var authUserId = User.GetAuthUserId();
            if (authUserId is > 0)
            {
                var user = await _userService.GetUserByAuthIdAsync(authUserId.Value);
                if (user != null)
                {
                    await _planningService.NotifyModeConfigPendingAsync(
                        user.Id,
                        authUserId.Value,
                        dto.WeekCode ?? string.Empty,
                        string.Empty,
                        ex.Message);
                }
            }

            return BadRequest(new { message = ex.Message });
        }
        catch (PlanningValidationException ex)
        {
            return BadRequest(new { message = ex.Message, anomalies = ex.Anomalies });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    // ----------------------------------------------------
    // PUBLICATION + OVERRIDE
    // ----------------------------------------------------

    // POST api/planning/5/publish
    [HttpPost("{id}/publish")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> Publish(int id, [FromQuery] int validatorId)
    {
        if (await ForbidUnlessAsync(CapPublish) is { } denied) return denied;
        try
        {
            var result = await _planningService.PublishPlanningAsync(id, validatorId);
            return Ok(result);
        }
        catch (PlanningValidationException ex)
        {
            return BadRequest(new { message = ex.Message, anomalies = ex.Anomalies });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT api/planning/override
    [HttpPut("override")]
    public async Task<IActionResult> OverrideShift([FromBody] OverrideShiftDto dto)
    {
        try
        {
            var result = await _planningService.OverrideShiftAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ----------------------------------------------------
    // GROUPES SAMEDI
    // ----------------------------------------------------

    // POST api/planning/saturday-groups/auto/3
    [HttpPost("saturday-groups/auto/{subServiceId}")]
    public async Task<IActionResult> AutoAssignGroups(int subServiceId)
    {
        await _planningService.AutoAssignSaturdayGroupsAsync(subServiceId);
        return Ok(new { message = "Groupes samedi assign�s automatiquement." });
    }

    // POST api/planning/saturday-group
    [HttpPost("saturday-group")]
    public async Task<IActionResult> SetSaturdayGroup([FromBody] SetSaturdayGroupDto dto)
    {
        await _planningService.SetSaturdayGroupAsync(dto);
        return Ok(new { message = "Groupe samedi configuré." });
    }

    // PUT api/planning/saturday-mode
    [HttpPut("saturday-mode")]
    public async Task<IActionResult> SetSaturdayWorkMode([FromBody] SetSaturdayWorkModeDto dto)
    {
        try
        {
            await _planningService.SetSaturdayWorkModeAsync(dto);
            return Ok(new { message = "Mode samedi configuré." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT api/planning/special-case
    [HttpPut("special-case")]
    public async Task<IActionResult> SetEmployeeSpecialCase([FromBody] SetEmployeeSpecialCaseDto dto)
    {
        try
        {
            await _planningService.SetEmployeeSpecialCaseAsync(dto);
            return Ok(new { message = "Cas particulier enregistré." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PUT api/planning/plateau-training
    [HttpPut("plateau-training")]
    public async Task<IActionResult> SetEmployeePlateauTraining([FromBody] SetEmployeePlateauTrainingDto dto)
    {
        try
        {
            await _planningService.SetEmployeePlateauTrainingAsync(dto);
            return Ok(new { message = "Ticket formation plateau enregistré." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET api/planning/saturday-balance/3
    [HttpGet("saturday-balance/{subServiceId:int}")]
    public async Task<IActionResult> GetSaturdayBalance(int subServiceId)
    {
        var result = await _planningService.GetSaturdayBalanceAsync(subServiceId);
        return Ok(result);
    }

    // POST api/planning/saturday-balance/3/notify?authUserId=12
    [HttpPost("saturday-balance/{subServiceId:int}/notify")]
    public async Task<IActionResult> NotifySaturdayImbalance(
        int subServiceId,
        [FromQuery] int authUserId)
    {
        if (authUserId <= 0)
            return BadRequest(new { message = "authUserId requis." });
        var count = await _planningService.NotifySaturdayImbalanceAsync(subServiceId, authUserId);
        return Ok(new { notified = count });
    }

    // GET api/planning/saturday-groups/3
    [HttpGet("saturday-groups/{subServiceId}")]
    public async Task<IActionResult> GetSaturdayGroups(int subServiceId)
    {
        var result = await _planningService.GetSaturdayGroupsAsync(subServiceId);
        return Ok(result);
    }

    // ----------------------------------------------------
    // VUE EMPLOY�
    // ----------------------------------------------------
    // VUE EMPLOY�
    // ----------------------------------------------------

    // ? DOIT �tre AVANT my/{weekCode}
    [HttpGet("my/current")]
    public async Task<IActionResult> GetMyCurrentPlanning([FromQuery] int userId)
    {
        var user = await _userService.GetUserByAuthIdAsync(userId);
        if (user is null)
            return NotFound(new { message = "Utilisateur introuvable." });

        // Pas de planning publié cette semaine ≠ erreur : 204 pour éviter un 404 bruyant côté UI.
        var result = await _planningService.GetMyCurrentPlanningAsync(user.Id);
        return result == null ? NoContent() : Ok(result);
    }

    [HttpGet("my/history")]
    public async Task<IActionResult> GetMyHistory([FromQuery] int userId)
    {
        var user = await _userService.GetUserByAuthIdAsync(userId);
        if (user is null)
            return Ok(new List<object>());

        var result = await _planningService.GetMyPlanningHistoryAsync(user.Id);
        return Ok(result);
    }

    /// <summary>Historique des plannings d'un agent (id planning interne) — superviseur / RH.</summary>
    [HttpGet("agent/{planningUserId:int}/history")]
    public async Task<IActionResult> GetAgentPlanningHistory(
        int planningUserId,
        [FromQuery] string? period = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var (rangeFrom, rangeTo) = Planning.Application.Common.PeriodRange.Resolve(
            period ?? "thisMonth", from, to);
        var result = await _planningService.GetAgentPlanningHistoryAsync(
            planningUserId, rangeFrom, rangeTo);
        return Ok(result);
    }

    [HttpGet("my/{weekCode}")]
    public async Task<IActionResult> GetMyPlanning(string weekCode, [FromQuery] int userId)
    {
        var user = await _userService.GetUserByAuthIdAsync(userId);
        if (user is null)
            return NotFound();

        var result = await _planningService.GetMyPlanningAsync(user.Id, weekCode);
        return result == null ? NotFound() : Ok(result);
    }
    // ----------------------------------------------------
    // NOTIFICATIONS DE PUBLICATION (persistées)
    // ----------------------------------------------------

    // GET api/planning/notifications?userId={authUserId}
    [HttpGet("notifications")]
    public async Task<IActionResult> GetMyNotifications([FromQuery] int userId)
    {
        var result = await _planningService.GetMyNotificationsAsync(userId);
        return Ok(result);
    }

    // POST api/planning/notifications/{id}/read?userId={authUserId}
    [HttpPost("notifications/{id:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id, [FromQuery] int userId)
    {
        await _planningService.MarkNotificationReadAsync(id, userId);
        return NoContent();
    }

    // POST api/planning/notifications/read-all?userId={authUserId}
    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead([FromQuery] int userId)
    {
        await _planningService.MarkAllNotificationsReadAsync(userId);
        return NoContent();
    }

    // POST api/planning/comment
    [HttpPost("comment")]
    public async Task<IActionResult> SaveComment([FromBody] SavePlanningCommentDto dto)
    {
        try
        {
            var result = await _planningService.SaveCommentAsync(dto);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // DELETE api/planning/5/comment/3
    [HttpDelete("{planningId}/comment/{userId}")]
    public async Task<IActionResult> DeleteComment(int planningId, int userId)
    {
        await _planningService.DeleteCommentAsync(planningId, userId);
        return NoContent();
    }

    // GET api/planning/5/comments
    [HttpGet("{planningId}/comments")]
    public async Task<IActionResult> GetComments(int planningId)
    {
        var result = await _planningService.GetCommentsAsync(planningId);
        return Ok(result);
    }

    [HttpPut("override-break")]
    public async Task<IActionResult> OverrideBreak(
    [FromBody] OverrideBreakDto dto)
    {
        try
        {
            var result = await _planningService.OverrideBreakAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    // GET /api/planning/saturday-history/{subServiceId}/ytd?year=2026
    // (déclaré avant {weekCode} pour éviter que "ytd" soit capturé comme code semaine)
    [HttpGet("saturday-history/{subServiceId}/ytd")]
    public async Task<IActionResult> GetSaturdayYtd(int subServiceId, [FromQuery] int? year)
        => Ok(await _planningService.GetSaturdayYtdAsync(
            subServiceId,
            year ?? DateTime.UtcNow.Year));

    // GET /api/planning/saturday-history/{subServiceId}/{weekCode}
    [HttpGet("saturday-history/{subServiceId}/{weekCode}")]
    public async Task<IActionResult> GetSaturdayHistory(
        int subServiceId, string weekCode)
        => Ok(await _planningService.GetSaturdayHistoryAsync(subServiceId, weekCode));

    // POST /api/planning/saturday-history
    [HttpPost("saturday-history")]
    public async Task<IActionResult> SaveSaturdayHistory(
        [FromBody] SetSaturdayHistoryDto dto)
    {
        await _planningService.SaveSaturdayHistoryAsync(dto, true);
        return Ok(new { message = "Historique samedi sauvegardé." });
    }
    // PUT api/planning/override-saturday
    [HttpPut("override-saturday")]
    public async Task<IActionResult> OverrideSaturdayShift(
        [FromBody] OverrideSaturdayDto dto)
    {
        try
        {
            var result = await _planningService.OverrideSaturdayShiftAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    [HttpDelete("{planningId}/saturday/{userId}/off")]
    public async Task<IActionResult> SetSaturdayOff(int planningId, int userId)
    {
        try
        {
            await _planningService.SetSaturdayOffAsync(planningId, userId);
            return Ok(new { message = "Samedi mis � OFF." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = FormatClientError(ex) });
        }
    }

    /// <summary>
    /// EF/Npgsql wraps the real Postgres error. Surface the innermost message
    /// so the UI is not stuck on « See the inner exception for details ».
    /// </summary>
    internal static string FormatClientError(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;

        var leaf = current.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(leaf) || ReferenceEquals(current, ex))
            return ex.Message;

        if (ex.Message.Contains("See the inner exception", StringComparison.OrdinalIgnoreCase))
            return leaf;

        if (ex.Message.Contains(leaf, StringComparison.Ordinal))
            return ex.Message;

        return $"{ex.Message} ({leaf})";
    }
}