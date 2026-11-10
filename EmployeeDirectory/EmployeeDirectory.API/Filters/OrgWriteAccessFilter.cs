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
    /// <summary>v39 : « Organisation » au niveau Modifier dans Accès par rôle / par compte.</summary>
    public const string OrganisationAgirAction = "admin.organisation.Act";

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

                // v39 : le droit donné dans « Accès par rôle » (Organisation, Modifier) suffit : l'écran
                // et le serveur suivent la même règle.
                if (await iam.EvaluateAsync(subject, role, OrganisationAgirAction, "platform", null, http.RequestAborted))
                {
                    await next();
                    return;
                }

                // v29 : un responsable autorisé à réaffecter (« Réaffecter les agents » au niveau
                // Modifier) peut changer les affectations (pôle, cellule, service) — pas la structure.
                var chemin = http.Request.Path.Value ?? string.Empty;
                if ((chemin.Contains("/assignments/", StringComparison.OrdinalIgnoreCase)
                     || chemin.Contains("/clear-org-placement", StringComparison.OrdinalIgnoreCase))
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
        context.Result = new ObjectResult(new { error = "Action non autorisée : droit « Organisation » (Modifier) requis dans Accès par rôle." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    private static bool IsAdminOrHr(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// v39 — lecture des écrans d'organisation réservés (archives, couverture) : Admin / RH, appel interne,
/// ou droit IAM « Organisation » (Voir ou Modifier) donné dans Accès par rôle / par compte.
/// </summary>
public sealed class OrgReadAccessAttribute() : TypeFilterAttribute(typeof(OrgReadAccessFilter));

public sealed class OrgReadAccessFilter(
    IIamReadService iam,
    ILogger<OrgReadAccessFilter> logger) : IAsyncActionFilter
{
    public const string OrganisationVoirAction = "admin.organisation.View";

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
        if (user.IsInRole("Admin") || user.IsInRole("RH")
            || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        if (subjectId is Guid subject && subject != Guid.Empty && !string.IsNullOrWhiteSpace(role))
        {
            try
            {
                if (await iam.EvaluateAsync(subject, role, OrganisationVoirAction, "platform", null, http.RequestAborted)
                    || await iam.EvaluateAsync(subject, role, OrgWriteAccessFilter.OrganisationAgirAction, "platform", null, http.RequestAborted))
                {
                    await next();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Évaluation IAM indisponible pour {Role} — lecture refusée par défaut", role);
            }
        }

        context.Result = new ObjectResult(new { error = "Accès non autorisé : droit « Organisation » requis dans Accès par rôle." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
