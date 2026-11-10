namespace EmployeeDirectory.Domain.Entities;

public class OrgCellule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PoleId { get; set; } = string.Empty;
    public OrgPole Pole { get; set; } = null!;
    /// <summary>Nœud archivé : conservé en base avec son historique, retiré des listes et des choix.</summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public ICollection<OrgService> Services { get; set; } = new List<OrgService>();
}
