using System.Text;

namespace Planning.Application.NotificationEmail;

/// <summary>Contenu d'une notification envoyée aussi par e-mail.</summary>
/// <param name="Source">Module d'origine (planning, formation, conge, prime, parrainage…).</param>
/// <param name="Title">Titre court, repris dans l'objet.</param>
/// <param name="Body">Texte de la notification, identique à celui affiché dans l'application.</param>
/// <param name="Route">Écran de l'application à ouvrir (ex. « /mes-conges »), facultatif.</param>
/// <param name="DedupKey">Identifiant de l'événement : un seul e-mail par destinataire, même si l'événement est rejoué.</param>
public sealed record NotificationEmailContent(
    string Source,
    string Title,
    string Body,
    string? Route = null,
    string? DedupKey = null);

/// <summary>Destinataires, par identifiant Planning, GUID employé, id Auth ou rôle.</summary>
public sealed record NotificationEmailRecipients(
    IReadOnlyCollection<int>? UserIds = null,
    IReadOnlyCollection<Guid>? EmployeeIds = null,
    IReadOnlyCollection<int>? AuthUserIds = null,
    IReadOnlyCollection<string>? Roles = null)
{
    public bool IsEmpty =>
        (UserIds?.Count ?? 0) == 0
        && (EmployeeIds?.Count ?? 0) == 0
        && (AuthUserIds?.Count ?? 0) == 0
        && (Roles?.Count ?? 0) == 0;
}

public static class NotificationEmailSources
{
    public const string Planning = "planning";
    public const string Formation = "formation";
    public const string Contract = "contract";
    public const string Conge = "conge";
    public const string Reclamation = "reclamation";
    public const string Proposition = "proposition";

    public static string Label(string? source) => (source ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Planning => "Planning",
        Formation => "Formation",
        Contract => "Contrat",
        Conge => "Congés",
        Reclamation => "Réclamation",
        Proposition => "Proposition",
        "prime" => "PRIME",
        "parrainage" => "Parrainage",
        "documentation" => "Documentation",
        "newsletter" => "Communication",
        var other when other.Length > 0 => char.ToUpperInvariant(other[0]) + other[1..],
        _ => "Notification",
    };
}

/// <summary>Gabarit HTML sobre (compatible Outlook) commun à toutes les notifications.</summary>
public static class NotificationEmailComposer
{
    public const int MaxSubjectLength = 300;

    public static string Subject(string title)
    {
        var subject = $"My Kyntus — {Clean(title, "Notification")}";
        return subject.Length <= MaxSubjectLength ? subject : subject[..MaxSubjectLength];
    }

    /// <summary>Lien absolu vers l'écran ; null si l'adresse publique de l'application n'est pas configurée.</summary>
    public static string? BuildLink(string? publicAppUrl, string? route)
    {
        var baseUrl = publicAppUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        var path = route?.Trim();
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//"))
            return baseUrl + "/";
        return baseUrl + path;
    }

    public static string BuildHtml(string? firstName, string source, string title, string body, string? link)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#f3f5f9;font-family:Calibri,Arial,sans-serif;color:#1f2937;\">");
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #dbe3ef;border-radius:8px;\">");
        sb.Append("<tr><td style=\"padding:16px 24px;background:#1E3A5F;border-radius:8px 8px 0 0;color:#ffffff;font-size:13px;letter-spacing:.04em;\">")
            .Append("MY KYNTUS · ").Append(Enc(NotificationEmailSources.Label(source).ToUpperInvariant()))
            .Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:24px;\">");
        var name = firstName?.Trim();
        sb.Append("<p style=\"margin:0 0 12px 0;font-size:15px;\">")
            .Append(string.IsNullOrEmpty(name) ? "Bonjour," : $"Bonjour {Enc(name)},")
            .Append("</p>");
        sb.Append("<p style=\"margin:0 0 8px 0;font-size:17px;font-weight:bold;color:#1E3A5F;\">")
            .Append(Enc(Clean(title, "Notification"))).Append("</p>");
        sb.Append("<p style=\"margin:0 0 20px 0;font-size:15px;line-height:1.5;\">")
            .Append(Enc(Clean(body, string.Empty)).Replace("\n", "<br>")).Append("</p>");
        if (!string.IsNullOrEmpty(link))
        {
            sb.Append("<p style=\"margin:0 0 20px 0;\"><a href=\"").Append(Enc(link))
                .Append("\" style=\"display:inline-block;padding:10px 18px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-size:14px;\">Ouvrir dans My Kyntus</a></p>");
        }
        sb.Append("<p style=\"margin:0;font-size:12px;color:#6b7280;\">Cette notification est aussi visible dans l'application. ")
            .Append("Vous pouvez désactiver ces e-mails dans vos paramètres My Kyntus.</p>");
        sb.Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string Clean(string? value, string fallback)
    {
        var v = (value ?? string.Empty).Replace("\r\n", "\n").Trim();
        return v.Length == 0 ? fallback : v;
    }

    /// <summary>Échappe le HTML sans convertir les accents en entités (texte lisible dans la source du message).</summary>
    private static string Enc(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
}

/// <summary>Règles propres aux notifications Planning stockées (table PlanningNotifications).</summary>
public static class PlanningNotificationEmailRules
{
    /// <summary>
    /// « Votre planning … est disponible / a été mis à jour » : l'agent reçoit déjà son planning
    /// complet par e-mail, la notification n'est donc pas envoyée une deuxième fois.
    /// </summary>
    public static bool IsCoveredByPlanningEmail(int? weeklyPlanningId, string? message) =>
        weeklyPlanningId is not null
        && (message ?? string.Empty).TrimStart().StartsWith("Votre planning ", StringComparison.OrdinalIgnoreCase);

    public static (string Source, string Title) Classify(string? weekCode, string? subServiceName)
    {
        var code = (weekCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.StartsWith("TRAIN", StringComparison.Ordinal) || code.StartsWith("INIT-", StringComparison.Ordinal))
            return (NotificationEmailSources.Formation, "Formation");
        if (code.StartsWith("SAT-IMBALANCE-", StringComparison.Ordinal))
            return (NotificationEmailSources.Planning, "Déséquilibre samedi");
        var sub = (subServiceName ?? string.Empty).Trim();
        if (sub.Contains("demande", StringComparison.OrdinalIgnoreCase)
            || sub.Contains("renfort", StringComparison.OrdinalIgnoreCase))
            return (NotificationEmailSources.Planning, sub);
        return (NotificationEmailSources.Planning, string.IsNullOrEmpty(sub) ? "Planning" : $"Planning — {sub}");
    }
}
