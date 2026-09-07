namespace Auth.Domain.Entities;

public class AccessAuditEvent
{
    public Guid Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Message { get; set; }
}
