using Planning.Application.NotificationEmail;

namespace Planning.Application.Abstractions;

/// <summary>
/// v62 — Notification affichée dans l'application (cloche), envoyée à la personne concernée après une
/// action (présence saisie, congé validé, document traité, réaffectation…). Persistée, poussée en temps
/// réel, et ouvrant la page concernée au clic.
/// </summary>
/// <param name="Categorie">presences, conges, documentation, affectation, planning…</param>
/// <param name="Titre">Titre court affiché en gras.</param>
/// <param name="Message">Phrase complète, compréhensible sans contexte.</param>
/// <param name="Lien">Page ouverte au clic (ex. « /mes-conges »).</param>
/// <param name="Cle">
/// Clé de regroupement : une notification non lue de même clé est mise à jour au lieu d'être doublée
/// (ex. plusieurs saisies sur la même journée).
/// </param>
public sealed record AppNotification(string Categorie, string Titre, string Message, string Lien, string? Cle = null);

public interface IAppNotifier
{
    /// <summary>
    /// Notifie les comptes actifs désignés. Ne lève jamais d'exception : une notification ne doit
    /// pas faire échouer l'action qui la déclenche. Renvoie le nombre de personnes notifiées.
    /// </summary>
    Task<int> NotifierAsync(
        NotificationEmailRecipients destinataires,
        AppNotification notification,
        IReadOnlyCollection<string>? rolesExclus = null,
        CancellationToken ct = default);
}
