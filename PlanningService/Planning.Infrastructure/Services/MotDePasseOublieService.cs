using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.Users;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v56 — « Mot de passe oublié » : la personne demande elle-même un nouveau mot de passe depuis
/// l'écran de connexion, sans passer par un administrateur.
/// <para>
/// Le nouveau mot de passe est envoyé <b>uniquement</b> à l'adresse professionnelle enregistrée sur
/// la fiche : rien n'est affiché à l'écran, et la réponse est toujours la même, que l'adresse existe
/// ou non (pour ne pas révéler qui a un compte). Le mot de passe n'est remplacé que si l'envoi est
/// possible : sans serveur d'e-mail configuré, la demande est refusée plutôt que de laisser
/// quelqu'un dehors.
/// </para>
/// </summary>
public interface IMotDePasseOublieService
{
    /// <summary>Traite la demande. Renvoie vrai si un e-mail a réellement été envoyé.</summary>
    Task<bool> DemanderAsync(string? email, string? adresseIp, CancellationToken ct = default);
}

public sealed class MotDePasseOublieService(
    AppDbContext db,
    IUserService users,
    IPlanningEmailSender emailSender,
    ILogger<MotDePasseOublieService> logger) : IMotDePasseOublieService
{
    /// <summary>Une demande par adresse toutes les 5 minutes, 20 demandes par heure et par poste.</summary>
    private static readonly TimeSpan DelaiEntreDeuxDemandes = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> DernieresDemandes = new();

    public async Task<bool> DemanderAsync(string? email, string? adresseIp, CancellationToken ct = default)
    {
        var adresse = (email ?? string.Empty).Trim();
        if (adresse.Length == 0 || !adresse.Contains('@', StringComparison.Ordinal))
            return false;

        if (!emailSender.IsConfigured)
        {
            logger.LogWarning("Mot de passe oublié : aucun serveur d'e-mail configuré, demande refusée.");
            throw new InvalidOperationException(
                "L'envoi d'e-mails n'est pas configuré sur cette installation : contactez les RH "
                + "pour la réinitialisation de votre mot de passe.");
        }

        if (!Autorise(adresse, adresseIp))
        {
            logger.LogInformation("Mot de passe oublié : demande trop rapprochée pour {Email}.", Masquer(adresse));
            return false;
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Email.ToLower() == adresse.ToLower())
            .Select(u => new { u.Id, u.Email, u.FirstName })
            .FirstOrDefaultAsync(ct);

        // Compte inconnu, désactivé ou sans mail interne : même réponse, rien n'est fait.
        if (user is null || MailInterne.EstProvisoire(user.Email))
        {
            logger.LogInformation("Mot de passe oublié : aucune suite pour {Email}.", Masquer(adresse));
            return false;
        }

        var resultat = await users.ResetPasswordAsync(user.Id, ct)
            ?? throw new InvalidOperationException("Compte introuvable.");

        try
        {
            await emailSender.SendRequiredHtmlAsync(
                user.Email,
                "My Kyntus — votre nouveau mot de passe",
                Corps(user.FirstName, resultat.TemporaryPassword ?? string.Empty),
                ct);
        }
        catch (Exception ex)
        {
            // Le mot de passe a été changé mais l'e-mail n'est pas parti : le dire franchement,
            // sinon la personne reste dehors sans comprendre pourquoi.
            logger.LogError(ex, "Mot de passe oublié : envoi impossible à {Email}.", Masquer(user.Email));
            throw new InvalidOperationException(
                "Le mot de passe a été réinitialisé mais l'e-mail n'a pas pu être envoyé : "
                + "contactez les RH pour le récupérer.");
        }

        logger.LogInformation("Mot de passe oublié : nouveau mot de passe envoyé à {Email}.", Masquer(user.Email));
        return true;
    }

    private static bool Autorise(string email, string? ip)
    {
        var maintenant = DateTimeOffset.UtcNow;
        if (DernieresDemandes.Count > 5000) DernieresDemandes.Clear();

        var cles = new[] { "mail:" + email.ToLowerInvariant(), "ip:" + (ip ?? "?") };
        foreach (var cle in cles)
        {
            if (DernieresDemandes.TryGetValue(cle, out var derniere)
                && maintenant - derniere < DelaiEntreDeuxDemandes)
                return false;
        }
        foreach (var cle in cles) DernieresDemandes[cle] = maintenant;
        return true;
    }

    private static string Masquer(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    private static string Corps(string prenom, string motDePasse) => $"""
        <p>Bonjour {WebUtility.HtmlEncode(prenom)},</p>
        <p>Voici votre nouveau mot de passe de connexion à My Kyntus :</p>
        <p style="font-size:18px;font-weight:bold;letter-spacing:1px">{WebUtility.HtmlEncode(motDePasse)}</p>
        <p>Connectez-vous avec ce mot de passe, puis changez-le depuis
        <strong>Mon espace › Changer mon mot de passe</strong>.</p>
        <p>Si vous n'êtes pas à l'origine de cette demande, prévenez les RH : votre ancien mot de
        passe ne fonctionne plus.</p>
        <p>— My Kyntus</p>
        """;
}
