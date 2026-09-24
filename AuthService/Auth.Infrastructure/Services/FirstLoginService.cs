using System.Security.Cryptography;
using System.Text;
using Auth.Application.Security;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Services;

/// <summary>Opaque, single-use challenges. No application JWT is issued during onboarding.</summary>
public sealed class FirstLoginService(
    AuthDbContext db, IPasswordHasher hasher, IConfiguration config) : IFirstLoginService
{
    private const int ChallengeSeconds = 600;

    public bool TryInitializeNewUser(User user)
    {
        if (!config.GetValue("FirstLogin:Enabled", false) || !FirstLoginRoles.IsEligible(user.Role?.Name))
            return false;
        ApplyTemporaryPassword(user, GetConfiguredPassword());
        return true;
    }

    private string GetConfiguredPassword()
    {
        if (!config.GetValue("FirstLogin:Enabled", false))
            throw new InvalidOperationException("Les nouvelles activations sont désactivées.");
        var password = config["FirstLogin:CommonPassword"];
        if (!PasswordPolicy.TryValidate(password, out _))
            throw new InvalidOperationException("Configurer un mot de passe temporaire conforme à la politique Auth.");
        return password!.Trim();
    }

    private void ApplyTemporaryPassword(User user, string password)
    {
        user.PasswordHash = hasher.HashPassword(password);
        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAt = DateTime.UtcNow.AddHours(
            Math.Clamp(config.GetValue("FirstLogin:TemporaryPasswordHours", 72), 1, 168));
        user.CredentialVersion++;
        user.FirstLoginTokenHash = null;
        user.FirstLoginTokenExpiresAt = null;
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<FirstLoginChallenge> BeginAsync(User user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!user.IsActive || !user.MustChangePassword || !FirstLoginRoles.IsEligible(user.Role?.Name)
            || user.TemporaryPasswordExpiresAt is null || user.TemporaryPasswordExpiresAt <= now)
            throw new UnauthorizedAccessException("Activation expirée ou indisponible. Contactez la RH.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.FirstLoginTokenHash = HashToken(token);
        user.FirstLoginTokenExpiresAt = new[] { now.AddSeconds(ChallengeSeconds), user.TemporaryPasswordExpiresAt.Value }.Min();
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync(ct);
        return new FirstLoginChallenge(token, (int)(user.FirstLoginTokenExpiresAt.Value - now).TotalSeconds);
    }

    public async Task CompleteAsync(string changeToken, string password, string confirmation, CancellationToken ct)
    {
        if (changeToken.Length != 64 || !changeToken.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("Autorisation invalide ou expirée. Reconnectez-vous.");
        if (password != confirmation)
            throw new ArgumentException("Les mots de passe ne correspondent pas.");
        if (!PasswordPolicy.TryValidate(password, out var error))
            throw new ArgumentException(error);

        var tokenHash = HashToken(changeToken);
        var now = DateTime.UtcNow;
        var user = await db.Users.AsNoTracking().Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.FirstLoginTokenHash == tokenHash, ct);
        if (user is null || !user.IsActive || !user.MustChangePassword
            || !FirstLoginRoles.IsEligible(user.Role.Name)
            || user.FirstLoginTokenExpiresAt is null || user.FirstLoginTokenExpiresAt <= now
            || user.TemporaryPasswordExpiresAt is null || user.TemporaryPasswordExpiresAt <= now
            || user.LockoutEnd > now)
            throw new UnauthorizedAccessException("Autorisation invalide ou expirée. Reconnectez-vous.");
        if (hasher.VerifyPassword(user.PasswordHash, password.Trim()))
            throw new ArgumentException("Choisissez un mot de passe différent du mot de passe temporaire.");

        var newHash = hasher.HashPassword(password.Trim());
        // Compare-and-swap consumes the challenge and changes the password in ONE SQL statement.
        // The audit and password commit together; concurrent requests can succeed only once.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Users.Where(x => x.Id == user.Id && x.IsActive && x.MustChangePassword
                && x.CredentialVersion == user.CredentialVersion && x.RoleId == user.RoleId
                && x.FirstLoginTokenHash == tokenHash && x.FirstLoginTokenExpiresAt > now
                && x.TemporaryPasswordExpiresAt > now && (x.LockoutEnd == null || x.LockoutEnd <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PasswordHash, newHash)
                .SetProperty(x => x.MustChangePassword, false)
                .SetProperty(x => x.PasswordChangedAt, now)
                .SetProperty(x => x.TemporaryPasswordExpiresAt, (DateTime?)null)
                .SetProperty(x => x.FirstLoginTokenHash, (string?)null)
                .SetProperty(x => x.FirstLoginTokenExpiresAt, (DateTime?)null)
                .SetProperty(x => x.RefreshToken, (string?)null)
                .SetProperty(x => x.RefreshTokenExpiryTime, (DateTime?)null)
                .SetProperty(x => x.CredentialVersion, x => x.CredentialVersion + 1)
                .SetProperty(x => x.AccessFailedCount, 0)
                .SetProperty(x => x.LockoutEnd, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (changed != 1)
            throw new UnauthorizedAccessException("Autorisation déjà utilisée. Reconnectez-vous.");
        db.AccessAuditEvents.Add(new AccessAuditEvent
        {
            EventType = "FirstLoginCompleted", Email = user.Email, UserId = user.Id,
            At = now, Message = "Mot de passe personnel enregistré."
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<FirstLoginEnrollmentResult>> EnrollAsync(
        IReadOnlyCollection<int> userIds, bool dryRun, bool renewExpired, string actor, CancellationToken ct)
    {
        if (userIds.Count is 0 or > 500) throw new ArgumentException("Sélectionnez entre 1 et 500 comptes.");
        var password = GetConfiguredPassword();
        var users = await db.Users.Include(x => x.Role).Where(x => userIds.Contains(x.Id)).ToListAsync(ct);
        var result = new List<FirstLoginEnrollmentResult>();
        foreach (var id in userIds.Distinct())
        {
            var user = users.SingleOrDefault(x => x.Id == id);
            var status = user switch
            {
                null => "not_found",
                { IsActive: false } => "inactive",
                _ when !FirstLoginRoles.IsEligible(user.Role.Name) => "role_not_eligible",
                { PasswordChangedAt: not null } => "already_personalized",
                { MustChangePassword: true } when !(renewExpired && user.TemporaryPasswordExpiresAt <= DateTime.UtcNow)
                    => "already_enrolled",
                _ => dryRun ? "would_enroll" : "enrolled"
            };
            result.Add(new(id, user?.Email ?? "", status));
            if (status != "enrolled") continue;
            ApplyTemporaryPassword(user!, password);
            db.AccessAuditEvents.Add(new AccessAuditEvent
            {
                EventType = "FirstLoginEnrolled", Email = user!.Email, UserId = user.Id,
                At = DateTime.UtcNow, Message = $"Activation explicite par {actor}."
            });
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        return result;
    }

    public Task<bool> IsSessionValidAsync(Guid subjectId, int version, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(x => x.SubjectId == subjectId && x.IsActive
            && !x.MustChangePassword && x.CredentialVersion == version
            && (x.LockoutEnd == null || x.LockoutEnd <= DateTime.UtcNow), ct);

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
