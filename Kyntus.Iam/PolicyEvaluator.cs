using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kyntus.Iam;

public sealed class PolicyEvaluator(
    IPermissionCatalog catalog,
    IRebacClient rebac,
    ILogger<PolicyEvaluator> logger,
    ISubjectPermissionOverrides? overrides = null,
    IAdminRestrictionProvider? adminRestrictions = null) : IPolicyEvaluator
{
    public async Task<PolicyDecision> EvaluateAsync(PolicyRequest request, CancellationToken ct = default)
    {
        var role = request.Role.Trim();
        var action = request.Action.Trim();

        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            // v60 — Tous les droits, sauf les fonctionnalités retirées par le Super Admin.
            if (adminRestrictions is not null && request.SubjectId != Guid.Empty)
            {
                var restrictions = await adminRestrictions.GetAsync(request.SubjectId, ct);
                if (restrictions.IsDenied(action))
                    return new PolicyDecision(false, "Retiré par le Super Admin");
            }
            return new PolicyDecision(true);
        }

        // Exceptions du compte (v19) : un retrait l'emporte sur le rôle ; un ajout ouvre l'écran
        // (et les ressources du compte lui-même), le périmètre des données restant celui du rôle.
        if (overrides is not null && request.SubjectId != Guid.Empty)
        {
            var exception = await overrides.GetOverrideAsync(request.SubjectId, action, ct);
            if (exception == false)
                return new PolicyDecision(false, "Retiré pour ce compte");
            // v66 — « Consulter » retiré au compte = écran entier retiré : Modifier / Valider / Configurer
            // ne peuvent plus passer par le rôle.
            var parts = AdminRestrictionRules.Split(action);
            if (exception is null
                && parts.HasValue
                && parts.Value.Verb != "View"
                && await overrides.GetOverrideAsync(request.SubjectId, parts.Value.Screen + ".View", ct) == false)
                return new PolicyDecision(false, "Écran retiré pour ce compte");
            if (exception == true
                && (string.IsNullOrWhiteSpace(request.ResourceId)
                    || (Guid.TryParse(request.ResourceId, out var own) && own == request.SubjectId)))
                return new PolicyDecision(true);
        }

        var scopes = new[] { "Global", "Self", "Subtree", "Pole", "Cellule", "Service" };
        foreach (var scope in scopes)
        {
            if (!await catalog.RoleHasActionAsync(role, action, scope, ct))
                continue;

            // Droit d'écran (matrice Admin) : pas de ressource = le rôle a l'action.
            if (scope == "Global" || string.IsNullOrWhiteSpace(request.ResourceId))
                return new PolicyDecision(true);

            if (scope == "Self"
                && Guid.TryParse(request.ResourceId, out var rid)
                && rid == request.SubjectId)
                return new PolicyDecision(true);

            if (scope == "Subtree"
                && Guid.TryParse(request.ResourceId, out var targetId)
                && await rebac.IsDescendantAsync(request.SubjectId, targetId, ct))
                return new PolicyDecision(true);

            if (scope is "Pole" or "Cellule" or "Service")
            {
                var kind = scope switch
                {
                    "Pole" => "ChefDeProjet",
                    "Cellule" => "Superviseur",
                    _ => "ReferentTechnique",
                };
                var nodes = await rebac.GetManagedNodeIdsAsync(request.SubjectId, kind, ct);
                if (nodes.Contains(request.ResourceId, StringComparer.OrdinalIgnoreCase))
                    return new PolicyDecision(true);
            }
        }

        logger.LogDebug("Policy denied {Role} {Action} on {ResourceType}/{ResourceId}", role, action, request.ResourceType, request.ResourceId);
        return new PolicyDecision(false, "Permission denied");
    }
}

public static class IamServiceCollectionExtensions
{
    public static IServiceCollection AddKyntusIam(this IServiceCollection services)
    {
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        return services;
    }
}
