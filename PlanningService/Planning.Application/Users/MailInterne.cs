using System.Globalization;
using System.Text;

namespace Planning.Application.Users;

/// <summary>
/// v47 — Mail interne facultatif à la création.
/// <para>
/// Un collaborateur créé sans adresse professionnelle reçoit une adresse <b>provisoire</b> sur un
/// domaine réservé (<see cref="DomaineProvisoire"/>) : la fiche reste unique en base (index
/// IX_Users_Email, annuaire Directory) sans inventer d'adresse plausible. Une adresse provisoire :
/// </para>
/// <list type="bullet">
///   <item>n'ouvre aucun compte de connexion ;</item>
///   <item>ne reçoit jamais d'e-mail ;</item>
///   <item>s'affiche « Sans mail interne » dans l'application ;</item>
///   <item>est remplacée par la vraie adresse lors de l'import (rapprochement nom / prénom).</item>
/// </list>
/// </summary>
public static class MailInterne
{
    /// <summary>Domaine réservé aux adresses provisoires. Jamais routable, jamais saisissable.</summary>
    public const string DomaineProvisoire = "sans-mail.kyntus.local";

    private const int LongueurMaxLocale = 48;

    /// <summary>Adresse absente ou provisoire : aucun mail interne réel.</summary>
    public static bool EstProvisoire(string? email)
    {
        var value = (email ?? string.Empty).Trim();
        return value.Length == 0
            || value.EndsWith("@" + DomaineProvisoire, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adresse réelle (ni vide, ni provisoire).</summary>
    public static bool EstReel(string? email) => !EstProvisoire(email);

    /// <summary>Adresse à afficher / publier : <c>null</c> si provisoire.</summary>
    public static string? Afficher(string? email) =>
        EstProvisoire(email) ? null : (email ?? string.Empty).Trim();

    /// <summary>
    /// Fabrique une adresse provisoire unique et lisible : <c>nom.prenom.ab12cd34@domaine réservé</c>.
    /// Le suffixe aléatoire garantit l'unicité même pour deux homonymes.
    /// </summary>
    public static string Generer(string? prenom, string? nom)
    {
        var slug = Slug(nom, prenom);
        var suffixe = Guid.NewGuid().ToString("N")[..8];
        var locale = string.IsNullOrEmpty(slug) ? $"sans-mail.{suffixe}" : $"{slug}.{suffixe}";
        return $"{locale}@{DomaineProvisoire}";
    }

    /// <summary>Format minimal d'une adresse réelle (un seul @, un point dans le domaine).</summary>
    public static bool FormatValide(string? email)
    {
        var value = (email ?? string.Empty).Trim();
        if (value.Length is < 6 or > 254) return false;
        if (value.Contains(' ')) return false;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')) return false;
        var domaine = value[(at + 1)..];
        var point = domaine.IndexOf('.');
        return point > 0 && point < domaine.Length - 1;
    }

    /// <summary>Clé de comparaison d'une adresse (minuscules, sans espaces).</summary>
    public static string Cle(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string Slug(string? nom, string? prenom)
    {
        var parts = new[] { nom, prenom }
            .Select(SansAccents)
            .Where(p => p.Length > 0)
            .ToArray();
        if (parts.Length == 0) return string.Empty;
        var slug = string.Join('.', parts);
        return slug.Length > LongueurMaxLocale ? slug[..LongueurMaxLocale].Trim('.') : slug;
    }

    private static string SansAccents(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
