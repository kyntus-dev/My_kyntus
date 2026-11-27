namespace Planning.Application.Abstractions;

/// <summary>v87 — Valeurs d'un jour pour l'accueil (clé → valeur).</summary>
public sealed record JourIndicateursAccueil(DateOnly Jour, IReadOnlyDictionary<string, decimal> Valeurs);

/// <summary>
/// v87 — Historique des indicateurs de l'accueil, strictement personnel : chaque personne enregistre et
/// relit uniquement ses propres valeurs. Aucun calcul métier, aucune autre donnée n'est lue.
/// </summary>
public interface IAccueilIndicateursService
{
    /// <summary>Enregistre (ou met à jour) les valeurs du jour de la personne ; retourne le nombre retenu.</summary>
    Task<int> EnregistrerAsync(Guid subjectId, DateOnly jour, IReadOnlyDictionary<string, decimal> valeurs, CancellationToken ct = default);

    /// <summary>Les valeurs de la personne sur les <paramref name="jours"/> derniers jours (120 au plus).</summary>
    Task<IReadOnlyList<JourIndicateursAccueil>> LireAsync(Guid subjectId, DateOnly aujourdhui, int jours, CancellationToken ct = default);
}
