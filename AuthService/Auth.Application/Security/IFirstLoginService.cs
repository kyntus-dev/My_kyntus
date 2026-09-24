using Auth.Domain.Entities;

namespace Auth.Application.Security;

public record FirstLoginChallenge(string ChangeToken, int ExpiresIn);
public record FirstLoginEnrollmentResult(int UserId, string Email, string Status);

public sealed class PasswordChangeRequiredException(FirstLoginChallenge challenge)
    : Exception("Vous devez définir votre mot de passe personnel.")
{
    public FirstLoginChallenge Challenge { get; } = challenge;
}

public interface IFirstLoginService
{
    bool TryInitializeNewUser(User user);
    Task<FirstLoginChallenge> BeginAsync(User user, CancellationToken ct);
    Task CompleteAsync(string changeToken, string password, string confirmation, CancellationToken ct);
    Task<IReadOnlyList<FirstLoginEnrollmentResult>> EnrollAsync(
        IReadOnlyCollection<int> userIds, bool dryRun, bool renewExpired, string actor, CancellationToken ct);
    Task<bool> IsSessionValidAsync(Guid subjectId, int version, CancellationToken ct);
}

public static class FirstLoginRoles
{
    public static bool IsEligible(string? role) =>
        role is "Employee" or "Pilote" or "Qualiticien";
}
