using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Planning.Infrastructure.Services.NotificationEmail;

/// <summary>Boucle d'envoi des e-mails : toutes les 15 secondes, collecte des notifications puis envoi.</summary>
public sealed class NotificationEmailHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationEmailHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse le temps aux patches de schéma (tables e-mail) de s'appliquer au démarrage.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        logger.LogInformation("Envoi des e-mails (plannings et notifications) démarré.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                var queued = await processor.HarvestAsync(stoppingToken);
                var sent = await processor.SendDueAsync(stoppingToken);
                if (queued > 0 || sent > 0)
                    logger.LogInformation("E-mails : {Queued} mis en file, {Sent} envoyé(s).", queued, sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Erreur de la boucle d'envoi des e-mails.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
