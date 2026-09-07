namespace Auth.Application.DTOs;

public sealed class AccessAuditEntryDto
{
    public required string Id { get; set; }
    public required DateTime At { get; set; }
    public required string EventType { get; set; }
    public required string Email { get; set; }
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Message { get; set; }
}
