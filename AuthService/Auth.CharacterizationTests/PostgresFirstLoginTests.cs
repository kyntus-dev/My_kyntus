using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Security;
using Auth.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AuthService.CharacterizationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRST_LOGIN_TEST_POSTGRES")))
            Skip = "Set FIRST_LOGIN_TEST_POSTGRES to an isolated PostgreSQL test instance.";
    }
}

public class PostgresFirstLoginTests
{
    [PostgresFact]
    public async Task Migration_preserves_existing_credentials_and_concurrent_completion_succeeds_once()
    {
        var connectionString = Environment.GetEnvironmentVariable("FIRST_LOGIN_TEST_POSTGRES")!;
        var schema = "first_login_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ToString();
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
        try
        {
            await using var db = new AuthDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260726180000_AddUserLockoutFields");
            // Populate the OLD schema, before the additive migration.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Roles" ("Id", "Name", "Description", "CreatedAt") VALUES (101, 'Employee', '', now());
                INSERT INTO "Users" ("Id", "Username", "Email", "PasswordHash", "IsActive", "CreatedAt", "RoleId", "SubjectId", "AccessFailedCount")
                VALUES (101, 'existing', 'existing@test.invalid', 'preserved-hash', true, now(), 101, '22222222-2222-4222-8222-222222222222', 0);
                """);
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync(); // rerun is idempotent
            var user = await db.Users.Include(x => x.Role).SingleAsync();
            Assert.Equal("preserved-hash", user.PasswordHash);
            Assert.False(user.MustChangePassword); Assert.Equal(0, user.CredentialVersion);
            // This table is historically created by AuthDatabaseInitializer, not migrations.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "AccessAuditEvents" (
                  "Id" uuid PRIMARY KEY, "At" timestamptz NOT NULL, "EventType" varchar(32) NOT NULL,
                  "Email" varchar(160) NOT NULL, "UserId" integer, "IpAddress" varchar(64),
                  "UserAgent" varchar(512), "Message" varchar(256));
                """);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["FirstLogin:Enabled"] = "true", ["FirstLogin:CommonPassword"] = "Temporary-Postgres-Test-2026!"
            }).Build();
            var hasher = new PasswordHasher();
            var service = new FirstLoginService(db, hasher, config);
            await service.EnrollAsync(new[] { user.Id }, false, false, "test", default);
            var challenge = await service.BeginAsync(user, default);
            async Task<bool> Complete()
            {
                await using var concurrentDb = new AuthDbContext(options);
                try {
                    await new FirstLoginService(concurrentDb, hasher, config).CompleteAsync(challenge.ChangeToken,
                        "Personal-Postgres-Test-2026!", "Personal-Postgres-Test-2026!", default);
                    return true;
                } catch (UnauthorizedAccessException) { return false; }
            }
            var outcomes = await Task.WhenAll(Complete(), Complete());
            Assert.Single(outcomes, x => x);
            db.ChangeTracker.Clear();
            user = await db.Users.SingleAsync();
            Assert.False(user.MustChangePassword);
            Assert.True(hasher.VerifyPassword(user.PasswordHash, "Personal-Postgres-Test-2026!"));
            Assert.False(hasher.VerifyPassword(user.PasswordHash, "Temporary-Postgres-Test-2026!"));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
