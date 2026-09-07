using System.Net.Http.Headers;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Planning.Application.Abstractions;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

public sealed class MailKitPlanningEmailSender(
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    AppDbContext db,
    ILogger<MailKitPlanningEmailSender> logger) : IPlanningEmailSender
{
    public bool IsConfigured => ResolveFromEnvOnly() is not null;

    public Task SendHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        SendCoreAsync(to, subject, htmlBody, requireConfigured: false, ct);

    public Task SendRequiredHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        SendCoreAsync(to, subject, htmlBody, requireConfigured: true, ct);

    private async Task SendCoreAsync(
        string to,
        string subject,
        string htmlBody,
        bool requireConfigured,
        CancellationToken ct)
    {
        var settings = await db.PlanningAutoGenerateSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == PlanningAutoGenerateSettings.SingletonId, ct);

        var (fromAddress, displayName) = await ResolveFromAsync(settings, ct);

        if (PlanningMailApiSettings.IsApiKeyMode(settings?.MailSendMode)
            && PlanningMailApiSettings.HasApiKey(settings?.MailApiKey))
        {
            await SendViaApiAsync(settings!, fromAddress, displayName, to, subject, htmlBody, ct);
            return;
        }

        var conn = ResolveConnection(settings);
        if (conn is null)
        {
            if (requireConfigured)
                throw new InvalidOperationException(
                    "Envoi mail non configuré. Choisissez « Clé API » (Brevo / SendGrid) ou renseignez le SMTP dans Auto-génération.");
            logger.LogInformation("SMTP / API mail non configuré — email planning ignoré pour {To}.", to);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(CreateFromMailbox(fromAddress, displayName));
        message.To.Add(MailboxAddress.Parse(to.Trim()));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socket = conn.EnableSsl
                ? (conn.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;
            await client.ConnectAsync(conn.Host, conn.Port, socket, ct);
            if (!string.IsNullOrWhiteSpace(conn.User))
            {
                var password = PlanningSmtpSettings.NormalizeSecret(conn.Password) ?? string.Empty;
                await client.AuthenticateAsync(conn.User, password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException ex)
        {
            throw new InvalidOperationException(PlanningSmtpSettings.MapSendFailure(ex.Message), ex);
        }
        catch (SmtpCommandException ex)
        {
            throw new InvalidOperationException(PlanningSmtpSettings.MapSendFailure(ex.Message), ex);
        }
    }

    private async Task<(string Address, string? DisplayName)> ResolveFromAsync(
        PlanningAutoGenerateSettings? settings,
        CancellationToken ct)
    {
        var rhEmail = settings?.SenderFromEmail;
        var rhDisplayName = settings?.SenderDisplayName;
        if (PlanningSenderAddress.NormalizeEmailOrNull(rhEmail) is null
            && settings?.UpdatedByUserId is int rhUserId)
        {
            var rhUser = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == rhUserId && u.IsActive)
                .Select(u => new { u.Email, u.FirstName, u.LastName })
                .FirstOrDefaultAsync(ct);
            rhEmail = rhUser?.Email;
            if (string.IsNullOrWhiteSpace(rhDisplayName) && rhUser is not null)
                rhDisplayName = $"{rhUser.FirstName} {rhUser.LastName}".Trim();
        }

        return PlanningSenderAddress.Resolve(
            rhEmail,
            rhDisplayName,
            configuration["Smtp:From"],
            settings?.SmtpUser);
    }

    private async Task SendViaApiAsync(
        PlanningAutoGenerateSettings settings,
        string fromAddress,
        string? displayName,
        string to,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        var request = PlanningMailApiSettings.BuildRequest(
            settings.MailApiProvider,
            settings.MailApiKey!,
            fromAddress,
            displayName,
            to,
            subject,
            htmlBody);

        using var msg = new HttpRequestMessage(HttpMethod.Post, request.Url);
        msg.Headers.TryAddWithoutValidation(request.HeaderName, request.HeaderValue);
        msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        msg.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");

        var http = httpFactory.CreateClient("PlanningMailApi");
        using var response = await http.SendAsync(msg, ct);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var snippet = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase
            : body.Trim();
        if (snippet is { Length: > 400 })
            snippet = snippet[..400];
        throw new InvalidOperationException(
            $"Envoi {PlanningMailApiSettings.NormalizeProvider(settings.MailApiProvider)} refusé ({(int)response.StatusCode}). {snippet}");
    }

    private PlanningSmtpConnection? ResolveConnection(PlanningAutoGenerateSettings? settings) =>
        PlanningSmtpSettings.Resolve(
            settings?.SmtpHost,
            settings?.SmtpPort ?? PlanningSmtpSettings.DefaultPort,
            settings?.SmtpUser,
            settings?.SmtpPassword,
            settings?.SmtpEnableSsl ?? true,
            configuration["Smtp:Host"],
            configuration.GetValue("Smtp:Port", PlanningSmtpSettings.DefaultPort),
            configuration["Smtp:User"],
            configuration["Smtp:Password"],
            configuration.GetValue("Smtp:EnableSsl", true));

    private PlanningSmtpConnection? ResolveFromEnvOnly() =>
        PlanningSmtpSettings.Resolve(
            null,
            PlanningSmtpSettings.DefaultPort,
            null,
            null,
            true,
            configuration["Smtp:Host"],
            configuration.GetValue("Smtp:Port", PlanningSmtpSettings.DefaultPort),
            configuration["Smtp:User"],
            configuration["Smtp:Password"],
            configuration.GetValue("Smtp:EnableSsl", true));

    private static MailboxAddress CreateFromMailbox(string fromAddress, string? displayName)
    {
        if (MailboxAddress.TryParse(fromAddress, out var parsed))
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? parsed
                : new MailboxAddress(displayName, parsed.Address);
        }

        return string.IsNullOrWhiteSpace(displayName)
            ? MailboxAddress.Parse(fromAddress)
            : new MailboxAddress(displayName, fromAddress);
    }
}
