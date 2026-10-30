using Conge.Application.Abstractions;
using Conge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conge.Infrastructure.Services;

/// <summary>
/// Circuit de validation lu dans la base Congé (table conge_workflow_config).
///
/// Défaut v9 : pas de deuxième étape RH — le superviseur décide seul. Un Admin peut
/// rétablir l'ancien circuit depuis l'écran de configuration, sans redéploiement.
/// En cas d'erreur de lecture, on conserve le circuit standard (superviseur seul)
/// plutôt que de bloquer les validations.
/// </summary>
public sealed class CongeWorkflowConfigPolicy(
    ICongeWorkflowConfigRepository repo,
    ILogger<CongeWorkflowConfigPolicy> logger) : ILeaveWorkflowPolicy
{
    public async Task<bool> IsRhValidationRequiredAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await repo.GetOrCreateAsync(ct);
            return config.EtapeRhRequise;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Configuration du circuit congés illisible — superviseur seul décideur.");
            return false;
        }
    }
}
