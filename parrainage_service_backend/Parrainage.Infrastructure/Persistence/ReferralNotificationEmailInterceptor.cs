using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Parrainage.Infrastructure.Persistence;

/// <summary>
/// Chaque notification de parrainage enregistrée est aussi demandée par e-mail (envoi réalisé par
/// Planning). Destinataires : le parrain concerné et les rôles RH / Admin visés par la notification.
/// Respecte l'option « E-mail » des préférences de notification du parrainage.
/// </summary>
public sealed class ReferralNotificationEmailInterceptor(
    IServiceProvider services,
    ILogger<ReferralNotificationEmailInterceptor> logger) : SaveChangesInterceptor
{
    private readonly List<ReferralNotificationEntity> _added = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PublishAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _added.Clear();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _added.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;
        _added.AddRange(context.ChangeTracker.Entries<ReferralNotificationEntity>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity));
    }

    private async Task PublishAsync(DbContext? context, CancellationToken ct)
    {
        if (_added.Count == 0 || context is not ParrainageDbContext db)
            return;
        var batch = _added.ToList();
        _added.Clear();

        try
        {
            var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(ct);
            if (prefs is { Email: false })
                return;

            var bus = services.GetService<IPublishEndpoint>();
            if (bus is null)
                return;

            var recent = DateTimeOffset.UtcNow.AddHours(-1);
            foreach (var n in batch)
            {
                // Données de démonstration ou reprises d'historique : jamais envoyées par e-mail.
                if (n.CreatedAt < recent)
                    continue;
                var message = BuildMessage(n);
                if (message is not null)
                    await bus.Publish(message, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // L'e-mail ne doit jamais faire échouer le parrainage lui-même.
            logger.LogWarning(ex, "E-mails des notifications de parrainage non publiés.");
        }
    }

    public static NotificationEmailRequestedMessage? BuildMessage(ReferralNotificationEntity n)
    {
        if (string.IsNullOrWhiteSpace(n.Message))
            return null;

        var targets = n.TargetRoles.Select(r => r.Trim().ToUpperInvariant()).ToHashSet();
        var roles = new List<string>();
        if (targets.Contains("RH")) roles.Add("RH");
        if (targets.Contains("ADMIN")) roles.Add("Admin");

        // Le parrain n'est prévenu que des notifications qui lui sont destinées.
        var employeeIds = new List<Guid>();
        var authUserIds = new List<int>();
        if ((targets.Contains("PILOTE") || targets.Contains("ALL")) && !string.IsNullOrWhiteSpace(n.ReferrerId))
        {
            if (Guid.TryParse(n.ReferrerId, out var guid) && guid != Guid.Empty)
                employeeIds.Add(guid);
            else if (int.TryParse(n.ReferrerId, out var authId) && authId > 0)
                authUserIds.Add(authId);
        }

        if (roles.Count == 0 && employeeIds.Count == 0 && authUserIds.Count == 0)
            return null;

        return new NotificationEmailRequestedMessage
        {
            RequestId = StableId(n.Id),
            Source = "parrainage",
            Title = "Parrainage",
            Body = n.Message.Trim(),
            Route = "/parrainage",
            Roles = roles,
            EmployeeIds = employeeIds,
            AuthUserIds = authUserIds,
            OccurredAt = n.CreatedAt.UtcDateTime,
        };
    }

    /// <summary>Même notification → même identifiant : un rejeu ne renvoie pas l'e-mail.</summary>
    private static Guid StableId(string notificationId)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("parrainage:" + notificationId));
        return new Guid(bytes);
    }
}
