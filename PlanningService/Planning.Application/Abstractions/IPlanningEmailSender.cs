namespace Planning.Application.Abstractions;

public interface IPlanningEmailSender
{
    bool IsConfigured { get; }

    Task SendHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>Comme SendHtmlAsync, mais lève si ni clé API ni hôte SMTP n’est configuré.</summary>
    Task SendRequiredHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

