namespace Auth.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public Guid SubjectId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public DateTime? TemporaryPasswordExpiresAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public int CredentialVersion { get; set; }
    public string? FirstLoginTokenHash { get; set; }
    public DateTime? FirstLoginTokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
}

