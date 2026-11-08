namespace Planning.Domain.Entities;

/// <summary>
/// Saisie d'un responsable sur une journée d'un agent (tableau des présences) : motif d'absence,
/// autorisation de retard, commentaire. Une ligne par agent et par jour ; sans ligne, la journée
/// est calculée à partir du pointage.
/// </summary>
public class PresenceJour
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Jour { get; set; }
    /// <summary>Code imposé (P, ABS N, AM…) ; null = code calculé depuis le pointage.</summary>
    public string? Code { get; set; }
    /// <summary>Retard d'arrivée autorisé par le responsable : il ne compte pas dans les retards.</summary>
    public bool RetardAutorise { get; set; }
    /// <summary>Dépassement de pause autorisé : il ne compte pas dans les retards.</summary>
    public bool RetardPauseAutorise { get; set; }
    /// <summary>Motif de l'autorisation (« prévenu la veille », « rendez-vous médical »…).</summary>
    public string? MotifAutorisation { get; set; }
    public string? Commentaire { get; set; }
    /// <summary>Absence partielle dans la journée (colonne « Abs. partielle » de l'export paie).</summary>
    public bool AbsencePartielle { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    /// <summary>Identifiant lisible de l'auteur (repris dans « Commentaire Présence » de l'export paie).</summary>
    public string? UpdatedByNom { get; set; }
}

/// <summary>Avertissement, erreur ou sanction saisi par un responsable pour un agent.</summary>
public class Avertissement
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Type { get; set; } = string.Empty;
    /// <summary>v30 : code du référentiel des manquements (C01, Q04…) ; null pour les anciennes saisies.</summary>
    public string? Code { get; set; }
    public string Commentaire { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}

/// <summary>
/// v31 : type ajouté à la main par un responsable (« Suite donnée » des manquements ou type d'élément
/// variable de paie), en plus des types prévus. Les saisies gardent le libellé : supprimer un type ne
/// modifie aucune saisie existante.
/// </summary>
public class PresenceTypePersonnalise
{
    public long Id { get; set; }
    /// <summary>« avertissement » ou « evp ».</summary>
    public string Categorie { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}

/// <summary>
/// Élément variable de paie : montant positif (ajouté au salaire) ou négatif (déduit), avec son
/// type et un commentaire. Montant 0 : élément « à valoriser » par le service paie.
/// </summary>
public class ElementVariablePaie
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    public string Commentaire { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}

/// <summary>Pièce du dossier administratif d'un agent (RH et chef de projet uniquement).</summary>
public class DossierDocument
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Categorie { get; set; } = string.Empty;
    public string NomFichier { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Taille { get; set; }
    public byte[] Contenu { get; set; } = [];
    public string? Commentaire { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}
