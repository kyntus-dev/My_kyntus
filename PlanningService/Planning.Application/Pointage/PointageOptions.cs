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

    /// <summary>
    /// Deux badgeages plus proches que ce délai, sur la même pointeuse, sont fusionnés (double passage
    /// du doigt sur le même passage).
    /// <para>
    /// v65 : 45 s au lieu de 120 s. À 120 s, le retour d'une pause courte (une minute) était pris pour
    /// un double passage et purement supprimé : la journée tombait à un nombre impair de pointages
    /// (« pointage manquant »), production et présence non calculées. Un double passage du doigt tient
    /// en quelques dizaines de secondes ; au-delà de <see cref="PauseMinimaleSecondes"/>, ce sont deux
    /// passages distincts et les deux sont conservés. En complément, la fusion ne rend jamais le
    /// nombre de pointages impair : un retour de pause n'est donc jamais perdu, quel que soit ce délai.
    /// </para>
    /// </summary>
    public int DoubleBadgeSecondes { get; set; } = 45;

    /// <summary>
    /// v65 — Durée au-delà de laquelle deux badgeages successifs sont toujours deux passages réels
    /// (sortie puis retour de pause), jamais un double passage du doigt : plafond appliqué à
    /// <see cref="DoubleBadgeSecondes"/>, y compris si une installation garde l'ancienne valeur de
    /// 120 s. Une pause plus courte reste une vraie pause dès lors que la supprimer déséquilibrerait
    /// la journée : les deux pointages sont alors gardés et la pause comptée.
    /// </summary>
    public int PauseMinimaleSecondes { get; set; } = 45;

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

    /// <summary>
    /// Pointeuses ignorées dans les calculs : badgeages d'accès à une zone (armoire technique…), qui ne
    /// sont ni des arrivées ni des pauses. Liste séparée par des virgules, comparée sans accents ni
    /// majuscules au nom (alias) ou au numéro de série de la pointeuse. Variable Pointage__TerminauxExclus.
    /// </summary>
    public string TerminauxExclus { get; set; } = "armoire";

    /// <summary>Période maximale du planning prévu / réalisé d'une équipe (jours).</summary>
    public int PeriodeEquipeDetailMaxJours { get; set; } = 31;

    /// <summary>Fuseau des pointeuses, pour savoir quelle journée est « aujourd'hui ».</summary>
    public string FuseauHoraire { get; set; } = "Africa/Casablanca";

    /// <summary>Période consultable au maximum en une fois (jours).</summary>
    public int PeriodeMaxJours { get; set; } = 93;
}
