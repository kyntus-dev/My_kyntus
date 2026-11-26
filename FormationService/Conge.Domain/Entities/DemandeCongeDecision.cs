using Conge.Domain.Enums;

namespace Conge.Domain.Entities;

/// <summary>
/// Historique immuable d'une décision sur une demande de congé
/// (validation superviseur / RH, refus, annulation).
/// </summary>
public class DemandeCongeDecision
{
    public Guid Id { get; private set; }
    public Guid DemandeId { get; private set; }
    public Guid ActeurId { get; private set; }
    public string ActeurNom { get; private set; } = string.Empty;
    public string ActeurRole { get; private set; } = string.Empty;
    /// <summary>ValidationSuperviseur | ValidationRh | Refus | Annulation | Modification</summary>
    public string Action { get; private set; } = string.Empty;
    public StatutDemande StatutAvant { get; private set; }
    public StatutDemande StatutApres { get; private set; }
    public string? Commentaire { get; private set; }
    public DateTime At { get; private set; }

    public DemandeConge? Demande { get; private set; }

    private DemandeCongeDecision() { }

    public static DemandeCongeDecision Creer(
        Guid demandeId,
        Guid acteurId,
        string? acteurNom,
        string? acteurRole,
        string action,
        StatutDemande statutAvant,
        StatutDemande statutApres,
        string? commentaire = null)
    {
        return new DemandeCongeDecision
        {
            Id = Guid.NewGuid(),
            DemandeId = demandeId,
            ActeurId = acteurId,
            ActeurNom = acteurNom?.Trim() ?? string.Empty,
            ActeurRole = acteurRole?.Trim() ?? string.Empty,
            Action = action,
            StatutAvant = statutAvant,
            StatutApres = statutApres,
            Commentaire = commentaire,
            At = DateTime.UtcNow
        };
    }
}

public static class DemandeCongeDecisionActions
{
    public const string ValidationSuperviseur = "ValidationSuperviseur";
    public const string ValidationRh = "ValidationRh";
    public const string Refus = "Refus";
    public const string Annulation = "Annulation";
    /// <summary>v64.2 — dates modifiées par Admin / RH (statut inchangé).</summary>
    public const string Modification = "Modification";
}
