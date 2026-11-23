namespace Auth.Application.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }

    /// <summary>v60 — Compte Super Admin (le rôle reste « Admin »).</summary>
    public bool IsSuperAdmin { get; set; }
}
