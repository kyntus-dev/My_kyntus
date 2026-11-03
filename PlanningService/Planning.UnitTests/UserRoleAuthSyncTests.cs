using System.Net;
using System.Reflection;
using System.Text.Json;
using Kyntus.Iam;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Domain.Entities;
using Planning.Infrastructure.Messaging.Publishers;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// v12.1 — la fiche fait foi : un changement de rôle est appliqué au compte de connexion existant
/// sans jamais envoyer de mot de passe (donc sans le réinitialiser). Un mot de passe n'est généré
/// que si aucun compte de connexion n'existe pour cet e-mail.
/// </summary>
public class UserRoleAuthSyncTests
{
    [Fact]
    public async Task Compte_existant_non_lie_role_aligne_sans_mot_de_passe()
    {
        var auth = new FakeAuth(_ => (HttpStatusCode.OK, """{"id":42,"email":"a@k.com","subjectId":"00000000-0000-0000-0000-000000000000"}"""));
        await using var db = CreateDb();
        var userId = await SeedUserAsync(db, "Ingénieur");

        await CreateService(db, auth).SyncUserRoleToAuthAsync(userId);

        var req = Assert.Single(auth.Requests);
        Assert.Equal(JsonValueKind.Null, req.GetProperty("defaultPassword").ValueKind);
        Assert.Equal("Ingénieur", req.GetProperty("roleName").GetString());
        Assert.Equal(42, (await db.Users.SingleAsync()).AuthUserId);
    }

    [Fact]
    public async Task Compte_absent_provisionne_avec_mot_de_passe()
    {
        var auth = new FakeAuth(body => body.GetProperty("defaultPassword").ValueKind == JsonValueKind.Null
            ? (HttpStatusCode.BadRequest, """{"message":"mot de passe requis"}""")
            : (HttpStatusCode.OK, """{"id":7,"email":"a@k.com","subjectId":"00000000-0000-0000-0000-000000000000"}"""));
        await using var db = CreateDb();
        var userId = await SeedUserAsync(db, "Support IT");

        await CreateService(db, auth).SyncUserRoleToAuthAsync(userId);

        Assert.Equal(2, auth.Requests.Count);
        Assert.Equal(JsonValueKind.String, auth.Requests[1].GetProperty("defaultPassword").ValueKind);
        Assert.Equal(7, (await db.Users.SingleAsync()).AuthUserId);
    }

    [Fact]
    public async Task Compte_lie_jamais_de_mot_de_passe()
    {
        var auth = new FakeAuth(_ => (HttpStatusCode.OK, """{"id":5,"email":"a@k.com","subjectId":"00000000-0000-0000-0000-000000000000"}"""));
        await using var db = CreateDb();
        var userId = await SeedUserAsync(db, "Coach", authUserId: 5);

        await CreateService(db, auth).SyncUserRoleToAuthAsync(userId);

        var req = Assert.Single(auth.Requests);
        Assert.Equal(JsonValueKind.Null, req.GetProperty("defaultPassword").ValueKind);
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<int> SeedUserAsync(AppDbContext db, string role, int? authUserId = null)
    {
        var r = new Role { Name = role };
        db.Roles.Add(r);
        var u = new User { FirstName = "A", LastName = "B", Email = "a@k.com", Role = r, IsActive = true, Guid = Guid.NewGuid(), AuthUserId = authUserId };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private static UserService CreateService(AppDbContext db, FakeAuth auth) => new(
        db,
        Stub<IEmployePublisher>(),
        new HttpClient(auth) { BaseAddress = new Uri("http://auth/") },
        Stub<IDirectoryEmployeeEnsureClient>(),
        Stub<IDirectoryEmployeeWriteClient>(),
        Stub<IDirectoryHierarchyClient>(),
        Stub<IDirectoryOrgWriteClient>(),
        new ConfigurationBuilder().Build(),
        NullLogger<UserService>.Instance,
        Stub<IEmployeeFieldService>(),
        Stub<IHttpContextAccessor>(),
        Stub<IRebacClient>(),
        Stub<IPlanningPerimeterResolver>());

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, NoopProxy>();

    /// <summary>Implémentation vide : Task terminée, valeurs par défaut.</summary>
    public class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var t = method!.ReturnType;
            if (t == typeof(void)) return null;
            if (t == typeof(Task)) return Task.CompletedTask;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var arg = t.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(arg)
                    .Invoke(null, [arg.IsValueType ? Activator.CreateInstance(arg) : null]);
            }
            if (t == typeof(ValueTask)) return ValueTask.CompletedTask;
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }

    private sealed class FakeAuth(Func<JsonElement, (HttpStatusCode, string)> repondre) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Requests.Add(body);
            var (status, json) = repondre(body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
