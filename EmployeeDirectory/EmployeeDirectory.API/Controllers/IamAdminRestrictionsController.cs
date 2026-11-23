using System.Security.Claims;
using EmployeeDirectory.Application.Iam;
using Kyntus.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeDirectory.API.Controllers;

/// <summary>
/// Droits des administrateurs (v60) : le Super Admin retire ou rend chaque fonctionnalité à tous les
/// administrateurs ou à un administrateur précis. Seul le Super Admin lit et modifie ces réglages ;
/// chaque administrateur peut seulement lire ses propres restrictions (« me »), ce que font aussi les
/// autres services pour les appliquer.
/// </summary>
[ApiController]
[Route("api/iam/admin-restrictions")]
[Authorize]
public class IamAdminRestrictionsController(IIamAdminRestrictionService restrictions) : ControllerBase
{
    private const string ReserveSuperAdmin = "Réservé au Super Admin : seul le Super Admin gère les droits des administrateurs.";

    /// <summary>Restrictions de l'appelant (vide pour tout autre rôle que Admin et pour le Super Admin).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<AdminRestrictionsMeDto>> Me(CancellationToken ct)
    {
        if (KyntusAdminClaims.IsSuperAdmin(User))
            return Ok(new AdminRestrictionsMeDto(true, false, []));
        var subjectId = KyntusAdminClaims.SubjectId(User);
        if (!KyntusAdminClaims.IsAdmin(User) || subjectId is null)
            return Ok(new AdminRestrictionsMeDto(false, false, []));
        var denied = await restrictions.GetDeniedAsync(subjectId.Value, ct);
        return Ok(new AdminRestrictionsMeDto(false, denied.Count > 0, denied));
    }

    /// <summary>Catalogue des fonctionnalités réglables (toutes les rubriques × tous les verbes).</summary>
    [HttpGet("catalog")]
    [Authorize(Roles = "Admin")]
    public ActionResult<AdminRestrictionCatalogDto> Catalog()
    {
        if (!KyntusAdminClaims.IsSuperAdmin(User)) return Refus();
        return Ok(AdminAccessCatalog.Catalog());
    }

    /// <summary>Comptes ayant des réglages propres (et la ligne « tous les administrateurs »).</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<AdminRestrictionSummaryDto>>> Summary(CancellationToken ct)
    {
        if (!KyntusAdminClaims.IsSuperAdmin(User)) return Refus();
        return Ok(await restrictions.SummaryAsync(ct));
    }

    /// <summary>Réglages d'un administrateur (Guid vide = tous les administrateurs).</summary>
    [HttpGet("{subjectId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AdminRestrictionsDto>> Get(Guid subjectId, CancellationToken ct)
    {
        if (!KyntusAdminClaims.IsSuperAdmin(User)) return Refus();
        return Ok(await restrictions.GetDetailAsync(subjectId, ct));
    }

    [HttpPut("{subjectId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AdminRestrictionsDto>> Update(
        Guid subjectId,
        [FromBody] AdminRestrictionsUpdateRequest body,
        CancellationToken ct)
    {
        if (!KyntusAdminClaims.IsSuperAdmin(User)) return Refus();
        if (subjectId != AdminRestrictionRules.AllAdmins && subjectId == KyntusAdminClaims.SubjectId(User))
            return BadRequest(new { error = "Le Super Admin garde tous les droits : il ne peut pas se restreindre.", message = "Le Super Admin garde tous les droits : il ne peut pas se restreindre." });
        try
        {
            var auteur = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? User.Identity?.Name;
            return Ok(await restrictions.UpdateAsync(subjectId, body, auteur, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, message = ex.Message });
        }
    }

    private ObjectResult Refus() =>
        StatusCode(StatusCodes.Status403Forbidden, new { code = "SUPER_ADMIN_ONLY", error = ReserveSuperAdmin, message = ReserveSuperAdmin });
}

/// <summary>Restrictions de l'administrateur appelant et garde des écritures d'« Accès par rôle / compte ».</summary>
internal static class IamCaller
{
    /// <summary>Fonctionnalités retirées à l'appelant (vide sauf administrateur restreint).</summary>
    public static async Task<IReadOnlyList<string>> DeniedAsync(
        ClaimsPrincipal user,
        IIamAdminRestrictionService restrictions,
        CancellationToken ct)
    {
        if (!KyntusAdminClaims.IsAdmin(user) || KyntusAdminClaims.IsSuperAdmin(user))
            return [];
        var subjectId = KyntusAdminClaims.SubjectId(user);
        if (subjectId is null)
            return [];
        return await restrictions.GetDeniedAsync(subjectId.Value, ct);
    }

    /// <summary>
    /// Hiérarchie : seul le Super Admin règle le rôle Admin ; un administrateur restreint ne peut ni
    /// accorder ni retirer une rubrique qui lui a été retirée (pas d'auto-attribution de droits).
    /// </summary>
    public static async Task<ObjectResult?> GuardAsync(
        ClaimsPrincipal user,
        IIamAdminRestrictionService restrictions,
        IEnumerable<(string? Role, string? Action)> writes,
        CancellationToken ct)
    {
        if (KyntusAdminClaims.IsSuperAdmin(user))
            return null;
        var liste = writes.ToList();
        if (liste.Any(w => string.Equals(PlatformAccessCatalog.NormalizeRole(w.Role ?? string.Empty), KyntusAdminClaims.AdminRole, StringComparison.OrdinalIgnoreCase)))
            return Forbidden("Seul le Super Admin peut modifier les droits du rôle Admin.");
        var denied = await DeniedAsync(user, restrictions, ct);
        if (denied.Count > 0 && liste.Any(w => !AdminAccessCatalog.ActionAllowed(denied, w.Action)))
            return Forbidden("Vous ne pouvez pas accorder ni retirer une fonctionnalité qui vous a été retirée par le Super Admin.");
        return null;
    }

    private static ObjectResult Forbidden(string message) =>
        new(new { code = "ADMIN_RESTRICTED", error = message, message }) { StatusCode = StatusCodes.Status403Forbidden };
}
