using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Services;

public sealed class AccessAuditService(AuthDbContext db, IHttpContextAccessor http) : IAccessAuditWriter
{
    public async Task WriteAsync(string eventType, string email, int? userId, string? message, CancellationToken ct = default)
    {
        try
        {
            var ctx = http.HttpContext;
            db.AccessAuditEvents.Add(new AccessAuditEvent
            {
                Id = Guid.NewGuid(),
                At = DateTime.UtcNow,
                EventType = eventType,
                Email = (email ?? string.Empty).Trim().ToLowerInvariant(),
                UserId = userId,
                IpAddress = ctx?.Connection.RemoteIpAddress?.ToString()
                            ?? ctx?.Request.Headers["X-Forwarded-For"].FirstOrDefault(),
                UserAgent = ctx?.Request.Headers.UserAgent.ToString(),
                Message = message,
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // L'audit d'accès ne doit jamais faire échouer login/logout.
        }
    }

    public async Task<IReadOnlyList<AccessAuditRecord>> ListAsync(int take, CancellationToken ct = default)
    {
        var limit = Math.Clamp(take, 1, 500);
        return await db.AccessAuditEvents.AsNoTracking()
            .OrderByDescending(e => e.At)
            .Take(limit)
            .Select(e => new AccessAuditRecord(e.Id, e.At, e.EventType, e.Email, e.UserId, e.IpAddress, e.UserAgent, e.Message))
            .ToListAsync(ct);
    }
}
