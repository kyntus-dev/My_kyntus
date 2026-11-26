using Kyntus.Messaging.Contracts;
using MassTransit;
using Planning.Application.Abstractions;

namespace Planning.Infrastructure.Messaging.Consumers;

/// <summary>v64 — Action publiée par un autre service (droits d'accès…) → journal d'actions du Planning.</summary>
public sealed class JournalActionConsumer(IJournalActionsService journal) : IConsumer<JournalActionMessage>
{
    public Task Consume(ConsumeContext<JournalActionMessage> context)
    {
        var m = context.Message;
        if (string.IsNullOrWhiteSpace(m.Module) || string.IsNullOrWhiteSpace(m.Operation)) return Task.CompletedTask;
        // v67 — Action du Super Admin : jamais inscrite au journal d'actions.
        if (m.ActeurSuperAdmin) return Task.CompletedTask;
        return journal.EnregistrerAsync(
            new JournalEvenement(
                m.Module, m.Operation, string.IsNullOrWhiteSpace(m.Element) ? m.Module : m.Element,
                m.Entite, m.EntiteId, null, m.AnciennesValeurs, m.NouvellesValeurs, m.Details, m.Source),
            new JournalAuteurExterne(m.ActeurGuid, m.ActeurEmail, m.ActeurRole, m.ActeurSuperAdmin),
            context.CancellationToken);
    }
}
