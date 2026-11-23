using System.Globalization;
using System.Text;

namespace Planning.Domain.Entities;

/// <summary>
/// v64 — Type d'employé : salarié ou sous-traitant (auto-entrepreneur).
///
/// Une seule règle pour toute l'application (fiche, tableau des présences, paie, filtres, exports,
/// planning) : la valeur saisie sur la fiche fait foi. Pour une fiche antérieure à la v64 (type non
/// renseigné), le type est déduit comme avant : sous-traitant s'il a un numéro de carte
/// d'auto-entrepreneur, salarié sinon.
/// </summary>
public static class TypesEmploye
{
    public const string Salarie = "SALARIE";
    public const string SousTraitant = "SOUS_TRAITANT";

    public const string LibelleSalarie = "Salarié";
    public const string LibelleSousTraitant = "Sous-traitant";

    /// <summary>Code du type d'une fiche (jamais null).</summary>
    public static string Code(UserHrProfile? profil) =>
        Normaliser(profil?.TypeEmploye)
        ?? (string.IsNullOrWhiteSpace(profil?.NumeroCarteAutoentrepreneur) ? Salarie : SousTraitant);

    /// <summary>Même règle à partir des deux valeurs (requêtes en projection).</summary>
    public static string Code(string? typeEmploye, string? numeroCarteAutoentrepreneur) =>
        Normaliser(typeEmploye)
        ?? (string.IsNullOrWhiteSpace(numeroCarteAutoentrepreneur) ? Salarie : SousTraitant);

    /// <summary>« Salarié » ou « Sous-traitant ».</summary>
    public static string Libelle(UserHrProfile? profil) => LibelleDe(Code(profil));

    public static string Libelle(string? typeEmploye, string? numeroCarteAutoentrepreneur) =>
        LibelleDe(Code(typeEmploye, numeroCarteAutoentrepreneur));

    public static string LibelleDe(string code) => code == SousTraitant ? LibelleSousTraitant : LibelleSalarie;

    public static bool EstSousTraitant(UserHrProfile? profil) => Code(profil) == SousTraitant;

    /// <summary>
    /// Code reconnu ou null : « Salarié », « salarie », « SALARIE », « CDI »… → SALARIE ;
    /// « Sous-traitant », « sous traitant », « ST », « Auto-entrepreneur », « AE »… → SOUS_TRAITANT.
    /// </summary>
    public static string? Normaliser(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return null;
        var n = SansAccents(valeur).ToUpperInvariant().Replace('-', ' ').Replace('_', ' ').Trim();
        while (n.Contains("  ", StringComparison.Ordinal)) n = n.Replace("  ", " ", StringComparison.Ordinal);
        return n switch
        {
            "SALARIE" or "SALARIES" or "SALARIEE" or "EMPLOYE" or "SALARIAT" => Salarie,
            "SOUS TRAITANT" or "SOUSTRAITANT" or "SOUS TRAITANTS" or "SOUS TRAITANCE" or "ST"
                or "AUTO ENTREPRENEUR" or "AUTOENTREPRENEUR" or "AE" or "ENTREPRENEUR" or "PRESTATAIRE" => SousTraitant,
            _ => null,
        };
    }

    private static string SansAccents(string texte)
    {
        var d = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
