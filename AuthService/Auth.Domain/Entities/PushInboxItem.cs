namespace Auth.Domain.Entities;

public class PushInboxItem
{
    public Guid Id { get; set; }
    public int AuthUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Url { get; set; } = "/";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
