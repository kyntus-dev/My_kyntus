namespace Conge.Domain.Entities;

/// <summary>
/// Circuit de validation des congés (ligne unique).
///
/// Règle v9 : la décision appartient au superviseur de la cellule du demandeur.
/// Le service RH suit les demandes sans pouvoir les valider ni les refuser, sauf
/// pour les demandes qui lui sont adressées faute de superviseur compétent
/// (congé d'un superviseur, cellule sans superviseur actif).
///
/// <see cref="EtapeRhRequise"/> rétablit l'ancien circuit à deux étapes
/// (superviseur puis RH) sans redéploiement.
/// </summary>
public class CongeWorkflowConfig
{
    public static readonly Guid SingletonId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffff0001");

    public Guid Id { get; private set; }
    /// <summary>Vrai = ancien circuit : la validation superviseur passe ensuite en RH.</summary>
    public bool EtapeRhRequise { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    private CongeWorkflowConfig() { }

    /// <summary>Défaut v9 : superviseur seul décideur.</summary>
    public static CongeWorkflowConfig CreerParDefaut() => new()
    {
        Id = SingletonId,
        EtapeRhRequise = false,
        UpdatedAt = DateTime.UtcNow,
    };

    public void Definir(bool etapeRhRequise, Guid? updatedBy = null)
    {
        EtapeRhRequise = etapeRhRequise;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
