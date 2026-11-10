using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Rôles : lecture, et gestion par l'Admin (création, modification, suppression avec réaffectation).
/// Chaque changement est répercuté sur le compte de connexion (service Auth) ; les droits des
/// rôles créés se règlent ensuite dans « Accès par rôle ».
/// </summary>
public sealed class RoleService(
    AppDbContext context,
    IUserService users,
    IHttpClientFactory httpFactory,
    ILogger<RoleService> logger) : IRoleService
{
    /// <summary>Client HTTP interne vers Auth (clé de service), déclaré dans l'injection de dépendances.</summary>
    public const string AuthClient = "kyntus-auth-internal";
    private const string AuthRoles = "api/Auth/roles-from-planning";
    private static readonly Regex NomValide = new(@"^[\p{L}\p{N}][\p{L}\p{N} _'’.\-]{1,49}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<RoleListItemDto>> GetActiveRolesAsync(CancellationToken ct = default) =>
        await context.Roles
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RoleListItemDto(r.Id, r.Name))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RoleAdminDto>> GetRolesAdminAsync(CancellationToken ct = default)
    {
        var roles = await context.Roles.AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => new { r.Id, r.Name, r.Description, Nb = r.Users.Count(u => u.IsActive) })
            .ToListAsync(ct);
        return roles
            .Select(r => new RoleAdminDto(r.Id, r.Name, r.Description, r.Nb, EstSysteme(r.Name)))
            .OrderBy(r => r.Systeme ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RoleAdminDto> CreerAsync(RoleSaisieRequest request, CancellationToken ct = default)
    {
        var nom = ValiderNom(request.Name);
        await VerifierNomLibreAsync(nom, null, ct);
        var description = Description(request.Description, nom);

        // Connexion d'abord : sans le rôle côté Auth, les comptes ne pourraient pas le recevoir.
        await AppelerAuthAsync(HttpMethod.Put, AuthRoles, new { name = nom, previousName = (string?)null, description }, ct);

        var role = new Role { Name = nom, Description = description, IsActive = true, CreatedAt = DateTime.UtcNow };
        context.Roles.Add(role);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Rôle « {Role} » créé.", nom);
        return new RoleAdminDto(role.Id, role.Name, role.Description, 0, false);
    }

    public async Task<RoleAdminDto> ModifierAsync(int id, RoleSaisieRequest request, CancellationToken ct = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id && r.IsActive, ct)
                   ?? throw new KeyNotFoundException("Rôle introuvable.");
        var nom = ValiderNom(request.Name);
        var renomme = !string.Equals(nom, role.Name, StringComparison.Ordinal);
        if (renomme && EstSysteme(role.Name))
            throw new RoleRegleException("Les rôles système ne peuvent pas être renommés : seule leur description est modifiable.");
        if (renomme) await VerifierNomLibreAsync(nom, role.Id, ct);
        var description = Description(request.Description, nom);

        await AppelerAuthAsync(HttpMethod.Put, AuthRoles,
            new { name = nom, previousName = renomme ? role.Name : null, description }, ct);

        var ancien = role.Name;
        role.Name = nom;
        role.Description = description;
        await context.SaveChangesAsync(ct);
        if (renomme) logger.LogInformation("Rôle « {Ancien} » renommé en « {Nom} ».", ancien, nom);
        var nb = await context.Users.CountAsync(u => u.RoleId == role.Id && u.IsActive, ct);
        return new RoleAdminDto(role.Id, role.Name, role.Description, nb, EstSysteme(role.Name));
    }

    public async Task<RoleSuppressionResultDto> SupprimerAsync(int id, int? remplacementId, CancellationToken ct = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new KeyNotFoundException("Rôle introuvable.");
        if (EstSysteme(role.Name))
            throw new RoleRegleException("Les rôles système ne peuvent pas être supprimés.");

        var comptes = await context.Users.Where(u => u.RoleId == id).Select(u => u.Id).ToListAsync(ct);
        Role? remplacement = null;
        if (remplacementId is int rid)
        {
            remplacement = await context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid && r.IsActive, ct);
            if (remplacement is null || remplacement.Id == id)
                throw new RoleRegleException("Choisissez un autre rôle de remplacement.");
        }
        if (comptes.Count > 0 && remplacement is null)
            throw new RoleRegleException($"{comptes.Count} compte(s) ont ce rôle : choisissez le rôle qui le remplace.");

        // Fiche, connexion et annuaire de chaque compte, un par un.
        var reaffectes = 0;
        foreach (var userId in comptes)
            if (await users.ChangerRoleAsync(userId, remplacement!.Id, ct)) reaffectes++;
        context.ChangeTracker.Clear();

        var restants = await context.Users.CountAsync(u => u.RoleId == id, ct);
        if (restants > 0)
            throw new RoleRegleException($"{restants} compte(s) n'ont pas pu être basculés : rien n'a été supprimé.");

        var url = $"{AuthRoles}?name={Uri.EscapeDataString(role.Name)}"
                  + (remplacement is null ? "" : $"&remplacement={Uri.EscapeDataString(remplacement.Name)}");
        await AppelerAuthAsync(HttpMethod.Delete, url, null, ct);

        var aSupprimer = await context.Roles.FirstAsync(r => r.Id == id, ct);
        context.Roles.Remove(aSupprimer);
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Rôle « {Role} » supprimé ; {N} compte(s) basculé(s) vers « {Remplacement} ».",
            role.Name, reaffectes, remplacement?.Name);
        return new RoleSuppressionResultDto(role.Name, remplacement?.Name, reaffectes);
    }

    internal static bool EstSysteme(string nom) => PlanningRoleSeed.NomsSysteme.Contains(nom.Trim());

    private static string ValiderNom(string? nom)
    {
        var n = Regex.Replace(nom ?? string.Empty, @"\s+", " ").Trim();
        if (!NomValide.IsMatch(n))
            throw new RoleRegleException("Nom de rôle invalide : 2 à 50 caractères (lettres, chiffres, espaces, - _ . ').");
        return n;
    }

    private static string Description(string? description, string nom)
    {
        var d = (description ?? string.Empty).Trim();
        if (d.Length == 0) d = nom;
        return d.Length > 200 ? d[..200] : d;
    }

    /// <summary>Compare sans casse, accents ni espaces : « Superviseur » = « superviseur ».</summary>
    internal static string Cle(string nom)
    {
        var sb = new StringBuilder();
        foreach (var c in nom.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private async Task VerifierNomLibreAsync(string nom, int? saufId, CancellationToken ct)
    {
        var cle = Cle(nom);
        if (PlanningRoleSeed.NomsSysteme.Any(s => Cle(s) == cle) && saufId is null)
            throw new RoleRegleException("Ce nom correspond à un rôle système existant.");
        var noms = await context.Roles.Where(r => saufId == null || r.Id != saufId).Select(r => r.Name).ToListAsync(ct);
        if (noms.Any(n => Cle(n) == cle) || PlanningRoleSeed.NomsSysteme.Any(s => Cle(s) == cle))
            throw new RoleRegleException("Un rôle porte déjà ce nom.");
    }

    private async Task AppelerAuthAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            var client = httpFactory.CreateClient(AuthClient);
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body);
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Service de connexion injoignable ({Url}).", url);
            throw new RoleRegleException("Le service de connexion ne répond pas : rien n'a été modifié. Réessayez.");
        }
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Auth {Status} sur {Url} : {Body}", (int)response.StatusCode, url, detail);
        throw new RoleRegleException((int)response.StatusCode is 404 or 405
            ? "Le service de connexion n'est pas à jour (v13) : rien n'a été modifié."
            : "Le service de connexion a refusé la modification : rien n'a été modifié.");
    }
}
