using Kyntus.Messaging.Contracts;

namespace Conge.Application.Contracts;

/// <summary>
/// Contrat pour la publication d'événements vers RabbitMQ via MassTransit.
/// Implémenté dans Conge.Infrastructure.
/// </summary>
public interface ICongeEventPublisher
{
    Task PublishCongeValideAsync(
        Guid employeId,
        Guid demandeId,
        DateTime dateDebut,
        DateTime dateFin,
        double nombreJours,
        string typeConge,
        string? typeExceptionnel = null,
        Guid? validateurId = null,
        string? validateurNom = null,
        CancellationToken ct = default);

    /// <summary>
    /// v64.2 — congé validé dont les dates ont changé. Par défaut, republie la validation
    /// (Planning met l'absence à jour par l'identifiant de la demande).
    /// </summary>
    Task PublishCongeModifieAsync(
        Guid employeId,
        Guid demandeId,
        DateTime dateDebut,
        DateTime dateFin,
        double nombreJours,
        string typeConge,
        string? typeExceptionnel,
        DateTime ancienneDateDebut,
        DateTime ancienneDateFin,
        Guid? acteurId = null,
        string? acteurNom = null,
        CancellationToken ct = default)
        => PublishCongeValideAsync(
            employeId, demandeId, dateDebut, dateFin, nombreJours, typeConge,
            typeExceptionnel, acteurId, acteurNom, ct);

    /// <summary>v64.2 — action à inscrire au journal d'actions (Audit et conformité). Sans effet par défaut.</summary>
    Task PublishJournalAsync(JournalActionMessage message, CancellationToken ct = default)
        => Task.CompletedTask;

    Task PublishCongeRefuseAsync(
        Guid employeId,
        Guid demandeId,
        string motif,
        Guid? validateurId = null,
        string? validateurNom = null,
        CancellationToken ct = default);

    Task PublishCongeDemandeAsync(
        Guid employeId,
        Guid demandeId,
        Guid managerId,
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken ct = default);
}
