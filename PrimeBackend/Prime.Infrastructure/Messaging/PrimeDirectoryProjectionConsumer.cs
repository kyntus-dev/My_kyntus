using Kyntus.Messaging.Contracts;
using MassTransit;
using Prime.Infrastructure.Services;

namespace Prime.Infrastructure.Messaging;

public sealed class PrimeDirectoryProjectionConsumer(IEmployeeDirectorySyncService employeeSync) :
    IConsumer<DirectoryEmployeeChangedMessage>
{
    public async Task Consume(ConsumeContext<DirectoryEmployeeChangedMessage> context)
    {
        var msg = context.Message;
        // v73 — Départ ou désactivation : le message était reçu puis jeté, et l'agent restait
        // indéfiniment dans les périmètres. Il est désormais marqué « sorti » (fiche conservée
        // pour l'historique des primes, affichée grisée, hors effectif planifiable).
        if (msg.IsDeleted)
        {
            await employeeSync.MarquerSortiAsync(msg.EmployeeId, context.CancellationToken);
            return;
        }

        await employeeSync.EnsureFromPlanningAsync(
            new EmployeeDirectoryUpsertRequest(
                EmployeeId: msg.EmployeeId,
                FirstName: msg.FirstName,
                LastName: msg.LastName,
                Email: msg.Email,
                Role: msg.Role,
                PrimeServiceId: msg.ServiceId,
                SupervisorId: msg.SuperviseurId ?? msg.ParentId ?? Guid.Empty,
                BusinessDepartmentId: msg.BusinessDepartmentId,
                BusinessDepartmentKind: msg.BusinessDepartmentKind,
                ChefDeProjetId: msg.ChefDeProjetId,
                SuperviseurId: msg.SuperviseurId,
                ReferentTechniqueId: msg.ReferentTechniqueId),
            context.CancellationToken);
    }
}
