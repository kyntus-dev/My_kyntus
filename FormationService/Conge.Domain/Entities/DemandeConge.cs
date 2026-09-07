using Conge.Domain.Enums;
using Conge.Domain.Events;
using Conge.Domain.Exceptions;

namespace Conge.Domain.Entities;

/// <summary>
/// Aggregate Root — Demande de congé.
/// Contient toute la logique métier relative à une demande.
/// </summary>
public class DemandeConge
{
    private readonly List<object> _domainEvents = new();
    private readonly List<DemandeCongeDecision> _decisions = new();

    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
    public IReadOnlyCollection<DemandeCongeDecision> Decisions => _decisions.AsReadOnly();

    public Guid Id { get; private set; }
    public Guid EmployeId { get; private set; }
    /// <summary>Validateur historique (premier responsable / ManagerId snapshot) — compat.</summary>
    public Guid ManagerId { get; private set; }
    public TypeConge TypeConge { get; private set; }
    public TypeCongeExceptionnel? TypeExceptionnel { get; private set; }
    public DateTime DateDebut { get; private set; }
    public DateTime DateFin { get; private set; }
    public double NombreJours { get; private set; }
    public StatutDemande Statut { get; private set; }
    public string? Motif { get; private set; }
    public string? CommentaireManager { get; private set; }
    public string? CommentaireRh { get; private set; }
    public DateTime DateDemande { get; private set; }
    public DateTime? DateValidationSuperviseur { get; private set; }
    public DateTime? DateDecision { get; private set; }

    /// <summary>Nœud org sur lequel la validation superviseur s'applique (cellule préférée).</summary>
    public string? ValidationNodeId { get; private set; }
    public string? ValidationNodeLevel { get; private set; }
    public Guid? SuperviseurDecideurId { get; private set; }
    public Guid? RhDecideurId { get; private set; }

    // EF Core constructor
    private DemandeConge() { }

    /// <summary>
    /// Crée une demande de congé annuel.
    /// </summary>
    public static DemandeConge CreerCongeAnnuel(
        Guid employeId,
        Guid managerId,
        DateTime dateDebut,
        DateTime dateFin,
        SoldeConge solde,
        EmployeSnapshot employe,
        string? motif = null,
        StatutDemande statutInitial = StatutDemande.EnAttente,
        string? validationNodeId = null,
        string? validationNodeLevel = null)
    {
        if (!employe.EstEligibleCongeAnnuel(dateDebut))
        {
            var eligibleAt = employe.DateEligibiliteCongeAnnuel();
            throw new EligibiliteException(
                employeId,
                $"Le congé annuel ne peut commencer qu'à partir du {eligibleAt:dd/MM/yyyy} (6 mois d'ancienneté). La demande peut être déposée dès maintenant.");
        }

        if (dateDebut >= dateFin)
            throw new ArgumentException("La date de début doit être antérieure à la date de fin.");

        if (dateDebut.Date < DateTime.UtcNow.Date)
            throw new ArgumentException("La date de début ne peut pas être dans le passé.");

        var nombreJours = PolitiqueConge.CompterJoursOuvrables(dateDebut, dateFin);

        if (!solde.ASufficament(nombreJours))
            throw new SoldeInsuffisantException(employeId, nombreJours, solde.SoldeRestant);

        EnsureStatutInitial(statutInitial);

        var demande = new DemandeConge
        {
            Id = Guid.NewGuid(),
            EmployeId = employeId,
            ManagerId = managerId,
            TypeConge = TypeConge.Annuel,
            DateDebut = dateDebut,
            DateFin = dateFin,
            NombreJours = nombreJours,
            Statut = statutInitial,
            Motif = motif,
            DateDemande = DateTime.UtcNow,
            ValidationNodeId = NormalizeNode(validationNodeId),
            ValidationNodeLevel = NormalizeNode(validationNodeLevel)
        };

        demande._domainEvents.Add(new CongeDemandeEvent(demande.Id, employeId, managerId, TypeConge.Annuel, nombreJours));
        return demande;
    }

    /// <summary>
    /// Crée une demande de congé exceptionnel.
    /// </summary>
    public static DemandeConge CreerCongeExceptionnel(
        Guid employeId,
        Guid managerId,
        TypeCongeExceptionnel typeExceptionnel,
        DateTime dateDebut,
        string? motif = null,
        StatutDemande statutInitial = StatutDemande.EnAttente,
        string? validationNodeId = null,
        string? validationNodeLevel = null)
    {
        var duree = PolitiqueConge.GetDureeExceptionnelle(typeExceptionnel);
        var dateFin = dateDebut.AddDays(duree - 1);

        EnsureStatutInitial(statutInitial);

        var demande = new DemandeConge
        {
            Id = Guid.NewGuid(),
            EmployeId = employeId,
            ManagerId = managerId,
            TypeConge = TypeConge.Exceptionnel,
            TypeExceptionnel = typeExceptionnel,
            DateDebut = dateDebut,
            DateFin = dateFin,
            NombreJours = duree,
            Statut = statutInitial,
            Motif = motif,
            DateDemande = DateTime.UtcNow,
            ValidationNodeId = NormalizeNode(validationNodeId),
            ValidationNodeLevel = NormalizeNode(validationNodeLevel)
        };

        demande._domainEvents.Add(new CongeDemandeEvent(demande.Id, employeId, managerId, TypeConge.Exceptionnel, duree));
        return demande;
    }

    public void AssignerNoeudValidation(string? validationNodeId, string? validationNodeLevel)
    {
        ValidationNodeId = NormalizeNode(validationNodeId);
        ValidationNodeLevel = NormalizeNode(validationNodeLevel);
    }

    private static void EnsureStatutInitial(StatutDemande statut)
    {
        if (statut is not (StatutDemande.EnAttente or StatutDemande.EnAttenteRh))
            throw new ArgumentException("Statut initial invalide pour une nouvelle demande.");
    }

    /// <summary>
    /// Validation superviseur : EnAttente → EnAttenteRh, ou directement Validee
    /// si l’étape RH est inactive dans la matrice d’accès.
    /// </summary>
    public void ValiderParSuperviseur(
        Guid superviseurId,
        string? commentaire = null,
        string? acteurNom = null,
        string? acteurRole = null,
        bool etapeRh = true)
    {
        EnsureTransition(StatutDemande.EnAttente, "valider (superviseur)");

        var avant = Statut;
        CommentaireManager = commentaire;
        DateValidationSuperviseur = DateTime.UtcNow;
        SuperviseurDecideurId = superviseurId;
        Statut = etapeRh ? StatutDemande.EnAttenteRh : StatutDemande.Validee;
        if (!etapeRh)
            DateDecision = DateTime.UtcNow;

        AppendDecision(
            superviseurId,
            acteurNom,
            acteurRole ?? "Superviseur",
            DemandeCongeDecisionActions.ValidationSuperviseur,
            avant,
            Statut,
            commentaire);

        if (!etapeRh)
            _domainEvents.Add(new CongeValideEvent(Id, EmployeId, superviseurId, NombreJours, TypeConge));
    }

    /// <summary>
    /// File RH déjà validée par le superviseur : clôture sans palier RH
    /// (matrice « Valider les congés » désactivée pour RH).
    /// </summary>
    public void FinaliserSansEtapeRh(
        Guid acteurId,
        string? acteurNom = null,
        string? acteurRole = null)
    {
        EnsureTransition(StatutDemande.EnAttenteRh, "finaliser sans étape RH");

        var avant = Statut;
        var validateur = SuperviseurDecideurId ?? acteurId;
        Statut = StatutDemande.Validee;
        DateDecision = DateTime.UtcNow;

        AppendDecision(
            acteurId,
            acteurNom,
            acteurRole ?? "Superviseur",
            DemandeCongeDecisionActions.ValidationSuperviseur,
            avant,
            Statut,
            "Étape RH inactive");

        _domainEvents.Add(new CongeValideEvent(Id, EmployeId, validateur, NombreJours, TypeConge));
    }

    /// <summary>Validation RH finale : EnAttenteRh → Validee.</summary>
    public void ValiderParRh(
        Guid rhId,
        string? commentaire = null,
        string? acteurNom = null,
        string? acteurRole = null)
    {
        EnsureTransition(StatutDemande.EnAttenteRh, "valider (RH)");

        var avant = Statut;
        Statut = StatutDemande.Validee;
        CommentaireRh = commentaire;
        DateDecision = DateTime.UtcNow;
        RhDecideurId = rhId;

        AppendDecision(
            rhId,
            acteurNom,
            acteurRole ?? "RH",
            DemandeCongeDecisionActions.ValidationRh,
            avant,
            Statut,
            commentaire);

        _domainEvents.Add(new CongeValideEvent(Id, EmployeId, rhId, NombreJours, TypeConge));
    }

    /// <summary>
    /// Validation legacy (une étape). Conservée pour compatibilité tests — préfère ValiderParRh.
    /// </summary>
    [Obsolete("Utiliser ValiderParSuperviseur puis ValiderParRh.")]
    public void Valider(Guid managerId, string? commentaire = null)
    {
        if (Statut == StatutDemande.EnAttente)
            ValiderParSuperviseur(managerId, commentaire);
        if (Statut == StatutDemande.EnAttenteRh)
            ValiderParRh(managerId, commentaire);
    }

    /// <summary>Refus par le superviseur ou RH.</summary>
    public void Refuser(
        Guid acteurId,
        string commentaire,
        string? acteurNom = null,
        string? acteurRole = null)
    {
        if (Statut is not (StatutDemande.EnAttente or StatutDemande.EnAttenteRh))
            throw AlreadyDecidedOrInvalid("refuser");

        if (string.IsNullOrWhiteSpace(commentaire))
            throw new ArgumentException("Un motif de refus est obligatoire.");

        var avant = Statut;
        Statut = StatutDemande.Refusee;
        if (avant == StatutDemande.EnAttente)
        {
            CommentaireManager = commentaire;
            SuperviseurDecideurId = acteurId;
        }
        else
        {
            CommentaireRh = commentaire;
            RhDecideurId = acteurId;
        }

        DateDecision = DateTime.UtcNow;

        AppendDecision(
            acteurId,
            acteurNom,
            acteurRole ?? (avant == StatutDemande.EnAttente ? "Superviseur" : "RH"),
            DemandeCongeDecisionActions.Refus,
            avant,
            Statut,
            commentaire);

        _domainEvents.Add(new CongeRefuseEvent(Id, EmployeId, acteurId, commentaire));
    }

    /// <summary>Annulation employé tant que pas définitivement traitée.</summary>
    public void Annuler(
        Guid? acteurId = null,
        string? acteurNom = null,
        string? acteurRole = null)
    {
        if (Statut is not (StatutDemande.EnAttente or StatutDemande.EnAttenteRh))
            throw AlreadyDecidedOrInvalid("annuler");

        var avant = Statut;
        Statut = StatutDemande.Annulee;
        DateDecision = DateTime.UtcNow;

        var id = acteurId is { } a && a != Guid.Empty ? a : EmployeId;
        AppendDecision(
            id,
            acteurNom,
            acteurRole ?? "Employee",
            DemandeCongeDecisionActions.Annulation,
            avant,
            Statut,
            null);

        _domainEvents.Add(new CongeAnnuleEvent(Id, EmployeId, NombreJours, TypeConge));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureTransition(StatutDemande expected, string actionLabel)
    {
        if (Statut == expected)
            return;
        throw AlreadyDecidedOrInvalid(actionLabel);
    }

    private InvalidOperationException AlreadyDecidedOrInvalid(string actionLabel)
    {
        if (Statut is StatutDemande.Validee or StatutDemande.Refusee or StatutDemande.Annulee
            || (Statut == StatutDemande.EnAttenteRh && SuperviseurDecideurId.HasValue))
        {
            var decideur = RhDecideurId ?? SuperviseurDecideurId;
            if (decideur.HasValue)
            {
                return new InvalidOperationException(
                    $"Impossible de {actionLabel} : demande déjà traitée (statut '{Statut}') par {decideur.Value}.");
            }
        }

        return new InvalidOperationException(
            $"Impossible de {actionLabel} une demande avec le statut '{Statut}'.");
    }

    private void AppendDecision(
        Guid acteurId,
        string? acteurNom,
        string? acteurRole,
        string action,
        StatutDemande avant,
        StatutDemande apres,
        string? commentaire)
    {
        _decisions.Add(DemandeCongeDecision.Creer(
            Id,
            acteurId,
            acteurNom,
            acteurRole,
            action,
            avant,
            apres,
            commentaire));
    }

    private static string? NormalizeNode(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
