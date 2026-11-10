using Auth.Application.DTOs;
using Auth.Application.Security;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.AdminResetPassword;

public record AdminResetPasswordCommand(AdminResetPasswordDto Dto) : IRequest<AdminResetPasswordResult>;

/// <summary>
/// Réinitialisation par un administrateur. v38 : le mot de passe est posé sur le compte avec lequel la
/// personne se connecte réellement (son e-mail), et tout ce qui ferait encore échouer la connexion est
/// corrigé au passage : verrouillage, compte désactivé alors que le collaborateur est actif, e-mail ou
/// identifiant employé décalés. Chaque correction est renvoyée pour être affichée à l'administrateur.
/// </summary>
public class AdminResetPasswordCommandHandler : IRequestHandler<AdminResetPasswordCommand, AdminResetPasswordResult>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminResetPasswordCommandHandler> _logger;

    public AdminResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<AdminResetPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AdminResetPasswordResult> Handle(AdminResetPasswordCommand request, CancellationToken ct)
    {
        var dto = request.Dto;
        if (!PasswordPolicy.TryValidate(dto.NewPassword, out var error))
            throw new ArgumentException(error);

        var (user, trouvePar) = await ResolveUserAsync(dto, ct);
        if (user is null)
            throw new KeyNotFoundException(
                "Compte de connexion introuvable (ni par e-mail, ni par GUID employé, ni par identifiant Auth).");

        var corrections = new List<string>();
        await AlignerIdentifiantsAsync(user, dto, trouvePar, corrections, ct);

        if (user.LockoutEnd is { } fin && fin > DateTime.UtcNow)
            corrections.Add("Compte verrouillé après plusieurs essais : déverrouillé.");
        if (!user.IsActive && dto.Activer == true)
        {
            user.IsActive = true;
            corrections.Add("Compte de connexion désactivé alors que le collaborateur est actif : réactivé.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword.Trim());
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, ct);

        _logger.LogInformation(
            "PasswordReset: AuthUserId={UserId} Email={Email} SubjectId={SubjectId} TrouvePar={TrouvePar} Corrections={Corrections}",
            user.Id,
            user.Email,
            user.SubjectId,
            trouvePar,
            string.Join(" | ", corrections));

        return new AdminResetPasswordResult(user.Id, user.Email, user.SubjectId, corrections);
    }

    /// <summary>
    /// v38 : ordre de recherche : e-mail (celui que la personne tape pour se connecter), puis GUID employé,
    /// puis identifiant Auth connu du Planning (retenu seulement si c'est bien la même personne).
    /// Avant, le GUID passait en premier : s'il existait un ancien compte avec ce GUID et un autre e-mail,
    /// le mot de passe était posé sur l'ancien compte et la connexion échouait (« e-mail ou mot de passe incorrect »).
    /// </summary>
    private async Task<(Domain.Entities.User? User, string TrouvePar)> ResolveUserAsync(AdminResetPasswordDto dto, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var byEmail = await _userRepository.GetByEmailAsync(dto.Email.Trim(), ct);
            if (byEmail is not null)
                return (byEmail, "email");
        }

        if (dto.EmployeeId is { } employeeId && employeeId != Guid.Empty)
        {
            var bySubject = await _userRepository.GetBySubjectIdAsync(employeeId, ct);
            if (bySubject is not null)
                return (bySubject, "guid");
        }

        if (dto.AuthUserId is > 0)
        {
            var byId = await _userRepository.GetByIdAsync(dto.AuthUserId.Value, ct);
            // Retenu seulement si c'est bien la même personne (même GUID ou même e-mail) :
            // un identifiant périmé ne doit jamais réinitialiser le compte de quelqu'un d'autre.
            var memePersonne = byId is not null
                && ((dto.EmployeeId is { } guid && guid != Guid.Empty && byId.SubjectId == guid)
                    || (!string.IsNullOrWhiteSpace(dto.Email)
                        && string.Equals(byId.Email, dto.Email.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (memePersonne)
                return (byId, "identifiant");
            if (byId is not null)
                _logger.LogWarning(
                    "PasswordReset: AuthUserId={Id} ignoré : e-mail {Email} et SubjectId {SubjectId} ne correspondent pas.",
                    byId.Id, byId.Email, byId.SubjectId);
        }

        return (null, "aucun");
    }

    /// <summary>
    /// Trouvé par e-mail : le GUID employé est réaligné (comme le fait déjà la synchronisation Planning).
    /// Trouvé autrement : l'e-mail de connexion est réaligné sur celui de l'application (il est libre,
    /// sinon la recherche par e-mail l'aurait trouvé) ; le GUID aussi s'il n'est porté par aucun autre compte.
    /// </summary>
    private async Task AlignerIdentifiantsAsync(
        Domain.Entities.User user, AdminResetPasswordDto dto, string trouvePar, List<string> corrections, CancellationToken ct)
    {
        if (dto.EmployeeId is { } guid && guid != Guid.Empty && user.SubjectId != guid)
        {
            var porteur = await _userRepository.GetBySubjectIdAsync(guid, ct);
            if (trouvePar == "email" || porteur is null)
            {
                _logger.LogInformation(
                    "PasswordReset: SubjectId réaligné AuthUserId={Id} {Old} -> {New}", user.Id, user.SubjectId, guid);
                user.SubjectId = guid;
                if (porteur is not null && porteur.Id != user.Id)
                    _logger.LogWarning(
                        "PasswordReset: ancien compte de connexion AuthUserId={Id} ({Email}) porte aussi ce GUID ; il n'est pas modifié.",
                        porteur.Id, porteur.Email);
            }
        }

        if (trouvePar == "email")
            return;

        var email = dto.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email) && email.Length <= 100
            && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var autre = await _userRepository.GetByEmailAsync(email, ct);
            if (autre is null || autre.Id == user.Id)
            {
                _logger.LogInformation(
                    "PasswordReset: e-mail réaligné AuthUserId={Id} {Old} -> {New}", user.Id, user.Email, email);
                corrections.Add($"E-mail de connexion corrigé : {user.Email} → {email}.");
                if (string.Equals(user.Username, user.Email, StringComparison.OrdinalIgnoreCase) && email.Length <= 50)
                    user.Username = email;
                user.Email = email;
            }
        }
    }
}
