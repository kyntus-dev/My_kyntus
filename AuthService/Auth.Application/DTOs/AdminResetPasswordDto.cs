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
}
