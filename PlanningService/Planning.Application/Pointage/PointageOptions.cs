namespace Planning.Application.Pointage;

/// <summary>
/// Section « Pointage » : lecture des pointages de ZKBio Time (variables KYNTUS_BIOTIME_*).
/// Désactivé par défaut : la synchronisation ne démarre qu'une fois l'adresse et le compte renseignés.
/// </summary>
public sealed class PointageOptions
{
    public const string SectionName = "Pointage";

    public bool Enabled { get; set; }

    /// <summary>Adresse de ZKBio Time, ex. https://10.10.10.7</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Compte ZKBio Time dédié, en consultation seule (ex. kyntus-api).</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Empreinte SHA-256 du certificat HTTPS de ZKBio Time (« AB:CD:… »). Renseignée, elle remplace
    /// la vérification par autorité (certificat auto-signé ou expiré) ; vide, la vérification standard s'applique.
    /// </summary>
    public string? CertificateSha256 { get; set; }

    /// <summary>Cadence de synchronisation.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Fenêtre relue à chaque synchronisation (rattrape les pointeuses momentanément hors ligne).</summary>
    public int FenetreHeures { get; set; } = 48;

    /// <summary>Relecture complète quotidienne (nuit) sur ce nombre de jours.</summary>
    public int RelectureJours { get; set; } = 7;

    /// <summary>Historique repris lors de la première synchronisation.</summary>
    public int HistoriqueInitialJours { get; set; } = 62;

    /// <summary>Arrivée dans ce délai après l'heure de planning : pas de retard. Au-delà, tout le retard compte.</summary>
    public int ToleranceMinutes { get; set; } = 5;

    /// <summary>Deux badgeages plus proches que ce délai sont fusionnés (double passage du doigt).</summary>
    public int DoubleBadgeSecondes { get; set; } = 120;

    /// <summary>Durée de pause retenue quand le planning du jour n'en précise pas (si <see cref="PauseSelonShift"/>).</summary>
    public int PauseParDefautMinutes { get; set; } = 60;

    /// <summary>
    /// Pauses autorisées sur la journée (toutes pauses confondues), au-delà desquelles le dépassement
    /// compte en retard pause : 80 min, comme l'Excel « Combien il me reste » (01:20). Un régime
    /// horaire peut fixer sa propre valeur.
    /// </summary>
    public int PauseAutoriseeMinutes { get; set; } = 80;

    /// <summary>
    /// true : les jours de planning publié reprennent la pause du shift (comportement v10, 60 min) ;
    /// false (défaut) : <see cref="PauseAutoriseeMinutes"/> s'applique à tous.
    /// </summary>
    public bool PauseSelonShift { get; set; }

    /// <summary>Période maximale du planning prévu / réalisé d'une équipe (jours).</summary>
    public int PeriodeEquipeDetailMaxJours { get; set; } = 31;

    /// <summary>Fuseau des pointeuses, pour savoir quelle journée est « aujourd'hui ».</summary>
    public string FuseauHoraire { get; set; } = "Africa/Casablanca";

    /// <summary>Période consultable au maximum en une fois (jours).</summary>
    public int PeriodeMaxJours { get; set; } = 93;
}
