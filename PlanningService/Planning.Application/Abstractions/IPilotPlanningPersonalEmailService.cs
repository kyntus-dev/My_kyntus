namespace Planning.Application.Abstractions;

public interface IPilotPlanningPersonalEmailService
{
    Task SendToAssignedPilotsAsync(int planningId, bool updated, CancellationToken ct = default);
}
