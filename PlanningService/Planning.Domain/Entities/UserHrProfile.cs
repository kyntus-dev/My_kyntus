namespace Planning.Domain.Entities;

/// <summary>Projection locale du profil RH canonique (Directory).</summary>
public class UserHrProfile
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? ChefDeProjetId { get; set; }
    public Guid? SuperviseurId { get; set; }
    public Guid? ReferentTechniqueId { get; set; }

    public DateOnly? DateNaissance { get; set; }
    public string? VilleNaissance { get; set; }
    public string? Nationalite { get; set; }
    public string? NumeroCarteAutoentrepreneur { get; set; }
    public string? Sexe { get; set; }
    public string? SituationFamiliale { get; set; }
    public int? NombreEnfants { get; set; }
    public string? Cin { get; set; }
    public string? Adresse { get; set; }
    /// <summary>Email personnel (hors compte / login).</summary>
    public string? EmailPersonnel { get; set; }
    public string? Telephone1 { get; set; }
    public string? TelephoneUrgence { get; set; }
    public string? RelationUrgence { get; set; }
    public string? Rib { get; set; }
    public string? ImmatriculationInterne { get; set; }
    public string? ImmatriculationCnss { get; set; }
    public DateOnly? DateEntree { get; set; }
    public DateOnly? DateEmbauche { get; set; }
    public DateOnly? DateAnciennete { get; set; }
    public DateOnly? DateSortie { get; set; }
    public DateOnly? DateEvolutionPoste { get; set; }
    public string? AncienPoste { get; set; }
    public string? AncienService { get; set; }
    public string? NiveauScolaire { get; set; }
    public string? IntitulesEtudes { get; set; }
    public bool EnFormation { get; set; }
    public DateOnly? DateDebutFormation { get; set; }
    public DateOnly? DateFinFormationPrevue { get; set; }
    /// <summary>
    /// v30 : jour où la case « En formation » a été décochée (ou l'agent affecté à un service de
    /// production). L'agent est en production à partir de ce jour ; null tant qu'il est en formation.
    /// </summary>
    public DateOnly? FormationTermineeLe { get; set; }
    public int? NiveauExpertiseMetier { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// v30 : change la case « En formation » en gardant la date d'intégration en production :
    /// décochée → intégration le jour donné ; recochée → la formation reprend (date effacée).
    /// </summary>
    public void ChangerEnFormation(bool enFormation, DateOnly aujourdhui)
    {
        if (EnFormation && !enFormation)
            FormationTermineeLe = aujourdhui;
        else if (!EnFormation && enFormation)
            FormationTermineeLe = null;
        EnFormation = enFormation;
    }

    /// <summary>
    /// v30 : dernier jour de formation. Case cochée : null (la formation continue, même au-delà de
    /// la date prévue, puisqu'elle peut être prolongée). Case décochée : la veille de l'intégration,
    /// ou la date de fin saisie si elle est antérieure.
    /// </summary>
    public DateOnly? FinFormationEffective()
    {
        if (EnFormation) return null;
        var veille = FormationTermineeLe?.AddDays(-1);
        if (DateFinFormationPrevue is DateOnly fin && (veille is null || fin < veille.Value)) return fin;
        return veille;
    }

    /// <summary>v30 : le jour donné est-il un jour de formation (hors paie) ?</summary>
    public bool EstEnFormationLe(DateOnly jour)
    {
        if ((DateDebutFormation ?? DateEntree) is DateOnly debut && jour < debut) return false;
        if (EnFormation) return true;
        // Formation terminée : jusqu'à son dernier jour, s'il est connu.
        return FinFormationEffective() is DateOnly fin && jour <= fin;
    }
}
