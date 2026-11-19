namespace Auth.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public Guid SubjectId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }

    /// <summary>
    /// v60 — Super Admin : compte « Admin » qui garde l'intégralité des droits et seul habilité à
    /// régler les droits des administrateurs. Posé uniquement par l'initialisation (jamais par l'API).
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>Super Admin effectif : marqueur posé ET rôle Admin.</summary>
    public bool EstSuperAdminEffectif =>
        IsSuperAdmin && string.Equals(Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
}
