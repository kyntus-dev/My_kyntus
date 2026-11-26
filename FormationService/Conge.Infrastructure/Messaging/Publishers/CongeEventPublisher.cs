using Conge.Application.Contracts;
using Kyntus.Messaging.Contracts;
using MassTransit;

namespace Conge.Infrastructure.Messaging.Publishers;

public class CongeEventPublisher : ICongeEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public CongeEventPublisher(IPublishEndpoint publishEndpoint)
        => _publishEndpoint = publishEndpoint;

    public async Task PublishCongeValideAsync(
        Guid employeId,
        Guid demandeId,
        DateTime dateDebut,
        DateTime dateFin,
        double nombreJours,
        string typeConge,
        string? typeExceptionnel = null,
        Guid? validateurId = null,
        string? validateurNom = null,
        CancellationToken ct = default)
    {
        await _publishEndpoint.Publish(new CongeValideMessage
        {
            DemandeId = demandeId,
            EmployeId = employeId,
            DateDebut = dateDebut,
            DateFin = dateFin,
            NombreJours = nombreJours,
            DateValidation = DateTime.UtcNow,
            TypeConge = typeConge,
            TypeExceptionnel = typeExceptionnel,
            ValidateurId = validateurId,
            ValidateurNom = validateurNom,
        }, ct);
    }

    public async Task PublishCongeModifieAsync(
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
    {
        await _publishEndpoint.Publish(new CongeValideMessage
        {
            DemandeId = demandeId,
            EmployeId = employeId,
            DateDebut = dateDebut,
            DateFin = dateFin,
            NombreJours = nombreJours,
            DateValidation = DateTime.UtcNow,
            TypeConge = typeConge,
            TypeExceptionnel = typeExceptionnel,
            ValidateurId = acteurId,
            ValidateurNom = acteurNom,
            Modification = true,
            AncienneDateDebut = ancienneDateDebut,
            AncienneDateFin = ancienneDateFin,
        }, ct);
    }

    public Task PublishJournalAsync(JournalActionMessage message, CancellationToken ct = default)
        => _publishEndpoint.Publish(message, ct);

    public async Task PublishCongeRefuseAsync(
        Guid employeId,
        Guid demandeId,
        string motif,
        Guid? validateurId = null,
        string? validateurNom = null,
        CancellationToken ct = default)
    {
        await _publishEndpoint.Publish(new CongeRefuseMessage
        {
            DemandeId = demandeId,
            EmployeId = employeId,
            Motif = motif,
            ValidateurId = validateurId,
            ValidateurNom = validateurNom,
        }, ct);
    }

    public async Task PublishCongeDemandeAsync(
        Guid employeId,
        Guid demandeId,
        Guid managerId,
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken ct = default)
    {
        await _publishEndpoint.Publish(new CongeDemandeMessage
        {
            DemandeId = demandeId,
            EmployeId = employeId,
            ManagerId = managerId,
            DateDebut = dateDebut,
            DateFin = dateFin
        }, ct);
    }
}
