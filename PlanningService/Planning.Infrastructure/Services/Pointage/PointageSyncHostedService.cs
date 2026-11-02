using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Pointage;

/// <summary>
/// Boucle de synchronisation des pointages ZKBio Time (toutes les 60 s par défaut).
/// Inactive tant que <c>Pointage__Enabled</c> n'est pas à true ; une erreur est notée dans l'état
/// de synchronisation (visible par RH) et la boucle reprend au tour suivant.
/// </summary>
public sealed class PointageSyncHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PointageOptions> options,
    ILogger<PointageSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse le temps aux patches de schéma (tables de pointage) de s'appliquer au démarrage.
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        var annonce = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var o = options.CurrentValue;
            if (o.Enabled)
            {
                if (!annonce)
                {
                    logger.LogInformation("Synchronisation des pointages ZKBio Time démarrée ({Url}).", o.BaseUrl);
                    annonce = true;
                }
                await RunOnceAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(o.IntervalSeconds, 15, 3600)), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPointageSyncService>().SynchroniserAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Synchronisation des pointages ZKBio Time en échec — nouvel essai au prochain tour.");
            await NoterErreurAsync(ex.Message, ct);
        }
    }

    private async Task NoterErreurAsync(string message, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.PointageSyncStates.FirstOrDefaultAsync(s => s.Id == PointageSyncState.SingletonId, ct);
            if (state is null)
            {
                state = new PointageSyncState();
                db.PointageSyncStates.Add(state);
            }
            state.DerniereErreur = message.Length > 1000 ? message[..1000] : message;
            state.DerniereErreurAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "État de synchronisation non enregistré.");
        }
    }
}
