namespace Conge.Application.Abstractions;

/// <summary>Catalogue org Directory (noms + effectifs) pour Congés.</summary>
public interface IDirectoryOrgCatalog
{
    Task<DirectoryOrgCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task<string?> ResolveNodeNameAsync(string nodeId, CancellationToken ct = default);
}

public sealed record DirectoryOrgCatalogSnapshot(
    IReadOnlyDictionary<string, string> NodeNames,
    IReadOnlyDictionary<string, int> CelluleHeadcounts,
    IReadOnlyDictionary<string, int> ServiceHeadcounts,
    IReadOnlyDictionary<string, string>? ServiceParentCelluleIds = null,
    IReadOnlyDictionary<string, string>? CelluleParentPoleIds = null,
    IReadOnlyDictionary<string, string>? PoleDepartmentIds = null,
    IReadOnlySet<string>? ActiveOrgNodeIds = null)
{
    /// <summary>
    /// v36 : pôles, cellules et services actifs de l'arbre Directory (les archivés n'y sont pas).
    /// Vide = arbre inconnu (Directory injoignable) : rien n'est écarté.
    /// </summary>
    public IReadOnlySet<string> ActiveNodes { get; } =
        ActiveOrgNodeIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>v36 : nœud absent de l'arbre actif alors que l'arbre est connu (archivé ou supprimé).</summary>
    public bool EstHorsArbre(string? nodeId)
    {
        if (ActiveNodes.Count == 0) return false;
        var id = nodeId?.Trim() ?? "";
        return id.Length > 0 && !ActiveNodes.Contains(id);
    }

    public static DirectoryOrgCatalogSnapshot Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>serviceId → celluleId parent (arbre opérationnel Directory).</summary>
    public IReadOnlyDictionary<string, string> ServiceParents { get; } =
        ServiceParentCelluleIds ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>celluleId → pôle parent (v29).</summary>
    public IReadOnlyDictionary<string, string> CelluleParents { get; } =
        CelluleParentPoleIds ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>pôleId → contrat (département de production) (v29).</summary>
    public IReadOnlyDictionary<string, string> PoleDepartments { get; } =
        PoleDepartmentIds ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pôles du ou des contrats de ces pôles (chef de projet : tout son département). Un pôle sans
    /// contrat connu reste seul.
    /// </summary>
    public IReadOnlySet<string> PolesDuDepartement(IEnumerable<string> poleIds)
    {
        var poles = new HashSet<string>(poleIds.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
        var departements = poles.Select(p => PoleDepartments.TryGetValue(p, out var d) ? d : null)
            .Where(d => !string.IsNullOrWhiteSpace(d)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (pole, dept) in PoleDepartments)
            if (departements.Contains(dept)) poles.Add(pole);
        return poles;
    }

    public IEnumerable<string> CellulesDesPoles(IReadOnlySet<string> poles) =>
        CelluleParents.Where(kv => poles.Contains(kv.Value)).Select(kv => kv.Key);

    public IEnumerable<string> ServicesDesCellules(IReadOnlySet<string> cellules) =>
        ServiceParents.Where(kv => cellules.Contains(kv.Value)).Select(kv => kv.Key);

    public string? GetName(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return null;
        return NodeNames.TryGetValue(nodeId.Trim(), out var n) && !string.IsNullOrWhiteSpace(n) ? n : null;
    }

    public int GetHeadcount(string nodeId, string scopeKind)
    {
        var id = nodeId?.Trim() ?? "";
        if (id.Length == 0) return 0;
        if (string.Equals(scopeKind, "Cellule", StringComparison.OrdinalIgnoreCase))
            return CelluleHeadcounts.TryGetValue(id, out var c) ? c : 0;
        return ServiceHeadcounts.TryGetValue(id, out var s) ? s : 0;
    }

    /// <summary>
    /// True si le service appartient à l'une des cellules (via l'arbre Directory).
    /// Sans info d'arbre : laisse passer (catalogue offline).
    /// </summary>
    public bool IsServiceUnderCellules(string? serviceId, IReadOnlySet<string> celluleIds)
    {
        if (celluleIds.Count == 0) return true;
        var id = serviceId?.Trim() ?? "";
        if (id.Length == 0) return false;

        if (ServiceParents.TryGetValue(id, out var parent) && !string.IsNullOrWhiteSpace(parent))
            return celluleIds.Contains(parent);

        // Arbre connu pour ces cellules mais service absent → hors périmètre (ex. OrgServiceId obsolète).
        if (ServiceParents.Values.Any(celluleIds.Contains))
            return false;

        return true;
    }
}
