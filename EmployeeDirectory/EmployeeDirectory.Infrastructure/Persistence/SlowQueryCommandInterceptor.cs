using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Persistence;

/// <summary>Logue les commandes SQL &gt; 200 ms (capacité ≥300 users).</summary>
public sealed class SlowQueryCommandInterceptor(ILogger<SlowQueryCommandInterceptor> logger) : DbCommandInterceptor
{
    private const int ThresholdMs = 200;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        var ms = eventData.Duration.TotalMilliseconds;
        if (ms < ThresholdMs) return;
        var sql = command.CommandText;
        if (sql.Length > 400) sql = sql[..400] + "…";
        logger.LogWarning("Slow SQL {DurationMs:F0}ms — {Command}", ms, sql);
    }
}
