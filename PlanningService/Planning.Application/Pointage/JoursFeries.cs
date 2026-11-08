namespace Planning.Application.Pointage;

/// <summary>
/// v31 : jours fériés du calendrier français (mêmes jours que les plannings des services :
/// FrenchHolidayHelper), avec leur nom. Sert au planning IT et au pointage des membres IT.
/// </summary>
public static class JoursFeries
{
    /// <summary>Dimanche de Pâques (algorithme de Meeus / Jones / Butcher).</summary>
    public static DateOnly Paques(int annee)
    {
        int a = annee % 19, b = annee / 100, c = annee % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int mois = (h + l - 7 * m + 114) / 31, jour = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(annee, mois, jour);
    }

    public static IReadOnlyDictionary<DateOnly, string> DeLAnnee(int annee)
    {
        var paques = Paques(annee);
        return new Dictionary<DateOnly, string>
        {
            [new DateOnly(annee, 1, 1)] = "Jour de l'An",
            [paques.AddDays(1)] = "Lundi de Pâques",
            [new DateOnly(annee, 5, 1)] = "Fête du Travail",
            [new DateOnly(annee, 5, 8)] = "Victoire 1945",
            [paques.AddDays(39)] = "Ascension",
            [paques.AddDays(50)] = "Lundi de Pentecôte",
            [new DateOnly(annee, 7, 14)] = "Fête nationale",
            [new DateOnly(annee, 8, 15)] = "Assomption",
            [new DateOnly(annee, 11, 1)] = "Toussaint",
            [new DateOnly(annee, 11, 11)] = "Armistice 1918",
            [new DateOnly(annee, 12, 25)] = "Noël",
        };
    }

    /// <summary>Nom du jour férié, ou null.</summary>
    public static string? Nom(DateOnly jour) => DeLAnnee(jour.Year).GetValueOrDefault(jour);
}
