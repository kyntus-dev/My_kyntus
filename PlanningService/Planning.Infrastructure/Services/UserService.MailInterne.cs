using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Application.Users;
using Planning.Infrastructure.Security;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v47 — Mail interne facultatif : collaborateurs créés sans adresse, puis attribution par import
/// (rapprochement nom / prénom). Voir <see cref="MailInterne"/> et <see cref="RapprochementMails"/>.
/// </summary>
public partial class UserService
{
    /// <summary>
    /// Création : adresse vide = mail interne non encore créé → adresse provisoire réservée.
    /// Le domaine réservé ne peut jamais être saisi à la main.
    /// </summary>
    private static void PreparerMailInterneCreation(CreateUserDto dto)
    {
        var saisi = (dto.Email ?? string.Empty).Trim();
        if (saisi.Length == 0)
        {
            dto.Email = MailInterne.Generer(dto.FirstName, dto.LastName);
            return;
        }
        if (MailInterne.EstProvisoire(saisi))
            throw new InvalidOperationException(
                $"Le domaine « {MailInterne.DomaineProvisoire} » est réservé à l'application.");
        if (!MailInterne.FormatValide(saisi))
            throw new InvalidOperationException($"L'adresse « {saisi} » n'est pas une adresse e-mail valide.");
        dto.Email = saisi;
    }

    /// <summary>Mise à jour : champ vide = adresse inchangée ; domaine réservé refusé.</summary>
    private static string PreparerMailInterneMiseAJour(string? saisi, string actuel)
    {
        var value = (saisi ?? string.Empty).Trim();
        if (value.Length == 0)
            return actuel;
        if (MailInterne.EstProvisoire(value))
        {
            if (string.Equals(value, actuel, StringComparison.OrdinalIgnoreCase))
                return actuel;
            throw new InvalidOperationException(
                $"Le domaine « {MailInterne.DomaineProvisoire} » est réservé à l'application.");
        }
        if (!MailInterne.FormatValide(value))
            throw new InvalidOperationException($"L'adresse « {value} » n'est pas une adresse e-mail valide.");
        return value;
    }

    public async Task<List<AgentSansMailDto>> ListerAgentsSansMailAsync(CancellationToken ct = default)
    {
        var suffixe = "@" + MailInterne.DomaineProvisoire;
        var rows = await _context.Users
            .AsNoTracking()
            .Where(u => u.Email.EndsWith(suffixe) || u.Email == string.Empty)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new AgentSansMailDto(
                u.Id,
                u.Guid,
                u.LastName,
                u.FirstName,
                u.Role != null ? u.Role.Name : string.Empty,
                u.SubService != null
                    ? (u.SubService.Service != null
                        ? u.SubService.Service.Name + " · " + u.SubService.Name
                        : u.SubService.Name)
                    : null,
                u.IsActive,
                u.IdTechnicien,
                u.HtelCode,
                u.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<Dictionary<string, string>> ListerMailsUtilisesAsync(CancellationToken ct = default)
    {
        var suffixe = "@" + MailInterne.DomaineProvisoire;
        var rows = await _context.Users
            .AsNoTracking()
            .Where(u => u.Email != string.Empty && !u.Email.EndsWith(suffixe))
            .Select(u => new { u.Email, u.LastName, u.FirstName })
            .ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
            map[MailInterne.Cle(r.Email)] = $"{r.LastName} {r.FirstName}".Trim();
        return map;
    }

    public async Task<AttributionMailResultDto> AttribuerMailInterneAsync(
        int userId,
        string email,
        bool creerCompte,
        CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return new AttributionMailResultDto(userId, "", "", email, false, "Collaborateur introuvable.", null);

        var nom = user.LastName;
        var prenom = user.FirstName;
        var cible = (email ?? string.Empty).Trim();

        AttributionMailResultDto Echec(string message) =>
            new(userId, nom, prenom, cible, false, message, null);

        if (!MailInterne.FormatValide(cible))
            return Echec("Adresse e-mail invalide.");
        if (MailInterne.EstProvisoire(cible))
            return Echec("Domaine réservé à l'application.");
        if (MailInterne.EstReel(user.Email))
            return Echec($"Ce collaborateur a déjà le mail interne {user.Email}.");
        if (!await IsEmailUniqueAsync(cible, userId))
            return Echec($"L'adresse « {cible} » est déjà utilisée.");

        var ancien = user.Email;
        user.Email = cible;
        await _context.SaveChangesAsync(ct);

        // L'annuaire (Directory) fait foi pour l'organisation : son employé porte la même adresse.
        var annuaireOk = await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user);
        if (!annuaireOk)
        {
            user.Email = ancien;
            await _context.SaveChangesAsync(ct);
            return Echec("La mise à jour de l'annuaire a échoué — aucune modification conservée.");
        }

        string? motDePasse = null;
        if (creerCompte)
        {
            motDePasse = PasswordGenerator.Generate();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(motDePasse);
            await _context.SaveChangesAsync(ct);
            await SyncToAuthServiceAsync(user, motDePasse);
            if (!user.AuthUserId.HasValue)
            {
                _logger.LogWarning(
                    "Mail interne attribué à {Email} mais compte de connexion non créé.", cible);
                return new AttributionMailResultDto(
                    userId, nom, prenom, cible, true,
                    "Adresse enregistrée ; compte de connexion à créer (réinitialisez le mot de passe).",
                    null);
            }
        }

        _logger.LogInformation(
            "Mail interne attribué : UserId={UserId} {Ancien} -> {Email} (compte créé : {Compte}).",
            userId, ancien, cible, creerCompte);

        return new AttributionMailResultDto(userId, nom, prenom, cible, true, null, motDePasse);
    }
}
