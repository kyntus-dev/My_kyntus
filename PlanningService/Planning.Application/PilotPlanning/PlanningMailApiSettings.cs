using System.Text.Json;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;

namespace Planning.Application.PilotPlanning;

public sealed record PlanningMailApiRequest(
    string Url,
    string HeaderName,
    string HeaderValue,
    string JsonBody);

/// <summary>Envoi HTTP par clé API (Brevo / SendGrid) — pas le mot de passe d’une boîte mail.</summary>
public static class PlanningMailApiSettings
{
    public const string ModeSmtp = "Smtp";
    public const string ModeApiKey = "ApiKey";
    public const string ProviderBrevo = "Brevo";
    public const string ProviderSendGrid = "SendGrid";
    public const int MaxKeyLength = 512;

    public static string NormalizeMode(string? value) =>
        string.Equals(value?.Trim(), ModeApiKey, StringComparison.OrdinalIgnoreCase)
            ? ModeApiKey
            : ModeSmtp;

    public static string NormalizeProvider(string? value) =>
        string.Equals(value?.Trim(), ProviderSendGrid, StringComparison.OrdinalIgnoreCase)
            ? ProviderSendGrid
            : ProviderBrevo;

    public static bool IsApiKeyMode(string? mode) => NormalizeMode(mode) == ModeApiKey;

    public static bool HasApiKey(string? key) => !string.IsNullOrWhiteSpace(key);

    public static void ApplyToEntity(PlanningAutoGenerateSettings entity, AutoGenerateSettingsDto dto)
    {
        entity.MailSendMode = NormalizeMode(dto.MailSendMode);
        entity.MailApiProvider = NormalizeProvider(dto.MailApiProvider);
        entity.MailApiKey = PlanningSmtpSettings.ApplyPassword(entity.MailApiKey, dto.MailApiKey);
        if (entity.MailApiKey is { Length: > MaxKeyLength } key)
            entity.MailApiKey = key[..MaxKeyLength];
    }

    public static void WriteToDto(PlanningAutoGenerateSettings entity, AutoGenerateSettingsDto dto)
    {
        dto.MailSendMode = NormalizeMode(entity.MailSendMode);
        dto.MailApiProvider = NormalizeProvider(entity.MailApiProvider);
        dto.MailApiKey = null;
        dto.MailApiKeySet = !string.IsNullOrEmpty(entity.MailApiKey);
    }

    public static PlanningMailApiRequest BuildRequest(
        string provider,
        string apiKey,
        string fromEmail,
        string? fromName,
        string toEmail,
        string subject,
        string htmlBody)
    {
        var key = apiKey.Trim();
        var from = fromEmail.Trim();
        var to = toEmail.Trim();
        var name = string.IsNullOrWhiteSpace(fromName) ? null : fromName.Trim();

        if (NormalizeProvider(provider) == ProviderSendGrid)
        {
            var payload = new Dictionary<string, object?>
            {
                ["personalizations"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["to"] = new object[] { new Dictionary<string, string> { ["email"] = to } },
                    },
                },
                ["from"] = name is null
                    ? new Dictionary<string, string> { ["email"] = from }
                    : new Dictionary<string, string> { ["email"] = from, ["name"] = name },
                ["subject"] = subject,
                ["content"] = new object[]
                {
                    new Dictionary<string, string> { ["type"] = "text/html", ["value"] = htmlBody },
                },
            };
            return new PlanningMailApiRequest(
                "https://api.sendgrid.com/v3/mail/send",
                "Authorization",
                "Bearer " + key,
                JsonSerializer.Serialize(payload));
        }

        var brevo = new Dictionary<string, object?>
        {
            ["sender"] = name is null
                ? new Dictionary<string, string> { ["email"] = from }
                : new Dictionary<string, string> { ["email"] = from, ["name"] = name },
            ["to"] = new object[] { new Dictionary<string, string> { ["email"] = to } },
            ["subject"] = subject,
            ["htmlContent"] = htmlBody,
        };
        return new PlanningMailApiRequest(
            "https://api.brevo.com/v3/smtp/email",
            "api-key",
            key,
            JsonSerializer.Serialize(brevo));
    }
}
