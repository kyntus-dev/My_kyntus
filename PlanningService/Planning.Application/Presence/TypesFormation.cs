namespace Planning.Application.Presence;

/// <summary>
/// v48 — Type de formation d'un agent « En formation ».
/// <para>
/// <b>Contractuel</b> : la période de formation est prévue au contrat de travail.
/// <b>Non contractuel</b> : formation hors contrat (période d'essai, mise à niveau, renfort).
/// </para>
/// Le type est choisi dès la création de la fiche, dès que « En formation » est coché ;
/// il est effacé automatiquement quand l'agent passe en production.
/// </summary>
public static class TypesFormation
{
    public const string Contractuel = "contractuel";
    public const string NonContractuel = "non-contractuel";

    public static readonly IReadOnlyList<string> Tous = new[] { Contractuel, NonContractuel };

    /// <summary>Libellé affiché (tableaux, export paie).</summary>
    public static string Libelle(string? type) => Normaliser(type) switch
    {
        Contractuel => "Contractuel",
        NonContractuel => "Non contractuel",
        _ => string.Empty,
    };

    /// <summary>Valeur rangée : « contractuel », « non-contractuel » ou null.</summary>
    public static string? Normaliser(string? type)
    {
        var value = (type ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return value switch
        {
            "contractuel" or "contrat" => Contractuel,
            "non-contractuel" or "noncontractuel" or "hors-contrat" => NonContractuel,
            _ => null,
        };
    }

    public static bool EstValide(string? type) => Normaliser(type) is not null;
}
