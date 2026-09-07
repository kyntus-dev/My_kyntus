namespace Auth.Domain.Interfaces;

public sealed record AccessAuditRecord(
    Guid Id,
    DateTime At,
    string EventType,
    string Email,
    int? UserId,
    string? IpAddress,
    string? UserAgent,
    string? Message);

public interface IAccessAuditWriter
{
    Task WriteAsync(string eventType, string email, int? userId, string? message, CancellationToken ct = default);
    Task<IReadOnlyList<AccessAuditRecord>> ListAsync(int take, CancellationToken ct = default);
}
