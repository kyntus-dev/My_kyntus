namespace EmployeeDirectory.Domain.Entities;

public class OrgService
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CelluleId { get; set; } = string.Empty;
    /// <summary>Nœud archivé : conservé en base avec son historique, retiré des listes et des choix.</summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public OrgCellule Cellule { get; set; } = null!;
}
