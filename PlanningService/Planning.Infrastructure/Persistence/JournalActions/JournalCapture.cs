using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Persistence.JournalActions;

/// <summary>
/// v64 — Lecture des changements suivis par EF juste avant l'enregistrement : chaque opération métier
/// tracée devient un brouillon de ligne du journal d'actions (valeurs avant / après lisibles).
/// Pur et sans accès base : les noms (employés, cellules, horaires, rôles) sont résolus après.
/// </summary>
public static class JournalCapture
{
    public const string ModuleEmployes = "Employés";
    public const string ModuleReaffectations = "Réaffectations";
    public const string ModulePresences = "Présences et absences";
    public const string ModuleConges = "Congés et arrêts maladie";
    public const string ModulePlanning = "Planning";
    public const string ModuleHoraires = "Horaires";
    public const string ModuleImportsExports = "Imports et exports";
    public const string ModuleDroits = "Droits d'accès";
    public const string ModuleParametres = "Paramètres";
    public const string ModuleValidations = "Validations";
    public const string ModulePaie = "Paie";

    /// <summary>Marqueur d'un identifiant à remplacer par un libellé après l'enregistrement.</summary>
    public const string RefCellule = "#cellule:";
    public const string RefHoraire = "#horaire:";
    public const string RefRole = "#role:";

    /// <summary>Jeton remplacé par le nom de l'employé concerné.</summary>
    public const string JetonCible = "{cible}";

    public sealed class Brouillon
    {
        public required string Module { get; init; }
        public required string Operation { get; init; }
        public required string Entite { get; init; }
        public required object Entity { get; init; }
        public string? EntiteId { get; set; }
        public required string Element { get; set; }
        public int? CibleUserId { get; set; }
        public Dictionary<string, string?>? Anciennes { get; init; }
        public Dictionary<string, string?>? Nouvelles { get; init; }
        public string? Details { get; set; }
    }

    private static readonly HashSet<string> Techniques = new(StringComparer.Ordinal)
    {
        "Id", "UserId", "CreatedAt", "CreatedBy", "CreatedByNom", "UpdatedAt", "UpdatedBy", "UpdatedByNom",
        "RowVersion", "ConcurrencyStamp", "LastPendingReminderAt", "GenerationErreur", "GenerationErreurLe",
        // Jamais au journal : secret ou compteur technique.
        "PasswordHash", "Guid", "AuthUserId", "EarlyShiftCount", "HtelTechnicienName",
    };

    /// <summary>Identifiants personnels : seuls les 4 derniers caractères sont conservés dans le journal.</summary>
    private static readonly HashSet<string> Sensibles = new(StringComparer.Ordinal)
    {
        "Cin", "Rib", "NumeroPasseport", "CarteImmatriculation", "NumeroCarteAutoentrepreneur", "ImmatriculationCnss",
    };

    private static readonly Dictionary<string, string> Libelles = new(StringComparer.Ordinal)
    {
        ["FirstName"] = "Prénom", ["LastName"] = "Nom", ["Email"] = "E-mail", ["HireDate"] = "Date d'embauche (compte)",
        ["IsActive"] = "Actif", ["Level"] = "Niveau contractuel", ["RoleId"] = "Rôle", ["SubServiceId"] = "Cellule",
        ["SaturdayWorkMode"] = "Régime du samedi", ["IsSpecialCase"] = "Cas particulier", ["SpecialCaseDescription"] = "Cas particulier (motif)",
        ["IsPlateauTraining"] = "Formation plateau", ["IsNewEmployee"] = "Nouvel employé", ["IdTechnicien"] = "Identifiant HTEL",
        ["HtelCode"] = "Code HTEL",
        ["DateNaissance"] = "Date de naissance", ["VilleNaissance"] = "Ville de naissance", ["Nationalite"] = "Nationalité",
        ["NumeroCarteAutoentrepreneur"] = "N° registre national (carte auto-entrepreneur)", ["Sexe"] = "Sexe",
        ["SituationFamiliale"] = "Situation familiale", ["NombreEnfants"] = "Nombre d'enfants", ["Cin"] = "CIN",
        ["Adresse"] = "Adresse", ["EmailPersonnel"] = "E-mail personnel", ["Telephone1"] = "Téléphone",
        ["TelephoneUrgence"] = "Téléphone d'urgence", ["RelationUrgence"] = "Relation (urgence)", ["Rib"] = "RIB",
        ["ImmatriculationInterne"] = "Matricule interne", ["ImmatriculationCnss"] = "N° CNSS", ["DateEntree"] = "Date d'entrée",
        ["DateEmbauche"] = "Date d'embauche", ["DateAnciennete"] = "Date d'ancienneté", ["DateSortie"] = "Date de sortie",
        ["DateEvolutionPoste"] = "Date d'évolution de poste", ["AncienPoste"] = "Ancien poste", ["AncienService"] = "Ancien service",
        ["Poste"] = "Nomination de poste", ["TypeContrat"] = "Type de contrat", ["NumeroBox"] = "N° BOX",
        ["TypeEmploye"] = "Type d'employé", ["CarteImmatriculation"] = "Carte d'immatriculation", ["NumeroPasseport"] = "N° passeport",
        ["NiveauScolaire"] = "Niveau scolaire", ["IntitulesEtudes"] = "Études", ["EnFormation"] = "En formation",
        ["TypeFormation"] = "Type de formation", ["DateDebutFormation"] = "Début de formation",
        ["DateFinFormationPrevue"] = "Fin de formation prévue", ["FormationTermineeLe"] = "Formation terminée le",
        ["ChefDeProjetId"] = "Chef de projet", ["SuperviseurId"] = "Superviseur", ["ReferentTechniqueId"] = "Référent technique",
        ["NiveauExpertiseMetier"] = "Expertise métier",
        ["StartDate"] = "Début", ["EndDate"] = "Fin", ["Reason"] = "Motif", ["AbsenceType"] = "Type d'absence", ["Status"] = "Statut",
        ["Jour"] = "Jour", ["Code"] = "Code", ["RetardAutorise"] = "Retard autorisé", ["RetardPauseAutorise"] = "Dépassement de pause autorisé",
        ["MotifAutorisation"] = "Motif de l'autorisation", ["Commentaire"] = "Commentaire", ["AbsencePartielle"] = "Absence partielle",
        ["SubServiceShiftConfigId"] = "Horaire", ["BreakTime"] = "Pause", ["IsOnLeave"] = "Absent", ["IsHoliday"] = "Férié",
        ["Label"] = "Libellé", ["StartTime"] = "Début", ["WorkHours"] = "Heures de travail", ["RequiredCount"] = "Effectif requis",
        ["Percentage"] = "Pourcentage", ["IsCriticalCell"] = "Créneau critique", ["ShiftKind"] = "Type d'horaire",
        ["BreakDurationMinutes"] = "Durée de pause (min)", ["BreakRangeStart"] = "Pauses à partir de", ["BreakRangeEnd"] = "Pauses jusqu'à",
        ["DisplayOrder"] = "Ordre", ["AppliesToSaturday"] = "Samedi", ["MinPresencePercent"] = "Présence minimale (%)",
        ["Date"] = "Date", ["Type"] = "Type", ["Montant"] = "Montant", ["PeriodeDu"] = "Période du", ["PeriodeAu"] = "Période au",
        ["ValideParNom"] = "Validé par", ["RouvertLe"] = "Rouvert le", ["RouvertParNom"] = "Rouvert par",
        ["AncienLibelle"] = "Ancienne affectation", ["NouveauLibelle"] = "Nouvelle affectation", ["Motif"] = "Motif",
        ["AncienRole"] = "Ancien rôle", ["NouveauRole"] = "Nouveau rôle", ["Origine"] = "Origine",
    };

    public static string Libelle(string propriete) => Libelles.GetValueOrDefault(propriete, propriete);

    private static readonly Dictionary<string, string> TypesAbsence = new(StringComparer.Ordinal)
    {
        ["AbsenceAutorisee"] = "Absence autorisée", ["AbsenceNonAutorisee"] = "Absence non autorisée",
        ["ArretMaladie"] = "Arrêt maladie", ["NeTravaillePlus"] = "Ne travaille plus", ["AccidentDeTravail"] = "Accident de travail",
        ["CongesPayes"] = "Congés payés", ["CongesSansSolde"] = "Congés sans solde", ["DeuilFamilial"] = "Deuil familial",
        ["EnfantMalade"] = "Enfant malade", ["Maternite"] = "Maternité", ["Paternite"] = "Paternité", ["Recup"] = "Récupération",
    };

    public static string LibelleAbsence(AbsenceType type) => TypesAbsence.GetValueOrDefault(type.ToString(), type.ToString());

    public static string? Format(string propriete, object? valeur)
    {
        if (valeur is null) return null;
        var texte = valeur switch
        {
            DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset o => o.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            bool b => b ? "Oui" : "Non",
            AbsenceType a => LibelleAbsence(a),
            decimal m => m.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => valeur.ToString(),
        };
        if (string.IsNullOrWhiteSpace(texte)) return null;
        texte = texte.Trim();
        if (Sensibles.Contains(propriete))
            return texte.Length <= 4 ? new string('•', texte.Length) : "••••" + texte[^4..];
        return propriete switch
        {
            "SubServiceId" => RefCellule + texte,
            "SubServiceShiftConfigId" => RefHoraire + texte,
            "RoleId" => RefRole + texte,
            "TypeEmploye" => TypesEmploye.LibelleDe(texte),
            _ => texte.Length > 500 ? texte[..500] + "…" : texte,
        };
    }

    /// <summary>Propriétés modifiées (avant / après), hors champs techniques.</summary>
    private static (Dictionary<string, string?> Avant, Dictionary<string, string?> Apres) Ecarts(EntityEntry e, Func<string, bool>? garder = null)
    {
        var avant = new Dictionary<string, string?>();
        var apres = new Dictionary<string, string?>();
        foreach (var p in e.Properties)
        {
            var nom = p.Metadata.Name;
            if (Techniques.Contains(nom) || p.Metadata.IsShadowProperty() || (garder is not null && !garder(nom))) continue;
            object? ancienne = e.State == EntityState.Added ? null : p.OriginalValue;
            object? nouvelle = e.State == EntityState.Deleted ? null : p.CurrentValue;
            if (e.State == EntityState.Modified && (!p.IsModified || Equals(ancienne, nouvelle))) continue;
            var a = Format(nom, ancienne);
            var n = Format(nom, nouvelle);
            if (a == n) continue;
            if (e.State != EntityState.Added) avant[Libelle(nom)] = a;
            if (e.State != EntityState.Deleted) apres[Libelle(nom)] = n;
        }
        return (avant, apres);
    }

    private static T? Original<T>(EntityEntry e, string propriete) =>
        e.Properties.FirstOrDefault(p => p.Metadata.Name == propriete) is { } p && p.OriginalValue is T v ? v : default;

    private static bool Change(EntityEntry e, string propriete) =>
        e.Properties.FirstOrDefault(p => p.Metadata.Name == propriete) is { IsModified: true } p
        && !Equals(p.OriginalValue, p.CurrentValue);

    private static string Op(EntityState s) => s switch
    {
        EntityState.Added => "Création",
        EntityState.Deleted => "Suppression",
        _ => "Modification",
    };

    /// <summary>Brouillons des opérations tracées parmi les changements en attente.</summary>
    public static List<Brouillon> Capturer(IEnumerable<EntityEntry> entrees, IReadOnlySet<Type> silencieux)
    {
        var resultat = new List<Brouillon>();
        foreach (var e in entrees)
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (e.Entity is JournalAction || silencieux.Contains(e.Entity.GetType())) continue;
            var b = e.Entity switch
            {
                User u => Utilisateur(e, u),
                UserHrProfile p => FicheRh(e, p),
                PresenceJour pj => Presence(e, pj),
                Conge c => Absence(e, c),
                ReaffectationAgent r => Reaffectation(e, r),
                WeeklyPlanning w => PlanningHebdo(e, w),
                ShiftAssignment a => Journee(e, a),
                SubServiceShiftConfig s => Horaire(e, s),
                ElementVariablePaie v => Evp(e, v),
                PaiePeriodeValidation pv => ValidationPaie(e, pv),
                _ => null,
            };
            if (b is not null) resultat.Add(b);
        }
        return resultat;
    }

    private static Brouillon? Utilisateur(EntityEntry e, User u)
    {
        var (avant, apres) = Ecarts(e);
        if (e.State == EntityState.Modified && apres.Count == 0 && avant.Count == 0) return null;
        var operation = Op(e.State);
        if (e.State == EntityState.Modified && Change(e, nameof(User.IsActive)))
            operation = u.IsActive ? "Réactivation" : "Désactivation";
        return new Brouillon
        {
            Module = ModuleEmployes, Operation = operation, Entite = nameof(User), Entity = u,
            Element = "Employé " + JetonCible, CibleUserId = u.Id == 0 ? null : u.Id,
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? FicheRh(EntityEntry e, UserHrProfile p)
    {
        var (avant, apres) = Ecarts(e);
        if (avant.Count == 0 && apres.Count == 0) return null;
        return new Brouillon
        {
            Module = ModuleEmployes, Operation = e.State == EntityState.Added ? "Création de la fiche RH" : "Modification de la fiche RH",
            Entite = nameof(UserHrProfile), Entity = p, EntiteId = p.UserId.ToString(CultureInfo.InvariantCulture),
            Element = "Fiche RH — " + JetonCible, CibleUserId = p.UserId,
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? Presence(EntityEntry e, PresenceJour pj)
    {
        var (avant, apres) = Ecarts(e);
        if (e.State == EntityState.Modified && avant.Count == 0 && apres.Count == 0) return null;
        var jour = e.State == EntityState.Deleted ? Original<DateOnly>(e, nameof(PresenceJour.Jour)) : pj.Jour;
        return new Brouillon
        {
            Module = ModulePresences,
            Operation = e.State switch
            {
                EntityState.Added => "Saisie",
                EntityState.Deleted => "Retour au calcul (saisie effacée)",
                _ => "Modification",
            },
            Entite = nameof(PresenceJour), Entity = pj,
            Element = $"Présence du {jour:dd/MM/yyyy} — {JetonCible}", CibleUserId = pj.UserId,
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? Absence(EntityEntry e, Conge c)
    {
        var (avant, apres) = Ecarts(e);
        if (e.State == EntityState.Modified && avant.Count == 0 && apres.Count == 0) return null;
        var debut = e.State == EntityState.Deleted ? Original<DateOnly>(e, nameof(Conge.StartDate)) : c.StartDate;
        var fin = e.State == EntityState.Deleted ? Original<DateOnly>(e, nameof(Conge.EndDate)) : c.EndDate;
        return new Brouillon
        {
            Module = ModuleConges, Operation = Op(e.State), Entite = nameof(Conge), Entity = c,
            Element = $"{LibelleAbsence(c.AbsenceType)} du {debut:dd/MM/yyyy} au {fin:dd/MM/yyyy} — {JetonCible}", CibleUserId = c.UserId,
            Anciennes = avant, Nouvelles = apres,
            Details = c.SourceDemandeId is null ? null : "Issue d'une demande de congé validée (workflow Congés).",
        };
    }

    private static Brouillon? Reaffectation(EntityEntry e, ReaffectationAgent r)
    {
        if (e.State != EntityState.Added) return null;
        return new Brouillon
        {
            Module = ModuleReaffectations, Operation = r.Origine == "Réaffectation" ? "Réaffectation" : r.Origine,
            Entite = nameof(ReaffectationAgent), Entity = r,
            Element = "Réaffectation — " + JetonCible, CibleUserId = r.UserId,
            Anciennes = new Dictionary<string, string?> { ["Affectation"] = r.AncienLibelle, ["Rôle"] = r.AncienRole },
            Nouvelles = new Dictionary<string, string?> { ["Affectation"] = r.NouveauLibelle, ["Rôle"] = r.NouveauRole },
            Details = string.IsNullOrWhiteSpace(r.Motif) ? null : "Motif : " + r.Motif,
        };
    }

    private static Brouillon? PlanningHebdo(EntityEntry e, WeeklyPlanning w)
    {
        if (e.State != EntityState.Modified || !Change(e, nameof(WeeklyPlanning.Status))) return null;
        var avant = Original<object>(e, nameof(WeeklyPlanning.Status))?.ToString();
        var apres = w.Status.ToString();
        var validation = apres == "Published";
        return new Brouillon
        {
            Module = ModuleValidations, Operation = validation ? "Validation" : "Dévalidation",
            Entite = nameof(WeeklyPlanning), Entity = w,
            Element = $"Planning de la semaine {w.WeekCode} — {RefCellule}{w.SubServiceId}",
            Anciennes = new Dictionary<string, string?> { ["Statut"] = LibelleStatut(avant) },
            Nouvelles = new Dictionary<string, string?> { ["Statut"] = LibelleStatut(apres) },
        };
    }

    private static string? LibelleStatut(string? s) => s switch
    {
        "Published" => "Validé (publié)",
        "Draft" => "À valider (brouillon)",
        _ => s,
    };

    private static Brouillon? Journee(EntityEntry e, ShiftAssignment a)
    {
        // Seules les corrections manuelles sont tracées une à une ; la génération l'est en bloc.
        if (e.State != EntityState.Modified || !a.IsManagerOverride) return null;
        if (!Change(e, nameof(ShiftAssignment.SubServiceShiftConfigId)) && !Change(e, nameof(ShiftAssignment.BreakTime))
            && !Change(e, nameof(ShiftAssignment.IsHoliday)) && !Change(e, nameof(ShiftAssignment.IsOnLeave)))
            return null;
        var (avant, apres) = Ecarts(e, nom => nom is nameof(ShiftAssignment.SubServiceShiftConfigId) or nameof(ShiftAssignment.BreakTime)
            or nameof(ShiftAssignment.IsHoliday) or nameof(ShiftAssignment.IsOnLeave));
        return new Brouillon
        {
            Module = ModulePlanning, Operation = "Correction manuelle", Entite = nameof(ShiftAssignment), Entity = a,
            Element = $"Journée du {a.AssignedDate:dd/MM/yyyy} — {JetonCible}", CibleUserId = a.UserId,
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? Horaire(EntityEntry e, SubServiceShiftConfig s)
    {
        var (avant, apres) = Ecarts(e, nom => nom is not (nameof(SubServiceShiftConfig.WeekCode) or nameof(SubServiceShiftConfig.WeekStartDate)
            or nameof(SubServiceShiftConfig.SubServiceId) or nameof(SubServiceShiftConfig.ShiftModeProfileId)
            or nameof(SubServiceShiftConfig.IsTemplate) or nameof(SubServiceShiftConfig.BreakSlotsJson)));
        if (e.State == EntityState.Modified && avant.Count == 0 && apres.Count == 0) return null;
        var portee = s.IsTemplate ? "modèle" : $"semaine {s.WeekCode}";
        return new Brouillon
        {
            Module = ModuleHoraires, Operation = Op(e.State), Entite = nameof(SubServiceShiftConfig), Entity = s,
            Element = $"Horaire « {s.Label} » ({s.StartTime:HH\\:mm}, {portee}) — {RefCellule}{s.SubServiceId}",
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? Evp(EntityEntry e, ElementVariablePaie v)
    {
        var (avant, apres) = Ecarts(e);
        if (e.State == EntityState.Modified && avant.Count == 0 && apres.Count == 0) return null;
        return new Brouillon
        {
            Module = ModulePaie, Operation = Op(e.State), Entite = nameof(ElementVariablePaie), Entity = v,
            Element = $"Élément variable de paie « {v.Type} » du {v.Date:dd/MM/yyyy} — {JetonCible}", CibleUserId = v.UserId,
            Anciennes = avant, Nouvelles = apres,
        };
    }

    private static Brouillon? ValidationPaie(EntityEntry e, PaiePeriodeValidation pv)
    {
        var element = $"Paie du {pv.PeriodeDu:dd/MM/yyyy} au {pv.PeriodeAu:dd/MM/yyyy}";
        if (e.State == EntityState.Added)
            return new Brouillon
            {
                Module = ModuleValidations, Operation = "Validation", Entite = nameof(PaiePeriodeValidation), Entity = pv,
                Element = element, Nouvelles = new Dictionary<string, string?> { ["Statut"] = "Validée (verrouillée)" },
                Details = pv.Commentaire,
            };
        if (e.State == EntityState.Modified && Change(e, nameof(PaiePeriodeValidation.RouvertLe)) && pv.RouvertLe is not null)
            return new Brouillon
            {
                Module = ModuleValidations, Operation = "Dévalidation", Entite = nameof(PaiePeriodeValidation), Entity = pv,
                Element = element,
                Anciennes = new Dictionary<string, string?> { ["Statut"] = "Validée (verrouillée)" },
                Nouvelles = new Dictionary<string, string?> { ["Statut"] = "Rouverte" },
            };
        return null;
    }
}
