using Auth.Application.Queries.AccessAudit;
using Auth.Domain.Interfaces;

namespace Auth.Application.UnitTests;

public class GetAccessAuditQueryHandlerTests
{
    [Fact]
    public async Task Maps_writer_rows_to_dtos()
    {
        var writer = new StubWriter();
        var handler = new GetAccessAuditQueryHandler(writer);

        var rows = await handler.Handle(new GetAccessAuditQuery(10), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal("11111111-1111-1111-1111-111111111111", rows[0].Id);
        Assert.Equal("LoginSuccess", rows[0].EventType);
        Assert.Equal("admin@kyntus.ma", rows[0].Email);
    }

    private sealed class StubWriter : IAccessAuditWriter
    {
        public Task WriteAsync(string eventType, string email, int? userId, string? message, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AccessAuditRecord>> ListAsync(int take, CancellationToken ct = default)
        {
            IReadOnlyList<AccessAuditRecord> rows =
            [
                new AccessAuditRecord(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
                    "LoginSuccess",
                    "admin@kyntus.ma",
                    1,
                    "127.0.0.1",
                    "test-agent",
                    null),
            ];
            return Task.FromResult(rows);
        }
    }
}
