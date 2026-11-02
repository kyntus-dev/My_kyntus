namespace Planning.Domain.Entities;

/// <summary>
/// Pointage brut lu dans ZKBio Time (lecture seule). Une ligne par badgeage, sans doublon :
/// la clé est l'identifiant de la transaction ZKBio Time.
/// Les heures sont l'heure locale de la pointeuse (colonnes « timestamp without time zone »).
/// </summary>
public class PointageBrut
{
    /// <summary>Identifiant de la transaction ZKBio Time.</summary>
    public long Id { get; set; }
    /// <summary>ID pointeuse du collaborateur (emp_code ZKBio Time).</summary>
    public string EmpCode { get; set; } = string.Empty;
    public DateTime PunchTime { get; set; }
    public DateTime? UploadTime { get; set; }
    public string? TerminalSn { get; set; }
    public string? TerminalAlias { get; set; }
    public int? VerifyType { get; set; }
    public string? NomBioTime { get; set; }
    /// <summary>Réception dans My-Kyntus (UTC).</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Statut d'une correspondance ID pointeuse → collaborateur.</summary>
public static class PointageCorrespondanceStatuts
{
    /// <summary>Rapprochement automatique par le nom, à valider par RH.</summary>
    public const string Auto = "Auto";
    /// <summary>Validé (ou saisi) par RH.</summary>
    public const string Valide = "Valide";
    /// <summary>Aucun collaborateur trouvé : à traiter par RH.</summary>
    public const string AValider = "AValider";
    /// <summary>Volontairement ignoré (badge de test, prestataire hors My-Kyntus…).</summary>
    public const string Ignore = "Ignore";
}

/// <summary>Correspondance entre l'ID pointeuse (emp_code) et un collaborateur My-Kyntus.</summary>
public class PointageCorrespondance
{
    public string EmpCode { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Statut { get; set; } = PointageCorrespondanceStatuts.AValider;
    public string? NomBioTime { get; set; }
    public string? DepartementBioTime { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
}

/// <summary>État de la synchronisation (ligne unique).</summary>
public class PointageSyncState
{
    public const string SingletonId = "biotime";

    public string Id { get; set; } = SingletonId;
    public DateTime? DerniereSynchro { get; set; }
    public DateTime? DerniereRelectureComplete { get; set; }
    public DateTime? DerniereSynchroCollaborateurs { get; set; }
    public int DerniersPointagesRecus { get; set; }
    public string? DerniereErreur { get; set; }
    public DateTime? DerniereErreurAt { get; set; }
}
