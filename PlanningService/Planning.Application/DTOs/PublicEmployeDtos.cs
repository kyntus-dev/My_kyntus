using Planning.Domain.Entities;

namespace Planning.Application.DTOs;

/// <summary>
/// v56 — Fiche employé telle qu'elle sort de l'API publique (lecture seule, consommable par un
/// autre outil). Tout ce que l'application connaît y figure, sauf les secrets : pas de mot de
/// passe, et le RIB n'est jamais renvoyé en entier.
/// </summary>
public sealed class PublicEmployeDto
{
    // Identifiants
    public int Id { get; set; }
    public Guid Guid { get; set; }
    public int? AuthUserId { get; set; }
    public string? ImmatriculationInterne { get; set; }
    public string? ImmatriculationCnss { get; set; }
    public string? Cin { get; set; }

    // Identité
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string NomComplet { get; set; } = string.Empty;
    public string? Sexe { get; set; }
    public DateOnly? DateNaissance { get; set; }
    public string? VilleNaissance { get; set; }
    public string? Nationalite { get; set; }
    public string? SituationFamiliale { get; set; }
    public int? NombreEnfants { get; set; }
    public string? NiveauScolaire { get; set; }
    public string? IntitulesEtudes { get; set; }
    public string? NumeroCarteAutoentrepreneur { get; set; }

    // Contact
    public string? MailInterne { get; set; }
    public string? EmailPersonnel { get; set; }
    public string? Telephone { get; set; }
    public string? TelephoneUrgence { get; set; }
    public string? RelationUrgence { get; set; }
    public string? Adresse { get; set; }
    /// <summary>Quatre derniers chiffres seulement : le RIB complet ne sort jamais de l'application.</summary>
    public string? RibMasque { get; set; }

    // Organisation
    public string? Contrat { get; set; }
    public string? Pole { get; set; }
    public string? Cellule { get; set; }
    public string? Service { get; set; }
    public int? SousServiceId { get; set; }
    public string? SousService { get; set; }
    public string Role { get; set; } = string.Empty;
    public int NiveauContractuel { get; set; }
    public int? NiveauExpertiseMetier { get; set; }
    public bool Planifiable { get; set; }
    public List<string> CellulesEncadrees { get; set; } = new();
    public List<string> ServicesEncadres { get; set; } = new();

    // Responsables (identifiants de fiche)
    public Guid? ChefDeProjetId { get; set; }
    public Guid? SuperviseurId { get; set; }
    public Guid? ReferentTechniqueId { get; set; }
    public string? ChefDeProjet { get; set; }
    public string? Superviseur { get; set; }
    public string? ReferentTechnique { get; set; }

    // Dates
    public DateOnly? DateEntree { get; set; }
    public DateOnly? DateEmbauche { get; set; }
    public DateOnly? DateAnciennete { get; set; }
    public DateOnly? DateSortie { get; set; }
    public DateOnly? DateEvolutionPoste { get; set; }
    public string? AncienPoste { get; set; }
    public string? AncienService { get; set; }
    /// <summary>v63 : champs du fichier RH de référence.</summary>
    public string? Poste { get; set; }
    public string? TypeContrat { get; set; }
    public string? NumeroBox { get; set; }
    /// <summary>v64 : « Salarié » ou « Sous-traitant » (type de la fiche employé).</summary>
    public string TypeEmploye { get; set; } = TypesEmploye.LibelleSalarie;
    public string? CarteImmatriculation { get; set; }
    public string? NumeroPasseport { get; set; }
    public DateTime CreeLe { get; set; }

    // Formation
    public bool EnFormation { get; set; }
    public string? TypeFormation { get; set; }
    public DateOnly? DateDebutFormation { get; set; }
    public DateOnly? DateFinFormationPrevue { get; set; }

    // Planification
    public int? ModeSamedi { get; set; }
    public bool CasParticulier { get; set; }
    public string? CasParticulierDescription { get; set; }
    public bool FormationPlateau { get; set; }

    // HTEL
    public int? IdTechnicienHtel { get; set; }
    public string? CodeHtel { get; set; }

    // Statut
    public bool Actif { get; set; }
    public string Statut { get; set; } = string.Empty;
    public bool CompteDeConnexion { get; set; }

    /// <summary>Champs personnalisés ajoutés par l'Admin (Configuration des modules).</summary>
    public Dictionary<string, string?> ChampsPersonnalises { get; set; } = new();
}

/// <summary>Page de résultats de l'API publique.</summary>
public sealed class PublicEmployesResultDto
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int Taille { get; set; }
    public int Pages { get; set; }
    public DateTime GenereLe { get; set; } = DateTime.UtcNow;
    public List<PublicEmployeDto> Employes { get; set; } = new();
}

/// <summary>Filtres de l'API publique (tous facultatifs).</summary>
public sealed class PublicEmployesFiltre
{
    public bool? Actif { get; set; } = true;
    public string? Contrat { get; set; }
    public string? Pole { get; set; }
    public string? Cellule { get; set; }
    public string? Service { get; set; }
    public string? Role { get; set; }
    public bool? EnFormation { get; set; }
    public bool? Planifiable { get; set; }
    public string? Recherche { get; set; }
    public int Page { get; set; } = 1;
    public int Taille { get; set; } = 200;
}
