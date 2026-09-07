using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Caching;
using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeDirectory.UnitTests;

public class DirectoryIamAccessTests : IDisposable
{
    private readonly DirectoryDbContext _db;
    private readonly DirectoryIamAdminService _admin;
    private readonly DirectoryIamReadService _read;

    public DirectoryIamAccessTests()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DirectoryDbContext(options);
        _admin = new DirectoryIamAdminService(_db);
        _read = new DirectoryIamReadService(_db, new StubResolver(), new NoCache());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Seed_rows_for_superviseur_include_generate_and_equipe()
    {
        var equipe = PlatformAccessCatalog.Screens.Single(s => s.Id == "planning.equipe");
        Assert.Contains("Superviseur", equipe.View);
        var rows = PlatformAccessCatalog.SeedRowsForRole("Superviseur").ToList();
        Assert.Contains(rows, x => x.Action == "planning.generate.Act" && x.Scope == "Cellule");
        Assert.Contains(rows, x => x.Action == "planning.equipe.View" && x.Scope == "Cellule");
        Assert.Contains(rows, x => x.Action == "planning.shift-config.Configure" && x.Scope == "Cellule");
        Assert.Contains(rows, x => x.Action == "planning.validation.View" && x.Scope == "Cellule");
    }

    [Fact]
    public async Task Seed_then_matrix_matches_catalog_defaults()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        var stored = await _db.IamPermissions.AsNoTracking()
            .Where(p => p.Role == "Superviseur")
            .Select(p => p.Action)
            .ToListAsync();
        Assert.Contains("planning.generate.Act", stored);
        Assert.Contains("planning.equipe.View", stored);

        var matrix = await _admin.GetMatrixAsync("Superviseur");
        var generate = matrix.Rows.Single(r => r.Capability == "planning.generate");
        var shift = matrix.Rows.Single(r => r.Capability == "planning.shift-config");
        var publish = matrix.Rows.Single(r => r.Capability == "planning.publish");

        Assert.Equal("Cellule", matrix.PrimaryScope);
        Assert.True(generate.Verbs["Act"]);
        Assert.True(shift.Verbs["Configure"]);
        Assert.False(publish.Verbs["Validate"]);
    }

    [Fact]
    public async Task Upsert_persists_and_is_read_back_by_matrix_and_effective()
    {
        await _admin.SeedDefaultsIfEmptyAsync();

        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "superviseur",
            "planning.shift-config.Configure",
            "cellule",
            true));

        var matrix = await _admin.GetMatrixAsync("Superviseur");
        var shift = matrix.Rows.Single(r => r.Capability == "planning.shift-config");
        Assert.True(shift.Verbs["Configure"]);

        var keys = await _read.GetEffectivePermissionKeysAsync("SUPERVISEUR");
        Assert.Contains("planning.shift-config.Configure:Cellule", keys, StringComparer.OrdinalIgnoreCase);

        Assert.True(await _read.RoleHasActionAsync(
            "Superviseur", "planning.shift-config.Configure", "Cellule"));
    }

    [Fact]
    public async Task Uncheck_revokes_effective_permission()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "Superviseur", "planning.generate.Act", "Cellule", false));

        var matrix = await _admin.GetMatrixAsync("Superviseur");
        Assert.False(matrix.Rows.Single(r => r.Capability == "planning.generate").Verbs["Act"]);

        var keys = await _read.GetEffectivePermissionKeysAsync("Superviseur");
        Assert.DoesNotContain("planning.generate.Act:Cellule", keys, StringComparer.OrdinalIgnoreCase);
        Assert.False(await _read.RoleHasActionAsync("Superviseur", "planning.generate.Act", "Cellule"));
    }

    [Fact]
    public async Task Uncheck_rh_conges_validation_revokes_effective_permission()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "RH", "conges.validation.Validate", "Global", false));

        var matrix = await _admin.GetMatrixAsync("RH");
        Assert.False(matrix.Rows.Single(r => r.Capability == "conges.validation").Verbs["Validate"]);

        var keys = await _read.GetEffectivePermissionKeysAsync("RH");
        Assert.DoesNotContain("conges.validation.Validate:Global", keys, StringComparer.OrdinalIgnoreCase);
        Assert.False(await _read.RoleHasActionAsync("RH", "conges.validation.Validate", "Global"));

        var evaluator = new PolicyEvaluator(_read, _read, NullLogger<PolicyEvaluator>.Instance);
        var decision = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "RH", "conges.validation.Validate", "platform"));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Uncheck_revokes_same_action_on_other_scopes()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "RH", "conges.validation.Validate", "Cellule", true));

        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "RH", "conges.validation.Validate", "Global", false));

        Assert.False(await _read.RoleHasActionAsync("RH", "conges.validation.Validate", "Global"));
        Assert.False(await _read.RoleHasActionAsync("RH", "conges.validation.Validate", "Cellule"));

        var evaluator = new PolicyEvaluator(_read, _read, NullLogger<PolicyEvaluator>.Instance);
        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "RH", "conges.validation.Validate", "platform"))).Allowed);
    }

    [Fact]
    public async Task Leave_workflow_follows_rh_validate_grant()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        var iam = new GetLeaveWorkflowQueryHandler(_read);
        Assert.True((await iam.Handle(new GetLeaveWorkflowQuery(), default)).RhValidationRequired);

        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "RH", "conges.validation.Validate", "Global", false));
        Assert.False((await iam.Handle(new GetLeaveWorkflowQuery(), default)).RhValidationRequired);
    }

    [Fact]
    public async Task Evaluate_allows_cellule_grant_without_resource()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        await _admin.UpsertAsync(new UpsertIamPermissionRequest(
            "Superviseur", "planning.shift-config.Configure", "Cellule", true));

        var evaluator = new PolicyEvaluator(_read, _read, NullLogger<PolicyEvaluator>.Instance);
        var allowed = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Superviseur", "planning.shift-config.Configure", "platform"));
        var denied = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Employee", "planning.shift-config.Configure", "platform"));

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    [Fact]
    public async Task Evaluate_denies_publish_for_superviseur_even_after_generate_grant()
    {
        await _admin.SeedDefaultsIfEmptyAsync();
        var evaluator = new PolicyEvaluator(_read, _read, NullLogger<PolicyEvaluator>.Instance);
        var publish = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Superviseur", "planning.publish.Validate", "platform"));
        Assert.False(publish.Allowed);
    }

    [Fact]
    public async Task Invalid_grant_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _admin.UpsertAsync(new UpsertIamPermissionRequest("Inconnu", "foo.Bar", "Galaxy", true)));
    }

    [Fact]
    public async Task Seed_fills_missing_catalog_rows_without_wiping_revocations()
    {
        _db.IamPermissions.Add(new IamPermission
        {
            Id = Guid.NewGuid(),
            Role = "Superviseur",
            Action = "planning.generate.Act",
            Scope = "Cellule",
            IsAllowed = false,
        });
        await _db.SaveChangesAsync();

        await _admin.SeedDefaultsIfEmptyAsync();

        var generate = await _db.IamPermissions.SingleAsync(p =>
            p.Role == "Superviseur" && p.Action == "planning.generate.Act" && p.Scope == "Cellule");
        Assert.False(generate.IsAllowed);

        Assert.True(await _db.IamPermissions.AnyAsync(p =>
            p.Role == "Superviseur" && p.Action == "planning.equipe.View"));
    }

    [Fact]
    public void Catalog_normalizes_known_grant()
    {
        Assert.True(PlatformAccessCatalog.TryNormalizeGrant(
            "superviseur",
            "planning.shift-config.configure",
            "cellule",
            out var role,
            out var action,
            out var scope,
            out var error));
        Assert.Null(error);
        Assert.Equal("Superviseur", role);
        Assert.Equal("planning.shift-config.Configure", action);
        Assert.Equal("Cellule", scope);
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
            => factory(ct);
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubResolver : EmployeeDirectory.Application.Abstractions.IOrgResponsibilityResolver
    {
        public Task<IReadOnlyList<ResponsibleEmployeeDto>> GetResponsiblesAsync(string kind, string nodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResponsibleEmployeeDto>>([]);
        public Task<IReadOnlyList<ManagedNodeDto>> GetManagedNodesDetailedAsync(Guid employeeId, string? kind = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ManagedNodeDto>>([]);
        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CanActOnNodeAsync(Guid actorId, string kind, string nodeId, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
