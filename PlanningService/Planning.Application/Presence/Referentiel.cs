namespace Planning.Application.Presence;

/// <summary>
/// v30 : « Référentiel unifié – discipline, prime variable, qualité et process » (fichier fourni par
/// la direction), repris tel quel : code, catégorie, manquement, montant du 1er mois, du 2e mois
/// consécutif, du 3e mois consécutif et +, gravité. Montants en DH, déduits de la paie.
/// G02 et G03 n'ont pas de montant : ils relèvent de la procédure disciplinaire.
/// Le même tableau est repris dans l'interface (manquements.util.ts) pour le calcul de l'export paie.
/// </summary>
public sealed record ManquementDto(
    string Code, string Categorie, string Libelle, decimal? Mois1, decimal? Mois2, decimal? Mois3, string Gravite);

public static class Referentiel
{
    public static readonly IReadOnlyList<ManquementDto> Manquements =
    [
        new ManquementDto("R01", "Retards et ponctualité", "Retard de 31 à 60 minutes", 50m, 100m, 150m, "1"),
        new ManquementDto("R02", "Retards et ponctualité", "Retard de plus de 1h00 à 2h00", 100m, 150m, 200m, "2"),
        new ManquementDto("R03", "Retards et ponctualité", "Retard de plus de 2h00", 200m, 250m, 300m, "3"),
        new ManquementDto("P01", "Pauses et présence au poste", "Sortie en pause en dehors des horaires prévus sans autorisation", 50m, 100m, 200m, "1"),
        new ManquementDto("P02", "Pauses et présence au poste", "Dépassement injustifié du temps de pause autorisé", 50m, 100m, 200m, "1"),
        new ManquementDto("P03", "Pauses et présence au poste", "Abandon du poste ou sortie du plateau sans prévenir le responsable", 100m, 200m, 300m, "2"),
        new ManquementDto("P04", "Pauses et présence au poste", "Retour tardif au poste après une pause", 50m, 100m, 200m, "1"),
        new ManquementDto("C01", "Comportement sur le plateau", "Utilisation du téléphone portable sur le plateau sans autorisation", 100m, 150m, 200m, "2"),
        new ManquementDto("C02", "Comportement sur le plateau", "Utilisation d’écouteurs ou d’un équipement personnel non autorisé pendant le travail", 50m, 100m, 200m, "1"),
        new ManquementDto("C03", "Comportement sur le plateau", "Non-respect de la langue de travail demandée pendant une communication professionnelle ou en arrière-plan", 50m, 100m, 200m, "3"),
        new ManquementDto("C04", "Comportement sur le plateau", "Bavardages, discussion parallèle, bruit excessif ou comportement perturbant le traitement et le fonctionnement du plateau", 50m, 100m, 200m, "2"),
        new ManquementDto("C05", "Comportement sur le plateau", "Déplacement ou circulation sur le plateau sans motif professionnel ni autorisation", 50m, 100m, 200m, "1"),
        new ManquementDto("C06", "Comportement sur le plateau", "Consommation de nourriture au poste de travail ou sur le plateau", 50m, 100m, 200m, "1"),
        new ManquementDto("C07", "Comportement sur le plateau", "Non-respect des règles de propreté et de rangement", 30m, 60m, 100m, "1"),
        new ManquementDto("M01", "Matériel et environnement de travail", "Mauvaise utilisation ou négligence dans l’utilisation du matériel informatique ou professionnel, pouvant entraîner une détérioration", 100m, 250m, 500m, "2"),
        new ManquementDto("M02", "Matériel et environnement de travail", "Dégradation volontaire du matériel ou des locaux", 500m, 500m, 500m, "3"),
        new ManquementDto("M03", "Matériel et environnement de travail", "Utilisation du matériel professionnel à des fins personnelles sans autorisation", 100m, 200m, 300m, "2"),
        new ManquementDto("G01", "Consignes et comportement professionnel", "Refus injustifié ou non-respect répété d’une consigne professionnelle légitime communiquée par le responsable", 100m, 200m, 500m, "2"),
        new ManquementDto("G02", "Consignes et comportement professionnel", "Comportement irrespectueux, dénigrement ou propos dévalorisants envers un collègue ou un interlocuteur professionnel", null, null, null, "1 à 3"),
        new ManquementDto("G03", "Consignes et comportement professionnel", "Comportement agressif, insultant ou menaçant", null, null, null, "3"),
        new ManquementDto("G04", "Consignes et comportement professionnel", "Prise de photos, vidéos ou enregistrements non autorisés dans les espaces de travail", 100m, 250m, 500m, "3"),
        new ManquementDto("G05", "Consignes et comportement professionnel", "Divulgation volontaire d’informations confidentielles ou violation grave des règles de sécurité des données ayant causé un préjudice à l’entreprise", 500m, 500m, 500m, "3"),
        new ManquementDto("Q01", "Qualité et process", "Geste promis au client ou au technicien non réalisé", 50m, 100m, 200m, "1"),
        new ManquementDto("Q02", "Qualité et process", "Geste ou engagement promis alors qu’il n’est pas réalisable", 50m, 100m, 200m, "1"),
        new ManquementDto("Q03", "Qualité et process", "Mise en attente non annoncée ou intempestive", 50m, 150m, 250m, "1 à 2"),
        new ManquementDto("Q04", "Qualité et process", "Appel non pris en charge", 250m, 400m, 500m, "3"),
        new ManquementDto("Q05", "Qualité et process", "Rappel / call back non réalisé après coupure ou lorsqu’il est requis par le process", 100m, 250m, 500m, "1 à 3"),
        new ManquementDto("Q06", "Qualité et process", "Traitement d’appel non complété ou partiel", 50m, 100m, 200m, "1"),
        new ManquementDto("Q07", "Qualité et process", "Discours, ton ou niveau de familiarité non adapté envers un client, technicien ou collègue OI", 150m, 300m, 500m, "2 à 3"),
        new ManquementDto("Q08", "Qualité et process", "Utilisation de termes techniques internes non adaptés au client", 50m, 100m, 200m, "1"),
        new ManquementDto("Q09", "Qualité et process", "Information CNIL / enregistrement de l’appel non mentionnée lorsqu’elle est requise", 250m, 400m, 500m, "3"),
        new ManquementDto("Q10", "Qualité et process", "Déplacement d’intervention sans appel réalisé", 100m, 200m, 300m, "2"),
        new ManquementDto("Q11", "Qualité et process", "Note non conforme aux process", 50m, 100m, 200m, "1"),
        new ManquementDto("Q12", "Qualité et process", "Diagnostic professionnel requis non réalisé", 50m, 100m, 200m, "1"),
        new ManquementDto("Q13", "Qualité et process", "Historisation non conforme", 50m, 100m, 200m, "1"),
    ];

    /// <summary>Codes des retards : calculés depuis le pointage, jamais saisis à la main.</summary>
    public static bool EstRetard(string? code) =>
        code is not null && code.StartsWith('R') && Manquements.Any(m => m.Code == code);

    public static ManquementDto? Trouver(string? code)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        return c.Length == 0 ? null : Manquements.FirstOrDefault(m => m.Code == c);
    }

    /// <summary>R01 : 31 à 60 min ; R02 : plus de 1 h à 2 h ; R03 : plus de 2 h ; sinon aucun code.</summary>
    public static string? CodeRetard(int minutes) =>
        minutes > 120 ? "R03" : minutes > 60 ? "R02" : minutes >= 31 ? "R01" : null;
}
