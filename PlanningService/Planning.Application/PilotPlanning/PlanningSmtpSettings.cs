using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;

namespace Planning.Application.PilotPlanning;

public sealed record PlanningSmtpConnection(
    string Host,
    int Port,
    string? User,
    string? Password,
    bool EnableSsl);

/// <summary>
/// Connexion SMTP : ligne Admin (base) si hôte renseigné, sinon variables Smtp:* / env.
/// </summary>
public static class PlanningSmtpSettings
{
    public const int DefaultPort = 587;
    public const int MaxHostLength = 256;
    public const int MaxUserLength = 256;
    public const int MaxPasswordLength = 512;

    public static PlanningSmtpConnection? Resolve(
        string? dbHost,
        int dbPort,
        string? dbUser,
        string? dbPassword,
        bool dbEnableSsl,
        string? envHost,
        int envPort,
        string? envUser,
        string? envPassword,
        bool envEnableSsl)
    {
        var host = TrimOrNull(dbHost, MaxHostLength);
        if (host != null)
        {
            return new PlanningSmtpConnection(
                host,
                ClampPort(dbPort),
                TrimOrNull(dbUser, MaxUserLength),
                TrimOrNull(dbPassword, MaxPasswordLength),
                dbEnableSsl);
        }

        host = TrimOrNull(envHost, MaxHostLength);
        if (host == null)
            return null;

        return new PlanningSmtpConnection(
            host,
            ClampPort(envPort),
            TrimOrNull(envUser, MaxUserLength),
            TrimOrNull(envPassword, MaxPasswordLength),
            envEnableSsl);
    }

    public static string? NormalizeHostForSave(string? value) => TrimOrNull(value, MaxHostLength);

    public static string? NormalizeUserForSave(string? value) => TrimOrNull(value, MaxUserLength);

    public static int ClampPort(int port) =>
        port is < 1 or > 65535 ? DefaultPort : port;

    public static string? ApplyPassword(string? existing, string? incoming)
    {
        var normalized = NormalizeSecret(incoming);
        if (string.IsNullOrEmpty(normalized))
            return existing;
        return normalized.Length <= MaxPasswordLength ? normalized : normalized[..MaxPasswordLength];
    }

    /// <summary>Retire espaces / retours (mot de passe d’application Gmail souvent collé « xxxx xxxx xxxx xxxx »).</summary>
    public static string? NormalizeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var compact = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        return string.IsNullOrEmpty(compact) ? null : compact;
    }

    public static string MapSendFailure(string? raw)
    {
        var text = raw ?? string.Empty;
        if (text.Contains("Application-specific password", StringComparison.OrdinalIgnoreCase)
            || text.Contains("InvalidSecondFactor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("5.7.9", StringComparison.OrdinalIgnoreCase))
        {
            return "Gmail refuse le mot de passe du compte. Créez un mot de passe d’application "
                + "(compte Google → Sécurité → Validation en 2 étapes → Mots de passe des applications) "
                + "et collez-le dans le champ mot de passe SMTP — pas le mot de passe de connexion Gmail.";
        }

        return string.IsNullOrWhiteSpace(text) ? "Échec de l’envoi SMTP." : text.Trim();
    }

    public static void ApplyToEntity(PlanningAutoGenerateSettings entity, AutoGenerateSettingsDto dto)
    {
        entity.SmtpHost = NormalizeHostForSave(dto.SmtpHost);
        entity.SmtpPort = ClampPort(dto.SmtpPort);
        entity.SmtpUser = NormalizeUserForSave(dto.SmtpUser);
        entity.SmtpEnableSsl = dto.SmtpEnableSsl;
        entity.SmtpPassword = ApplyPassword(entity.SmtpPassword, dto.SmtpPassword);
    }

    public static void WriteToDto(PlanningAutoGenerateSettings entity, AutoGenerateSettingsDto dto)
    {
        dto.SmtpHost = entity.SmtpHost;
        dto.SmtpPort = entity.SmtpPort <= 0 ? DefaultPort : entity.SmtpPort;
        dto.SmtpUser = entity.SmtpUser;
        dto.SmtpPassword = null;
        dto.SmtpEnableSsl = entity.SmtpEnableSsl;
        dto.SmtpPasswordSet = !string.IsNullOrEmpty(entity.SmtpPassword);
    }

    public static string? TrimOrNull(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
