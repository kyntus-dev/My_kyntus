using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.SetActiveFromPlanning;

/// <summary>
/// Active ou désactive un compte depuis Planning (sortie d'un collaborateur, réintégration).
/// Le compte n'est jamais supprimé : un compte inactif ne peut plus se connecter ni renouveler
/// sa session, et son historique reste disponible.
/// </summary>
public record SetActiveFromPlanningCommand(int AuthUserId, bool IsActive) : IRequest<bool>;

public sealed class SetActiveFromPlanningCommandHandler(IUserRepository users)
    : IRequestHandler<SetActiveFromPlanningCommand, bool>
{
    public async Task<bool> Handle(SetActiveFromPlanningCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.AuthUserId, ct);
        if (user is null)
            return false;

        if (user.IsActive == request.IsActive && (request.IsActive || user.RefreshToken is null))
            return true;

        user.IsActive = request.IsActive;
        if (!request.IsActive)
        {
            // Coupe immédiatement les sessions ouvertes : le jeton de renouvellement est révoqué.
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
        }
        await users.UpdateAsync(user, ct);
        return true;
    }
}
