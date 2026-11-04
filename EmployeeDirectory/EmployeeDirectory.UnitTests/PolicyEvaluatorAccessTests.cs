using Kyntus.Iam;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeDirectory.UnitTests;

public class PolicyEvaluatorAccessTests
{
    [Fact]
    public async Task Scoped_grant_without_resource_is_allowed()
    {
        var catalog = new FakeCatalog(("Superviseur", "planning.shift-config.Configure", "Cellule"));
        var evaluator = new PolicyEvaluator(catalog, new DenyRebac(), NullLogger<PolicyEvaluator>.Instance);

        var decision = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Superviseur", "planning.shift-config.Configure", "platform"));

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Missing_grant_is_denied()
    {
        var evaluator = new PolicyEvaluator(new FakeCatalog(), new DenyRebac(), NullLogger<PolicyEvaluator>.Instance);
        var decision = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Superviseur", "planning.publish.Validate", "platform"));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Admin_remains_privileged_rh_follows_catalog()
    {
        var evaluator = new PolicyEvaluator(new FakeCatalog(), new DenyRebac(), NullLogger<PolicyEvaluator>.Instance);
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Admin", "anything", "platform"))).Allowed);
        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "RH", "conges.validation.Validate", "platform"))).Allowed);
    }

    [Fact]
    public async Task Rh_grant_allows_conges_validation()
    {
        var catalog = new FakeCatalog(("RH", "conges.validation.Validate", "Global"));
        var evaluator = new PolicyEvaluator(catalog, new DenyRebac(), NullLogger<PolicyEvaluator>.Instance);
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "RH", "conges.validation.Validate", "platform"))).Allowed);
    }

    [Fact]
    public async Task Audit_is_not_privileged_and_needs_catalog()
    {
        var evaluator = new PolicyEvaluator(new FakeCatalog(), new DenyRebac(), NullLogger<PolicyEvaluator>.Instance);
        var decision = await evaluator.EvaluateAsync(new PolicyRequest(
            Guid.NewGuid(), "Audit", "planning.shift-config.Configure", "platform"));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Exception_de_compte_retire_ou_accorde_sans_toucher_au_role()
    {
        var farah = Guid.NewGuid();
        var collegue = Guid.NewGuid();
        var catalog = new FakeCatalog(("Référent technique", "presences.View", "Cellule"));
        var overrides = new FakeOverrides((farah, "presences.View", false), (farah, "presences.export.View", true));
        var evaluator = new PolicyEvaluator(catalog, new DenyRebac(), NullLogger<PolicyEvaluator>.Instance, overrides);

        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(farah, "Référent technique", "presences.View", "platform"))).Allowed);
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(farah, "Référent technique", "presences.export.View", "platform"))).Allowed);
        // Les autres comptes du même rôle gardent les droits du rôle.
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(collegue, "Référent technique", "presences.View", "platform"))).Allowed);
        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(collegue, "Référent technique", "presences.export.View", "platform"))).Allowed);
        // Un ajout n'ouvre pas les ressources d'autres personnes.
        Assert.False((await evaluator.EvaluateAsync(new PolicyRequest(farah, "Référent technique", "presences.export.View", "employee", Guid.NewGuid().ToString()))).Allowed);
        // L'Admin reste privilégié.
        Assert.True((await evaluator.EvaluateAsync(new PolicyRequest(farah, "Admin", "presences.View", "platform"))).Allowed);
    }

    [Fact]
    public void Cles_effectives_corrigees_par_les_exceptions()
    {
        var cles = EmployeeDirectory.Infrastructure.Services.DirectoryIamReadService.AppliquerExceptions(
            ["presences.View:Cellule", "presences.Act:Cellule", "planning.self.View:Self"],
            [("presences.Act", false), ("presences.export.View", true), ("planning.self.View", true)]);
        Assert.Equal(new[] { "presences.View:Cellule", "planning.self.View:Self", "presences.export.View:Self" }, cles);
    }

    private sealed class FakeOverrides(params (Guid Subject, string Action, bool Accorde)[] rows) : ISubjectPermissionOverrides
    {
        public Task<bool?> GetOverrideAsync(Guid subjectId, string action, CancellationToken ct = default)
        {
            var r = rows.Where(x => x.Subject == subjectId && x.Action == action).Select(x => (bool?)x.Accorde).FirstOrDefault();
            return Task.FromResult(r);
        }
    }

    private sealed class FakeCatalog(params (string Role, string Action, string Scope)[] rows) : IPermissionCatalog
    {
        public Task<bool> RoleHasActionAsync(string role, string action, string scope, CancellationToken ct = default)
            => Task.FromResult(rows.Any(r =>
                string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Action, action, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Scope, scope, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<string>> GetEffectivePermissionKeysAsync(string role, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(
                rows.Where(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase))
                    .Select(r => $"{r.Action}:{r.Scope}")
                    .ToList());
    }

    private sealed class DenyRebac : IRebacClient
    {
        public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
