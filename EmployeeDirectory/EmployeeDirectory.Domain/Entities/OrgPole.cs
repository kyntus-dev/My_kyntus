namespace EmployeeDirectory.Domain.Entities;

public class OrgPole
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? BusinessDepartmentId { get; set; }
    public BusinessDepartment? BusinessDepartment { get; set; }
    /// <summary>Nœud archivé : conservé en base avec son historique, retiré des listes et des choix.</summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public ICollection<OrgCellule> Cellules { get; set; } = new List<OrgCellule>();
}
