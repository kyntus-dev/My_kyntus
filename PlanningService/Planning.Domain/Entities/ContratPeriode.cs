namespace Planning.Domain.Entities;

/// <summary>
/// v64 — Période sous contrat d'un employé : de la date d'entrée réelle à la date de sortie effective.
/// En dehors, aucun shift n'est généré, aucune présence ni absence n'est calculée, et les cases sont grisées.
///
/// Entrée : date d'entrée de la fiche RH, sinon date d'embauche RH. La date d'embauche du compte n'est
/// pas utilisée : pour les comptes venus de l'annuaire elle vaut la date de synchronisation, pas
/// l'entrée réelle (l'utiliser griserait à tort l'historique).
/// Sortie : date de sortie de la fiche, ignorée si elle précède la dernière entrée connue (réembauche).
/// </summary>
public readonly record struct ContratPeriode(DateOnly? Entree, DateOnly? Sortie)
{
    public static readonly ContratPeriode SansBorne = new(null, null);

    /// <param name="dateEmbaucheCompte">Conservé pour compatibilité ; volontairement ignoré (voir le résumé).</param>
    public static ContratPeriode De(UserHrProfile? profil, DateTime? dateEmbaucheCompte = null)
    {
        _ = dateEmbaucheCompte;
        var entree = profil?.DateEntree ?? profil?.DateEmbauche;
        var sortie = profil?.DateSortie;
        if (sortie is DateOnly s)
        {
            // Réembauche : une sortie antérieure à la dernière entrée connue n'est plus en vigueur.
            var derniereEntree = new[] { profil?.DateEntree, profil?.DateEmbauche }
                .Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max();
            if (derniereEntree != default && s < derniereEntree) sortie = null;
        }
        return new ContratPeriode(entree, sortie);
    }

    public static ContratPeriode De(User user, UserHrProfile? profil) => De(profil, user.HireDate);

    /// <summary>Le jour est-il couvert par le contrat ?</summary>
    public bool Couvre(DateOnly jour) =>
        (Entree is not DateOnly e || jour >= e) && (Sortie is not DateOnly s || jour <= s);

    public bool AvantEntree(DateOnly jour) => Entree is DateOnly e && jour < e;

    public bool ApresSortie(DateOnly jour) => Sortie is DateOnly s && jour > s;

    /// <summary>Motif lisible d'un jour hors contrat (null si le jour est couvert).</summary>
    public string? MotifHorsContrat(DateOnly jour) =>
        AvantEntree(jour) ? $"avant la date d'entrée ({Entree:dd/MM/yyyy})"
        : ApresSortie(jour) ? $"après la date de sortie ({Sortie:dd/MM/yyyy})"
        : null;
}
