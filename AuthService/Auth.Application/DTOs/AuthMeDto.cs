namespace Auth.Application.DTOs;

public class AuthMeDto
{
    public Guid SubjectId { get; set; }
    public int AuthUserId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string TenantId { get; set; }

    /// <summary>v60 — Compte Super Admin (claim signée du jeton).</summary>
    public bool IsSuperAdmin { get; set; }
}
