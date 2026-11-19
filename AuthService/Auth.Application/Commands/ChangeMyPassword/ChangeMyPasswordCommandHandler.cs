using Auth.Application.Security;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.ChangeMyPassword;

/// <summary>v56 — Changement de son propre mot de passe, par le titulaire du compte.</summary>
public sealed record ChangeMyPasswordCommand(
    Guid SubjectId,
    string? Email,
    string AncienMotDePasse,
    string NouveauMotDePasse) : IRequest<ChangeMyPasswordResult>;

/// <summary>Résultat : rien de secret n'est renvoyé, seulement la confirmation.</summary>
public sealed record ChangeMyPasswordResult(string Email);

/// <summary>
/// v56 — Chacun change son mot de passe depuis l'application, sans passer par un administrateur.
/// <para>
/// L'ancien mot de passe est exigé (le jeton seul ne suffit pas : un poste laissé ouvert ne doit pas
/// permettre de changer le mot de passe). Le nouveau passe la politique de l'application, et les
/// sessions ouvertes ailleurs sont coupées : le jeton de rafraîchissement est effacé.
/// </para>
/// </summary>
public sealed class ChangeMyPasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILogger<ChangeMyPasswordCommandHandler> logger)
    : IRequestHandler<ChangeMyPasswordCommand, ChangeMyPasswordResult>
{
    public async Task<ChangeMyPasswordResult> Handle(ChangeMyPasswordCommand request, CancellationToken ct)
    {
        var ancien = (request.AncienMotDePasse ?? string.Empty).Trim();
        var nouveau = (request.NouveauMotDePasse ?? string.Empty).Trim();

        if (ancien.Length == 0)
            throw new ArgumentException("Saisissez votre mot de passe actuel.");
        if (!PasswordPolicy.TryValidate(nouveau, out var erreur))
            throw new ArgumentException(erreur);
        if (string.Equals(ancien, nouveau, StringComparison.Ordinal))
            throw new ArgumentException("Le nouveau mot de passe doit être différent de l'actuel.");

        var user = await userRepository.GetBySubjectIdAsync(request.SubjectId, ct);
        if (user is null && !string.IsNullOrWhiteSpace(request.Email))
            user = await userRepository.GetByEmailAsync(request.Email.Trim(), ct);
        if (user is null)
            throw new KeyNotFoundException("Compte de connexion introuvable.");
        if (!user.IsActive)
            throw new InvalidOperationException("Ce compte est désactivé : contactez les RH.");

        if (!passwordHasher.VerifyPassword(user.PasswordHash, ancien))
            throw new UnauthorizedAccessException("Mot de passe actuel incorrect.");

        user.PasswordHash = passwordHasher.HashPassword(nouveau);
        // Sessions ouvertes ailleurs : coupées.
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;
        await userRepository.UpdateAsync(user, ct);

        logger.LogInformation("Mot de passe changé par le titulaire du compte {UserId}.", user.Id);
        return new ChangeMyPasswordResult(user.Email);
    }
}
