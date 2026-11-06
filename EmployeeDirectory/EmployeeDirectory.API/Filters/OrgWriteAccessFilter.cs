using EmployeeDirectory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EmployeeDirectory.API.Filters;

/// <summary>
/// Réorganisation 2026 — écritures sur la structure (pôles, cellules, services) et sur les
/// affectations de responsables réservées aux comptes habilités :
/// rôles Admin / RH, appel inter-services authentifié par clé interne, ou rôle disposant de la
/// permission IAM <c>users:manage</c> (la même que Planning exige pour créer / modifier un employé,
/// flux qui appelle ces routes). Un utilisateur connecté sans ces droits reçoit 403.
/// </summary>
public sealed class OrgWriteAccessAttribute() : TypeFilterAttribute(typeof(OrgWriteAccessFilter));

public sealed class OrgWriteAccessFilter(
    IIamReadService iam,
    ILogger<OrgWriteAccessFilter> logger) : IAsyncActionFilter
{
    public const string ManageUsersAction = "users:manage";
    public const string ReaffecterAction = "rh.affectations.Act";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (http.Items.TryGetValue("IsInternalService", out var internalFlag) && internalFlag is true)
        {
            await next();
            return;
        }

        var user = http.User;
        var subjectId = Kyntus.Identity.Jwt.KyntusClaimsPrincipalExtensions.GetSubjectId(user);
        var role = Kyntus.Identity.Jwt.KyntusClaimsPrincipalExtensions.GetAuthRole(user);
        if (user.IsInRole("Admin") || user.IsInRole("RH") || IsAdminOrHr(role))
        {
            await next();
            return;
        }

        if (subjectId is Guid subject && subject != Guid.Empty && !string.IsNullOrWhiteSpace(role))
        {
            try
            {
                if (await iam.EvaluateAsync(subject, role, ManageUsersAction, "users", null, http.RequestAborted))
                {
                    await next();
                    return;
                }

                // v29 : un responsable autorisé à réaffecter (« Réaffecter les agents » au niveau
                // Modifier) peut changer les affectations (pôle, cellule, service) — pas la structure.
                if (http.Request.Path.Value?.Contains("/assignments/", StringComparison.OrdinalIgnoreCase) == true
                    && await iam.EvaluateAsync(subject, role, ReaffecterAction, "platform", null, http.RequestAborted))
                {
                    await next();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Évaluation IAM indisponible pour {Role} — accès refusé par défaut", role);
            }
        }

        logger.LogWarning(
            "Écriture organisation refusée : {Method} {Path} (rôle {Role})",
            http.Request.Method,
            http.Request.Path,
            role ?? "inconnu");
        context.Result = new ObjectResult(new { error = "Action réservée aux rôles Admin / RH." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    private static bool IsAdminOrHr(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
}
