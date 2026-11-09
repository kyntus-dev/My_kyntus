using System.ComponentModel.DataAnnotations;

namespace Auth.Application.DTOs;

public class AdminResetPasswordDto
{
    public Guid? EmployeeId { get; set; }

    /// <summary>Identifiant Auth connu du Planning (Users.AuthUserId) : retrouve le compte même si l'e-mail a changé.</summary>
    public int? AuthUserId { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    public required string NewPassword { get; set; }

    /// <summary>
    /// v38 : collaborateur actif dans l'application. Vrai : un compte de connexion désactivé est réactivé,
    /// sinon la personne ne pourrait pas se connecter avec le nouveau mot de passe.
    /// </summary>
    public bool? Activer { get; set; }
}

/// <summary>v38 : compte réinitialisé et ce qui a été corrigé pour que la connexion fonctionne.</summary>
public sealed record AdminResetPasswordResult(int Id, string Email, Guid SubjectId, IReadOnlyList<string> Corrections);
