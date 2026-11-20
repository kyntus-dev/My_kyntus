using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v61 — Toutes les 15 minutes, aligne les plannings de la semaine en cours et de la suivante sur les
/// agents réels des cellules : agents sortis retirés des jours à venir, agents arrivés ajoutés
/// (y compris dans « Mon planning » de l'agent, sans attendre qu'un responsable ouvre le planning).
/// Seuls les jours encore modifiables sont touchés ; l'historique n'est jamais effacé.
/// </summary>
public sealed class PlanningEffectifSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlanningEffectifSyncHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse le service démarrer (migrations, synchro organisation) avant le premier passage.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PasserAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Synchronisation de l'effectif des plannings : passage interrompu.");
            }

            try { await Task.Delay(Intervalle, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PasserAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        var lundi = aujourdhui.AddDays(-(((int)aujourdhui.DayOfWeek + 6) % 7));
        var lundiSuivant = lundi.AddDays(7);
        var ids = await db.WeeklyPlannings
            .AsNoTracking()
            .Where(p => p.WeekStartDate >= lundi && p.WeekStartDate <= lundiSuivant)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var total = 0;
        foreach (var id in ids)
        {
            // Un contexte par planning : un échec n'empêche pas les autres cellules.
            using var s = scopeFactory.CreateScope();
            if (s.ServiceProvider.GetRequiredService<IPlanningService>() is not PlanningService service)
                return;
            try
            {
                total += await service.SynchroniserEffectifAsync(id, ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Effectif du planning {PlanningId} non synchronisé.", id);
            }
        }
        if (total > 0)
            logger.LogInformation("Effectif des plannings synchronisé : {Total} journée(s) retirée(s) ou ajoutée(s).", total);
    }
}
