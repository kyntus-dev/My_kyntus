using Documentation.Domain.Entities;
using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Documentation.Infrastructure.Persistence;

/// <summary>
/// Demandes de documents doublées par e-mail (envoi réalisé par Planning) :
/// nouvelle demande → RH et Admin ; changement de statut → le collaborateur concerné.
/// </summary>
public sealed class DocumentRequestEmailInterceptor(
    IServiceProvider services,
    ILogger<DocumentRequestEmailInterceptor> logger) : SaveChangesInterceptor
{
    private readonly List<NotificationEmailRequestedMessage> _pending = [];

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
        PublishAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending.Clear();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;
        foreach (var entry in context.ChangeTracker.Entries<DocumentRequest>())
        {
            NotificationEmailRequestedMessage? message = entry.State switch
            {
                // Données de démonstration ou reprises d'historique : jamais envoyées par e-mail.
                EntityState.Added when entry.Entity.CreatedAt == default
                                       || entry.Entity.CreatedAt >= DateTimeOffset.UtcNow.AddHours(-1)
                    => ForNewRequest(entry.Entity),
                EntityState.Modified when entry.Property(r => r.Status).IsModified
                                          && !Equals(entry.Property(r => r.Status).OriginalValue, entry.Entity.Status)
                    => ForStatusChange(entry.Entity),
                _ => null,
            };
            if (message is not null)
                _pending.Add(message);
        }
    }

    private async Task PublishAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
            return;
        var batch = _pending.ToList();
        _pending.Clear();
        try
        {
            var bus = services.GetService<IPublishEndpoint>();
            if (bus is null)
                return;
            foreach (var message in batch)
                await bus.Publish(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // L'e-mail ne doit jamais faire échouer la demande elle-même.
            logger.LogWarning(ex, "E-mails des demandes de documents non publiés.");
        }
    }

    public static NotificationEmailRequestedMessage ForNewRequest(DocumentRequest r) => new()
    {
        Source = "documentation",
        Title = "Nouvelle demande de document",
        Body = $"Une nouvelle demande de document{Reference(r)} attend un traitement RH.",
        Route = "/documentation",
        Roles = ["RH", "Admin"],
    };

    public static NotificationEmailRequestedMessage? ForStatusChange(DocumentRequest r)
    {
        (string? title, string? body) = r.Status switch
        {
            DocumentRequestStatus.Approved => ("Demande de document validée", $"Votre demande de document{Reference(r)} a été validée."),
            DocumentRequestStatus.Rejected => ("Demande de document refusée",
                $"Votre demande de document{Reference(r)} a été refusée."
                + (string.IsNullOrWhiteSpace(r.RejectionReason) ? string.Empty : $" Motif : {r.RejectionReason.Trim()}")),
            DocumentRequestStatus.Generated => ("Document disponible", $"Votre document{Reference(r)} est disponible dans My Kyntus."),
            DocumentRequestStatus.Cancelled => ("Demande de document annulée", $"Votre demande de document{Reference(r)} a été annulée."),
            _ => (null, null),
        };
        if (title is null || body is null)
            return null;
        var employee = r.BeneficiaryUserId is { } b && b != Guid.Empty ? b : r.RequesterUserId;
        if (employee == Guid.Empty)
            return null;
        return new NotificationEmailRequestedMessage
        {
            Source = "documentation",
            Title = title,
            Body = body,
            Route = "/documentation",
            EmployeeIds = [employee],
        };
    }

    private static string Reference(DocumentRequest r) =>
        string.IsNullOrWhiteSpace(r.RequestNumber) ? string.Empty : $" {r.RequestNumber.Trim()}";
}
