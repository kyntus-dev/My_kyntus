using System.Net.Mail;

namespace Planning.Application.PilotPlanning;

/// <summary>
/// From = adresse RH/admin si renseignée et valide, sinon Smtp:From / Smtp:User.
/// Pas d’identifiants SMTP ici.
/// </summary>
public static class PlanningSenderAddress
{
    public const int MaxEmailLength = 256;
    public const int MaxDisplayNameLength = 128;
    public const string FallbackAddress = "planning@kyntus.local";

    public static string? NormalizeEmailOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxEmailLength)
            return null;
        if (!PilotPlanningEmailComposer.IsValidPersonalEmail(trimmed))
            return null;
        try
        {
            return new MailAddress(trimmed).Address;
        }
        catch
        {
            return null;
        }
    }

    public static string? NormalizeEmailForSave(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return NormalizeEmailOrNull(trimmed)
            ?? throw new ArgumentException("Adresse RH invalide.");
    }

    public static string? NormalizeDisplayName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= MaxDisplayNameLength
            ? trimmed
            : trimmed[..MaxDisplayNameLength];
    }

    public static (string Address, string? DisplayName) Resolve(
        string? adminEmail,
        string? adminDisplayName,
        string? smtpFrom,
        string? smtpUser)
    {
        var displayName = NormalizeDisplayName(adminDisplayName);
        var fromRh = NormalizeEmailOrNull(adminEmail);
        if (fromRh != null)
            return (fromRh, displayName);

        var fromEnv = FirstNonEmpty(smtpFrom, smtpUser);
        return (fromEnv ?? FallbackAddress, displayName);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                return trimmed;
        }

        return null;
    }
}
