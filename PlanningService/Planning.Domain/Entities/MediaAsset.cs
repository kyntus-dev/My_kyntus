namespace Planning.Domain.Entities;

public enum MediaOwnerType
{
    Orphan = 0,
    Newsletter = 1,
    Reclamation = 2,
    Proposition = 3,
    TicketComment = 4,
    AppGuide = 5
}

public enum MediaKind
{
    Image = 0,
    Video = 1,
    Document = 2
}

public class MediaAsset
{
    public int Id { get; set; }
    public MediaOwnerType OwnerType { get; set; } = MediaOwnerType.Orphan;
    public int? OwnerId { get; set; }
    public MediaKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string StoragePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int SortOrder { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TicketComment
{
    public int Id { get; set; }
    /// <summary>Reclamation or Proposition</summary>
    public MediaOwnerType OwnerType { get; set; }
    public int OwnerId { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorNom { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
