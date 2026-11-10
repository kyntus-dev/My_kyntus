namespace Planning.Application.Abstractions;

/// <summary>Indique si un moyen d'envoi (SMTP ou clé API) est configuré.</summary>
public interface IPlanningEmailReadiness
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);
}

/// <summary>Aucun SMTP ni clé API : l'e-mail reste en attente, ce n'est pas un échec d'envoi.</summary>
public sealed class MailNotConfiguredException(string message) : InvalidOperationException(message);
