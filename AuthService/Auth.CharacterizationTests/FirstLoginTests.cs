using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.Application.Security;
using Auth.Application.Commands.RegisterFromPlanning;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MediatR;

namespace AuthService.CharacterizationTests;

public sealed class FirstLoginTests
{
    private const string Temporary = "Temporary-Tests-Only-2026!";
    private const string Personal = "Personal-Tests-Only-2026!";

    private sealed class App : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public App() => connection.Open();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FirstLogin:Enabled"] = "true", ["FirstLogin:CommonPassword"] = Temporary
            }));
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<DbContextOptions<AuthDbContext>>();
                s.RemoveAll<AuthDbContext>();
                s.AddDbContext<AuthDbContext>(o => o.UseSqlite(connection));
            });
        }
        public async Task<(int Id, string Jwt)> Seed(string role = "Employee", bool pending = true)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new User { Email = $"{role}@test.invalid", Username = role, SubjectId = Guid.NewGuid(),
                IsActive = true, Role = new Role { Name = role },
                PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(Temporary) };
            if (pending) scope.ServiceProvider.GetRequiredService<IFirstLoginService>().TryInitializeNewUser(user);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return (user.Id, scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user));
        }
        public async Task WithDb(Func<AuthDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<AuthDbContext>());
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
    }

    private static async Task<string> Challenge(HttpClient client, string role)
    {
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { email = $"{role}@test.invalid", password = Temporary });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("refreshToken", out _));
        Assert.True(response.Headers.CacheControl?.NoStore);
        return body.GetProperty("changeToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Complete(HttpClient c, string token, string password = Personal, string? confirm = null) =>
        c.PostAsJsonAsync("/api/Auth/complete-first-login", new { changeToken = token, newPassword = password, confirmPassword = confirm ?? password });

    [Theory]
    [InlineData("Employee")]
    [InlineData("Qualiticien")]
    public async Task Mandatory_change_persists_hash_and_consumes_challenge(string role)
    {
        using var app = new App();
        var (id, oldJwt) = await app.Seed(role);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", oldJwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/session/validate")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var token = await Challenge(client, role);
        Assert.Equal(HttpStatusCode.OK, (await Complete(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Complete(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Auth/login", new { email = $"{role}@test.invalid", password = Temporary })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { email = $"{role}@test.invalid", password = Personal });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/Auth/session/validate")).StatusCode);
        await app.WithDb(async db => {
            var u = await db.Users.SingleAsync(x => x.Id == id);
            Assert.False(u.MustChangePassword); Assert.NotNull(u.PasswordChangedAt);
            Assert.Null(u.FirstLoginTokenHash); Assert.Null(u.TemporaryPasswordExpiresAt);
            Assert.NotEqual(Personal, u.PasswordHash);
            Assert.Single(await db.AccessAuditEvents.Where(x => x.EventType == "FirstLoginCompleted").ToListAsync());
        });
    }

    [Theory]
    [InlineData("expired-challenge")]
    [InlineData("expired-temporary")]
    [InlineData("inactive")]
    [InlineData("locked")]
    public async Task Invalid_account_or_expiry_blocks_completion(string condition)
    {
        using var app = new App(); await app.Seed(); var c = app.CreateClient(); var token = await Challenge(c, "Employee");
        await app.WithDb(async db => {
            var u = await db.Users.SingleAsync();
            if (condition == "expired-challenge") u.FirstLoginTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            if (condition == "expired-temporary") u.TemporaryPasswordExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            if (condition == "inactive") u.IsActive = false;
            if (condition == "locked") u.LockoutEnd = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Complete(c, token)).StatusCode);
    }

    [Fact]
    public async Task Validation_does_not_consume_challenge_and_new_login_replaces_it()
    {
        using var app = new App(); await app.Seed(); var c = app.CreateClient(); var old = await Challenge(c, "Employee");
        var token = await Challenge(c, "Employee");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Complete(c, old)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Complete(c, token, Temporary)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Complete(c, token, Personal, "Different-Password-123!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Complete(c, token, "short")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Complete(c, token)).StatusCode);
    }

    [Fact]
    public async Task Explicit_enrollment_is_authorized_dry_by_default_and_revokes_existing_sessions()
    {
        using var app = new App(); var (id, jwt) = await app.Seed(pending:false); var (_, admin) = await app.Seed("Admin", false);
        var c = app.CreateClient();
        var url = "/api/Auth/first-login/enroll";
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync(url, new { userIds = new[] { id } })).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(url, new { userIds = new[] { id } })).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync(url, new { userIds = new[] { id } })).StatusCode);
        await app.WithDb(async db => Assert.False((await db.Users.FindAsync(id))!.MustChangePassword));
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync(url, new { userIds = new[] { id }, dryRun = false })).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/Auth/session/validate")).StatusCode);
        c.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await Complete(c, await Challenge(c, "Employee"))).StatusCode);
        c.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        var response = await c.PostAsJsonAsync(url, new { userIds = new[] { id }, dryRun = false });
        Assert.Contains("already_personalized", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Import_never_overwrites_existing_password()
    {
        using var app = new App(); var (id, _) = await app.Seed(pending:false);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var before = await db.Users.AsNoTracking().SingleAsync();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegisterFromPlanningCommand(new RegisterFromPlanningDto {
            Email = before.Email, EmployeeId = before.SubjectId, RoleName = "Pilote", DefaultPassword = Personal
        }));
        db.ChangeTracker.Clear();
        var after = await db.Users.FindAsync(id);
        Assert.Equal(before.PasswordHash, after!.PasswordHash);
        Assert.False(after.MustChangePassword);
    }
    [Fact]
    public async Task New_import_uses_temporary_password_and_pending_refresh_is_denied()
    {
        using var app = new App(); await app.Seed();
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegisterFromPlanningCommand(new RegisterFromPlanningDto {
                Email = "new@test.invalid", EmployeeId = Guid.NewGuid(), RoleName = "Pilote", DefaultPassword = Personal
            }));
        }
        var c = app.CreateClient();
        var login = await c.PostAsJsonAsync("/api/Auth/login", new { email = "new@test.invalid", password = Temporary });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        await app.WithDb(async db => {
            var u = await db.Users.SingleAsync(x => x.Email == "new@test.invalid");
            Assert.True(u.MustChangePassword);
            u.RefreshToken = "previous-refresh"; u.RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        });
        var refresh = await c.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken = "previous-refresh" });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

}
