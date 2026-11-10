using EmployeeDirectory.Application.Dtos;

namespace EmployeeDirectory.Application.Abstractions;

/// <summary>
/// Affectation des responsables par périmètre métier (réorganisation 2026) :
/// <list type="bullet">
/// <item>Superviseur  → une ou plusieurs activités (départements de production) : toutes les cellules de l'activité.</item>
/// <item>Chef de projet → une ou plusieurs activités : tous les pôles de l'activité.</item>
/// <item>Référent technique → une ou plusieurs cellules : tous les services de la cellule.</item>
/// </list>
/// Le périmètre est développé en affectations <c>OrgAssignment</c> aux niveaux existants
/// (Pôle / Cellule / Service) : aucun consommateur aval (Planning, Prime, Congé, Formation,
/// Parrainage, Documentation, Qualité) n'a besoin de connaître un nouveau niveau.
/// </summary>
public interface IOrgScopeAssignmentService
{
    /// <summary>
    /// Synchronise exactement le périmètre d'un responsable (remplace l'ensemble des nœuds du kind).
    /// <paramref name="scopeIds"/> = ids de départements de production (ChefDeProjet / Superviseur)
    /// ou ids de cellules (ReferentTechnique).
    /// </summary>
    Task<OrgScopeReconcileResult> ReconcileScopeAsync(
        string kind,
        Guid employeeId,
        IReadOnlyList<string> scopeIds,
        string primaryScopeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default);

    /// <summary>Rapport lecture seule : couverture des périmètres de chaque responsable.</summary>
    Task<IReadOnlyList<OrgScopeCoverageRowDto>> GetCoverageReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Après création d'une cellule : les superviseurs qui couvraient TOUTES les autres cellules
    /// de l'activité reçoivent aussi la nouvelle cellule. Retourne le nombre de responsables étendus.
    /// </summary>
    Task<int> PropagateAfterCelluleCreatedAsync(string celluleId, Guid? changedBy, CancellationToken ct = default);

    /// <summary>
    /// Après création d'un service : les référents techniques qui couvraient TOUS les autres
    /// services de la cellule reçoivent aussi le nouveau service.
    /// </summary>
    Task<int> PropagateAfterServiceCreatedAsync(string serviceId, Guid? changedBy, CancellationToken ct = default);

    /// <summary>
    /// Après création / rattachement d'un pôle à une activité : les chefs de projet qui couvraient
    /// tous les autres pôles reçoivent le pôle ; les superviseurs qui couvraient toutes les autres
    /// cellules reçoivent les cellules du pôle.
    /// </summary>
    Task<int> PropagateAfterPoleAttachedAsync(string poleId, Guid? changedBy, CancellationToken ct = default);

    /// <summary>
    /// Après une affectation sur un seul nœud (page Organisation RH, import Excel, API historique) :
    /// étend le responsable à son périmètre complet — superviseur / chef de projet à toute
    /// l'activité du nœud, référent technique à toute la cellule du service. Les autres nœuds
    /// déjà détenus sont conservés. Retourne le nombre de nœuds ajoutés.
    /// </summary>
    Task<int> ExpandAssignmentToScopeAsync(string kind, Guid employeeId, string nodeId, Guid? changedBy, CancellationToken ct = default);
}
