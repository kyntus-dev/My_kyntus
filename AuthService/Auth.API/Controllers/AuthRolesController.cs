using Auth.API.Filters;
using Auth.Application;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Controllers;

/// <summary>
/// Rôles de connexion gérés depuis l'écran Admin « Rôles » (Planning, appel interne uniquement).
/// Les rôles système ne sont jamais renommés ni supprimés : le code de toute la plateforme en dépend.
/// </summary>
[ApiController]
[Route("api/Auth/roles-from-planning")]
[RequireInternalService]
public class AuthRolesController(AuthDbContext db, ILogger<AuthRolesController> logger) : ControllerBase
{
    public static readonly HashSet<string> RolesSysteme = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin", "RH", "Audit", "Coach", "Comptabilité", "Employee", "Formateur", "Manager",
        "Qualiticien", "RP", "Superviseur", "Ingénieur", "Support IT", "User",
    };

    public sealed record RoleUpsertRequest(string Name, string? PreviousName, string? Description);

    /// <summary>Crée le rôle, met à jour sa description, ou le renomme (PreviousName).</summary>
    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] RoleUpsertRequest body, CancellationToken ct)
    {
        var nom = NomConnexion(body.Name);
        if (nom is null || nom.Length > 50)
            return BadRequest(new { message = "Nom de rôle invalide." });
        var description = Tronquer(string.IsNullOrWhiteSpace(body.Description) ? nom : body.Description.Trim(), 200);

        var ancien = NomConnexion(body.PreviousName);
        if (ancien is not null && !string.Equals(ancien, nom, StringComparison.Ordinal))
        {
            var aRenommer = await db.Roles.FirstOrDefaultAsync(r => r.Name == ancien, ct);
            if (aRenommer is not null)
            {
                if (RolesSysteme.Contains(ancien))
                    return Conflict(new { message = "Rôle système : renommage interdit." });
                if (await db.Roles.AnyAsync(r => r.Name == nom, ct))
                    return Conflict(new { message = "Ce nom de rôle existe déjà." });
                aRenommer.Name = nom;
                aRenommer.Description = description;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Rôle de connexion « {Ancien} » renommé en « {Nom} ».", ancien, nom);
                return Ok(new { aRenommer.Id, aRenommer.Name });
            }
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == nom, ct);
        if (role is null)
        {
            // Même attribution d'identifiant que AuthDatabaseInitializer.EnsureRole.
            var id = (await db.Roles.MaxAsync(r => (int?)r.Id, ct) ?? 0) + 1;
            role = new Role { Id = id, Name = nom, Description = description, CreatedAt = DateTime.UtcNow };
            db.Roles.Add(role);
            logger.LogInformation("Rôle de connexion « {Nom} » créé.", nom);
        }
        else
        {
            role.Description = description;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { role.Id, role.Name });
    }

    /// <summary>Bascule les comptes restants vers <paramref name="remplacement"/>, puis supprime le rôle.</summary>
    [HttpDelete]
    public async Task<IActionResult> Supprimer([FromQuery] string name, [FromQuery] string? remplacement, CancellationToken ct)
    {
        var nom = NomConnexion(name);
        if (nom is null) return BadRequest(new { message = "Nom de rôle invalide." });
        if (RolesSysteme.Contains(nom)) return Conflict(new { message = "Rôle système : suppression interdite." });

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == nom, ct);
        if (role is null) return Ok(new { reaffectes = 0 });

        var comptes = await db.Users.Where(u => u.RoleId == role.Id).ToListAsync(ct);
        if (comptes.Count > 0)
        {
            var cibleNom = NomConnexion(remplacement);
            var cible = cibleNom is null ? null : await db.Roles.FirstOrDefaultAsync(r => r.Name == cibleNom, ct);
            if (cible is null || cible.Id == role.Id)
                return Conflict(new { message = "Rôle de remplacement introuvable côté connexion." });
            foreach (var u in comptes)
            {
                u.RoleId = cible.Id;
                u.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Rôle de connexion « {Nom} » supprimé ({N} compte(s) basculé(s)).", nom, comptes.Count);
        return Ok(new { reaffectes = comptes.Count });
    }

    private static string? NomConnexion(string? nom) =>
        string.IsNullOrWhiteSpace(nom) ? null : PlanningRoleToAuthRoleMapper.MapToAuthRoleName(nom)?.Trim();

    private static string Tronquer(string s, int max) => s.Length > max ? s[..max] : s;
}
