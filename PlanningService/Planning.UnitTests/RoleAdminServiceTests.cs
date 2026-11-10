using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v13 — gestion des rôles par l'Admin : création, protection des rôles système, suppression avec réaffectation.</summary>
public class RoleAdminServiceTests
{
    [Fact]
    public async Task Creation_passe_par_la_connexion_puis_la_fiche()
    {
        var (db, auth, svc) = Creer();
        var r = await svc.CreerAsync(new RoleSaisieRequest("  Chargé   qualité ", null));

        Assert.Equal("Chargé qualité", r.Name);
        Assert.False(r.Systeme);
        Assert.Contains(auth.Appels, a => a.StartsWith("PUT"));
        Assert.True(await db.Roles.AnyAsync(x => x.Name == "Chargé qualité"));
    }

    [Fact]
    public async Task Doublons_et_noms_systeme_refuses()
    {
        var (db, _, svc) = Creer();
        db.Roles.Add(new Role { Name = "Superviseur" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RoleRegleException>(() => svc.CreerAsync(new RoleSaisieRequest("superviseur", null)));
        await Assert.ThrowsAsync<RoleRegleException>(() => svc.CreerAsync(new RoleSaisieRequest("Coach", null)));
        await Assert.ThrowsAsync<RoleRegleException>(() => svc.CreerAsync(new RoleSaisieRequest("<b>", null)));
    }

    [Fact]
    public async Task Role_systeme_ni_renomme_ni_supprime()
    {
        var (db, _, svc) = Creer();
        var sup = new Role { Name = "Superviseur" };
        db.Roles.Add(sup);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RoleRegleException>(() => svc.ModifierAsync(sup.Id, new RoleSaisieRequest("Chef", null)));
        await Assert.ThrowsAsync<RoleRegleException>(() => svc.SupprimerAsync(sup.Id, null));
        var maj = await svc.ModifierAsync(sup.Id, new RoleSaisieRequest("Superviseur", "Superviseur de cellule"));
        Assert.Equal("Superviseur de cellule", maj.Description);
    }

    [Fact]
    public async Task Suppression_bascule_les_comptes_puis_supprime()
    {
        var (db, auth, svc) = Creer();
        var perso = new Role { Name = "Chargé qualité" };
        var pilote = new Role { Name = "Pilote" };
        db.Roles.AddRange(perso, pilote);
        db.Users.AddRange(
            new User { FirstName = "A", LastName = "A", Email = "a@k.com", Role = perso, IsActive = true, Guid = Guid.NewGuid() },
            new User { FirstName = "B", LastName = "B", Email = "b@k.com", Role = perso, IsActive = true, Guid = Guid.NewGuid() });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RoleRegleException>(() => svc.SupprimerAsync(perso.Id, null));
        var res = await svc.SupprimerAsync(perso.Id, pilote.Id);

        Assert.Equal(2, res.ComptesReaffectes);
        Assert.Equal("Pilote", res.Remplacement);
        Assert.False(await db.Roles.AnyAsync(x => x.Name == "Chargé qualité"));
        Assert.True(await db.Users.AllAsync(u => u.RoleId == pilote.Id));
        Assert.Contains(auth.Appels, a => a.StartsWith("DELETE") && a.Contains("remplacement=Pilote"));
    }

    private static (AppDbContext Db, FakeAuth Auth, RoleService Svc) Creer()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var auth = new FakeAuth();
        var users = DispatchProxy.Create<IUserService, FakeUsers>();
        ((FakeUsers)(object)users).Db = db;
        var svc = new RoleService(db, users, new Factory(auth), NullLogger<RoleService>.Instance);
        return (db, auth, svc);
    }

    /// <summary>Seul ChangerRoleAsync est utilisé : il change le rôle dans la base de test.</summary>
    public class FakeUsers : DispatchProxy
    {
        public AppDbContext Db { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IUserService.ChangerRoleAsync))
                throw new NotSupportedException(method.Name);
            var user = Db.Users.First(u => u.Id == (int)args![0]!);
            user.RoleId = (int)args[1]!;
            Db.SaveChanges();
            return Task.FromResult(true);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://auth/") };
    }

    private sealed class FakeAuth : HttpMessageHandler
    {
        public List<string> Appels { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Appels.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
