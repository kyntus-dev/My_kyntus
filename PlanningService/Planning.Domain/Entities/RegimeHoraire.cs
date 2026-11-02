namespace Planning.Domain.Entities;

/// <summary>
/// Régime horaire défini par l'Admin : heures fixes de production par jour de la semaine
/// (ex. 8 h 40 du lundi au jeudi, 7 h 40 le vendredi, samedi non travaillé) et pause autorisée.
/// Sert de planning les jours où aucun planning n'est publié pour le collaborateur, et ouvre
/// (si coché) la page « Heure de sortie ».
/// </summary>
public class RegimeHoraire
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    /// <summary>Pauses autorisées sur la journée, non comptées dans la production (Excel : 01:20).</summary>
    public int PauseAutoriseeMinutes { get; set; } = 80;
    /// <summary>Donne accès à la page « Heure de sortie ».</summary>
    public bool AccesCalculSortie { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }

    public ICollection<RegimeHoraireJour> Jours { get; set; } = new List<RegimeHoraireJour>();
    public ICollection<RegimeHoraireAffectation> Affectations { get; set; } = new List<RegimeHoraireAffectation>();
}

/// <summary>Jour travaillé d'un régime. Un jour absent = non travaillé (repos).</summary>
public class RegimeHoraireJour
{
    public int Id { get; set; }
    public int RegimeHoraireId { get; set; }
    public RegimeHoraire RegimeHoraire { get; set; } = null!;
    /// <summary>Jour ISO : 1 = lundi … 7 = dimanche.</summary>
    public int JourSemaine { get; set; }
    /// <summary>Heure d'arrivée prévue (référence du retard).</summary>
    public TimeOnly Arrivee { get; set; }
    /// <summary>Production prévue, pauses exclues (ex. 520 = 8 h 40).</summary>
    public int ProductionMinutes { get; set; }
}

/// <summary>Cibles possibles d'une affectation de régime (de la plus précise à la plus large).</summary>
public static class RegimeCibles
{
    public const string Utilisateur = "Utilisateur";
    /// <summary>Service (SubService du Planning).</summary>
    public const string Service = "Service";
    /// <summary>Cellule (Service du Planning).</summary>
    public const string Cellule = "Cellule";
    /// <summary>Pôle (Floor du Planning).</summary>
    public const string Pole = "Pole";

    public static readonly IReadOnlyList<string> Toutes = [Utilisateur, Service, Cellule, Pole];

    public static bool EstValide(string? cible) => cible is not null && Toutes.Contains(cible);
}

/// <summary>
/// Affectation d'un régime à un pôle, une cellule, un service ou un collaborateur.
/// Une cible n'a qu'un régime ; la plus précise l'emporte (collaborateur &gt; service &gt; cellule &gt; pôle).
/// </summary>
public class RegimeHoraireAffectation
{
    public int Id { get; set; }
    public int RegimeHoraireId { get; set; }
    public RegimeHoraire RegimeHoraire { get; set; } = null!;
    public string Cible { get; set; } = RegimeCibles.Utilisateur;
    public int CibleId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}

/// <summary>Compte autorisé à utiliser « Heure de sortie » (accordé par l'Admin, compte par compte).</summary>
public class HeureSortieAcces
{
    public int UserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public int? GrantedBy { get; set; }
}
