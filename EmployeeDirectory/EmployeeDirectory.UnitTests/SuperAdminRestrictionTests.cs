using System.Security.Claims;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Iam;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// v60 — Super Admin et restrictions des administrateurs : par défaut un administrateur garde tous les
/// droits ; ce que le Super Admin retire est refusé, ce qu'il rend est de nouveau permis.
/// </summary>
public class SuperAdminRestrictionTests : IDisposable
{
    private static readonly Guid AdminA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid AdminB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid SuperAdmin = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    private readonly DirectoryDbContext _db;
    private readonly HttpContextAccessor _http = new();
    private readonly DirectoryIamAdminRestrictionService _service;

    public SuperAdminRestrictionTests()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DirectoryDbContext(options);
        _service = new DirectoryIamAdminRestrictionService(
            _db,
            new MemoryCache(new MemoryCacheOptions()),
            _http,
            NullLogger<DirectoryIamAdminRestrictionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ───────────── Règles pures ─────────────

    [Fact]
    public void Without_rows_an_admin_keeps_every_right()
    {
        Assert.Empty(AdminRestrictionRules.DeniedActions(AdminA, []));
        Assert.Equal(new[] { "*:Global" }, AdminAccessCatalog.EffectiveKeys([]));
        Assert.False(new AdminRestrictionSnapshot(false, []).Restricted);
    }

    [Fact]
    public void Account_row_overrides_all_admins_row()
    {
        var rows = new (Guid, string, bool)[]
        {
            (AdminRestrictionRules.AllAdmins, "admin.users.View", false),
            (AdminRestrictionRules.AllAdmins, "presences.export.Export", false),
            (AdminA, "admin.users.View", true),
        };
        var deniedA = AdminRestrictionRules.DeniedActions(AdminA, rows);
        var deniedB = AdminRestrictionRules.DeniedActions(AdminB, rows);

        Assert.DoesNotContain("admin.users.View", deniedA);
        Assert.Contains("presences.export.Export", deniedA);
        Assert.Contains("admin.users.View", deniedB);
    }

    [Fact]
    public void Removing_view_removes_every_verb_of_the_screen()
    {
        var snapshot = new AdminRestrictionSnapshot(false, ["admin.users.View"]);
        foreach (var verb in AdminRestrictionRules.Verbs)
            Assert.True(snapshot.IsDenied("admin.users", verb));
        Assert.False(snapshot.IsDenied("admin.organisation", "View"));
    }

    [Fact]
    public void Super_admin_snapshot_is_never_restricted()
    {
        var snapshot = new AdminRestrictionSnapshot(true, ["admin.users.View"]);
        Assert.False(snapshot.Restricted);
        Assert.False(snapshot.IsDenied("admin.users", "View"));
    }

    [Fact]
    public void Effective_keys_of_restricted_admin_hide_removed_features_only()
    {
        var keys = AdminAccessCatalog.EffectiveKeys(["admin.access.View", "presences.export.Export"]);

        Assert.DoesNotContain(keys, k => k.StartsWith("admin.access.", StringComparison.Ordinal));
        Assert.DoesNotContain("presences.export.Export:Global", keys);
        Assert.Contains("presences.export.View:Global", keys);
        Assert.Contains("admin.users.Delete:Global", keys);
        Assert.DoesNotContain("*:Global", keys);
    }

    [Fact]
    public void Restricted_admin_cannot_grant_what_was_removed()
    {
        IReadOnlyCollection<string> denied = ["admin.users.View", "planning.generate.Act"];
        Assert.False(AdminAccessCatalog.ActionAllowed(denied, "admin.users.Act"));
        Assert.False(AdminAccessCatalog.ActionAllowed(denied, "planning.generate.Act"));
        Assert.False(AdminAccessCatalog.ActionAllowed(denied, "*"));
        Assert.True(AdminAccessCatalog.ActionAllowed(denied, "planning.generate.View"));
        Assert.False(AdminAccessCatalog.ScreenVisible(denied, "admin.users"));
        Assert.True(AdminAccessCatalog.ScreenVisible(denied, "planning.generate"));
    }

    // ───────────── Correspondance API → fonctionnalité ─────────────

    [Fact]
    public void Every_screen_of_the_route_map_exists_in_the_catalog()
    {
        var ids = PlatformAccessCatalog.Screens.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var inconnus = AdminFeatureRouteMap.ReferencedScreens.Where(s => !ids.Contains(s)).ToList();
        Assert.True(inconnus.Count == 0, "Écrans inconnus : " + string.Join(", ", inconnus));
    }

    [Theory]
    [InlineData("GET", "/api/users", null, null)]
    [InlineData("GET", "/api/users/me", null, null)]
    [InlineData("GET", "/api/iam/effective-permissions", null, null)]
    [InlineData("GET", "/api/iam/admin-restrictions/me", null, null)]
    [InlineData("OPTIONS", "/api/iam/permissions", null, null)]
    [InlineData("PUT", "/api/iam/permissions/bulk", "admin.access", "Configure")]
    [InlineData("PUT", "/api/iam/users/0c9c1a39-0000-4000-8000-000000000000/access", "admin.access", "Configure")]
    [InlineData("POST", "/api/users", "admin.users", "Act")]
    [InlineData("DELETE", "/api/users/12", "admin.users", "Delete")]
    [InlineData("POST", "/api/users/12/reset-password", "admin.users", "Act")]
    [InlineData("POST", "/api/Users/12/reaffectation", "rh.affectations", "Act")]
    [InlineData("DELETE", "/api/planning/regimes-horaires/3", "pointage.regimes", "Delete")]
    [InlineData("PUT", "/api/planning/regimes-horaires/3", "pointage.regimes", "Configure")]
    [InlineData("POST", "/api/planning/42/publish", "planning.validation", "Validate")]
    [InlineData("GET", "/api/planning/export/rh", "planning.equipe", "Export")]
    [InlineData("POST", "/api/planning/presences/paie/validation", "presences.export", "Validate")]
    [InlineData("PUT", "/api/conges/3fa85f64-5717-4562-b3fc-2c963f66afa6/valider", "conges.validation", "Validate")]
    [InlineData("PUT", "/api/documentation/storage-config", "documentation.storage", "Configure")]
    [InlineData("GET", "/api/parrainage/admin/export", "parrainage.module", "Export")]
    [InlineData("PUT", "/api/prime/admin/rbac/roles", "prime.rbac", "Configure")]
    [InlineData("GET", "/api/auth/audit/access", "audit.access-history", "View")]
    [InlineData("POST", "/api/auth/login", null, null)]
    public void Route_map_resolves_expected_feature(string method, string path, string? screen, string? verb)
    {
        var feature = AdminFeatureRouteMap.Resolve(method, path);
        if (screen is null)
        {
            Assert.Null(feature);
            return;
        }
        Assert.NotNull(feature);
        Assert.Equal(screen, feature!.Screens[0]);
        Assert.Equal(verb, feature.Verb);
    }

    [Fact]
    public void Shared_endpoint_stays_open_when_one_of_its_screens_is_allowed()
    {
        // L'export paie lit les EVP : retirer « EVP » ne casse pas l'export s'il reste autorisé.
        var feature = AdminFeatureRouteMap.Resolve("GET", "/api/planning/presences/evp");
        var snapshot = new AdminRestrictionSnapshot(false, ["presences.evp.View"]);
        Assert.NotNull(feature);
        Assert.Contains(feature!.Screens, s => !snapshot.IsDenied(s, feature.Verb));
    }

    // ───────────── Évaluateur de droits ─────────────

    [Fact]
    public async Task Policy_evaluator_denies_removed_feature_to_admin_only()
    {
        var provider = new FixedProvider(new AdminRestrictionSnapshot(false, ["planning.generate.Act"]));
        var evaluator = new PolicyEvaluator(new NoCatalog(), new NoRebac(), NullLogger<PolicyEvaluator>.Instance, null, provider);

        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(AdminA, "Admin", "planning.generate.Act", "platform"))).Allowed);
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(AdminA, "Admin", "planning.generate.View", "platform"))).Allowed);
    }

    [Fact]
    public async Task Policy_evaluator_without_restrictions_keeps_admin_privileged()
    {
        var evaluator = new PolicyEvaluator(new NoCatalog(), new NoRebac(), NullLogger<PolicyEvaluator>.Instance, null, new FixedProvider(AdminRestrictionSnapshot.None));
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(AdminA, "Admin", "anything.Act", "platform"))).Allowed);
    }

    // ───────────── Service (base) : accorder puis retirer ─────────────

    [Fact]
    public async Task Removing_then_giving_back_a_feature_applies_immediately()
    {
        await _service.UpdateAsync(AdminA, new AdminRestrictionsUpdateRequest([new("admin.users.Delete", "retire")]), "super@kyntus.ma");
        Assert.True((await _service.GetAsync(AdminA)).IsDenied("admin.users", "Delete"));
        Assert.False((await _service.GetAsync(AdminB)).Restricted);

        await _service.UpdateAsync(AdminA, new AdminRestrictionsUpdateRequest([new("admin.users.Delete", "herite")]), "super@kyntus.ma");
        Assert.False((await _service.GetAsync(AdminA)).Restricted);
    }

    [Fact]
    public async Task All_admins_restriction_applies_to_every_admin_unless_given_back()
    {
        await _service.UpdateAsync(AdminRestrictionRules.AllAdmins, new AdminRestrictionsUpdateRequest([new("prime.rbac.View", "retire")]), "super@kyntus.ma");
        await _service.UpdateAsync(AdminB, new AdminRestrictionsUpdateRequest([new("prime.rbac.View", "accorde")]), "super@kyntus.ma");

        Assert.True((await _service.GetAsync(AdminA)).IsDenied("prime.rbac", "Configure"));
        Assert.False((await _service.GetAsync(AdminB)).IsDenied("prime.rbac", "Configure"));

        var detailB = await _service.GetDetailAsync(AdminB);
        var verbe = detailB.Screens.Single(s => s.Id == "prime.rbac").Verbs.Single(v => v.Verb == "View");
        Assert.False(verbe.TousLesAdmins);
        Assert.True(verbe.Compte);
        Assert.True(verbe.Effectif);
    }

    [Fact]
    public async Task Unknown_feature_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(AdminA, new AdminRestrictionsUpdateRequest([new("inconnu.View", "retire")]), "super@kyntus.ma"));
    }

    [Fact]
    public async Task Super_admin_is_never_restricted_even_with_rows()
    {
        await _service.UpdateAsync(SuperAdmin, new AdminRestrictionsUpdateRequest([new("admin.access.View", "retire")]), "x");
        _http.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim("sub", SuperAdmin.ToString()),
                    new Claim("role", "Admin"),
                    new Claim(KyntusAdminClaims.SuperAdminClaim, "true"),
                },
                "test")),
        };

        Assert.True(KyntusAdminClaims.IsSuperAdmin(_http.HttpContext.User));
        Assert.False((await _service.GetAsync(SuperAdmin)).Restricted);
    }

    [Fact]
    public void Plain_admin_token_is_not_super_admin()
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", "Admin"), new Claim("sub", AdminA.ToString()) }, "test"));
        var rh = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", "RH"), new Claim(KyntusAdminClaims.SuperAdminClaim, "true") }, "test"));
        Assert.True(KyntusAdminClaims.IsAdmin(admin));
        Assert.False(KyntusAdminClaims.IsSuperAdmin(admin));
        Assert.False(KyntusAdminClaims.IsSuperAdmin(rh));
    }

    private sealed class FixedProvider(AdminRestrictionSnapshot snapshot) : IAdminRestrictionProvider
    {
        public Task<AdminRestrictionSnapshot> GetAsync(Guid subjectId, CancellationToken ct = default) => Task.FromResult(snapshot);
    }

    private sealed class NoCatalog : IPermissionCatalog
    {
        public Task<bool> RoleHasActionAsync(string role, string action, string scope, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetEffectivePermissionKeysAsync(string role, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoRebac : IRebacClient
    {
        public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
