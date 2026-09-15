using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeDirectory.UnitTests;

public sealed class HtelReconciliationTests : IDisposable
{
    private readonly DirectoryDbContext db = new(new DbContextOptionsBuilder<DirectoryDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeHtel client = new();
    private readonly RecordingOutbox outbox = new();
    private HtelFusionService Service => new(db, client, outbox, NullLogger<HtelFusionService>.Instance);
    private static HtelTechnicienDto Tech(int id = 4551, string name = "AARAB Fatima Zahra", int active = 1, string code = "SUPPORT")
        => new(id, name, active, code);
    private static Employee Employee(int? id = null, bool active = true, string first = "Fatima Zahra", string last = "AARAB") => new()
    {
        Id = Guid.NewGuid(), IdTechnicien = id, IsActive = active, FirstName = first, LastName = last,
        Email = $"{Guid.NewGuid():N}@example.test", Role = "Employe", HireDate = new DateTime(2020, 1, 1),
    };

    [Fact]
    public void A_single_name_candidate_is_visible_and_never_classified_as_new()
    {
        var employee = Employee();
        var report = HtelReconciliation.BuildReport([Tech()], [employee]);
        Assert.Empty(report.OrphansHtel);
        Assert.Equal(employee.Id.ToString(), Assert.Single(Assert.Single(report.Ambiguous).Candidates).EmployeeId);
        Assert.Empty(report.MissingFromHtel);
        Assert.Null(employee.IdTechnicien);
    }

    [Fact]
    public void Inactive_accounts_and_homonyms_are_included_in_candidates()
    {
        var a = Employee(active: false);
        var b = Employee();
        var report = HtelReconciliation.BuildReport([Tech()], [a, b]);
        Assert.Equal(2, Assert.Single(report.Ambiguous).Candidates.Count);
        Assert.Contains(report.Ambiguous[0].Candidates, c => !c.IsActive);
        Assert.Empty(report.OrphansHtel);
    }

    [Fact]
    public void Inactive_linked_account_reserves_the_external_identity()
    {
        var report = HtelReconciliation.BuildReport([Tech()], [Employee(4551, false)]);
        Assert.Empty(report.OrphansHtel);
        Assert.Empty(report.Ambiguous);
        Assert.False(Assert.Single(report.Linked).IsActive);
    }

    [Fact]
    public void Missing_linked_id_is_distinguished_from_an_unlinked_unmatched_account()
    {
        var linked = Employee(999);
        var unlinked = Employee(first: "ZZZZ", last: "YYYY");
        var report = HtelReconciliation.BuildReport([Tech()], [linked, unlinked]);
        Assert.Contains(report.MissingFromHtel, x => x.EmployeeId == linked.Id.ToString() && x.Reason == "LinkedIdMissing");
        Assert.Contains(report.MissingFromHtel, x => x.EmployeeId == unlinked.Id.ToString() && x.Reason == "NoReliableMatch");
        Assert.False(Assert.Single(report.Linked).ExistsInHtel);
    }

    [Fact]
    public void New_active_and_inactive_technicians_keep_their_upstream_status()
    {
        var report = HtelReconciliation.BuildReport([Tech(), Tech(2, "AUTRE Personne", 0)], []);
        Assert.Equal(2, report.OrphansHtel.Count);
        Assert.Single(report.OrphansHtel.Where(x => x.Actif == 1));
    }

    [Fact]
    public void Swapped_identity_is_flagged_for_review()
    {
        var e = Employee(4551, first: "AARAB", last: "Fatima Zahra");
        Assert.True(Assert.Single(HtelReconciliation.BuildReport([Tech()], [e]).Linked).IdentityNeedsReview);
        Assert.Equal("AARAB", e.FirstName);
    }

    [Fact]
    public async Task Sync_updates_mirrors_without_changing_names_status_or_other_HR_data_and_is_idempotent()
    {
        var e = Employee(4551, first: "AARAB", last: "Fatima Zahra");
        var inactive = Employee(2, false, "Autre", "Personne");
        var missing = Employee(3);
        db.Employees.AddRange(e, inactive, missing);
        await db.SaveChangesAsync();
        client.Rows = [Tech(active: 0), Tech(2, "PERSONNE Autre")];
        var first = await Service.SyncAsync();
        Assert.Equal(2, first.LinkedUpdated);
        Assert.Equal(0, first.NewlyLinked);
        Assert.True(e.IsActive);
        Assert.False(inactive.IsActive);
        Assert.True(missing.IsActive);
        Assert.Equal("AARAB", e.FirstName);
        Assert.Equal("Fatima Zahra", e.LastName);
        Assert.Equal("SUPPORT", e.HtelCode);
        Assert.Equal("AARAB Fatima Zahra", e.HtelTechnicienName);
        Assert.Equal(new DateTime(2020, 1, 1), e.HireDate);
        Assert.Equal(2, outbox.Messages.Count);
        Assert.Equal(0, (await Service.SyncAsync()).LinkedUpdated);
        Assert.Equal(2, outbox.Messages.Count);
    }

    [Fact]
    public async Task Ordinary_create_does_not_link_a_fuzzy_or_exact_name_automatically()
    {
        client.Rows = [Tech()];
        var employee = Employee();
        await Service.ApplyLinkOnEmployeeAsync(employee, null);
        Assert.Null(employee.IdTechnicien);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Explicit_create_checks_inactive_duplicates_and_inactive_HTEL_before_any_link()
    {
        db.Employees.Add(Employee(4551, false));
        await db.SaveChangesAsync();
        client.Rows = [Tech()];
        var employee = Employee();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ApplyLinkOnEmployeeAsync(employee, 4551));
        Assert.Null(employee.IdTechnicien);
        client.Rows = [Tech(2, active: 0)];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ApplyLinkOnEmployeeAsync(employee, 2));
        Assert.Null(employee.IdTechnicien);
    }

    [Fact]
    public async Task Upstream_failure_aborts_explicit_creation_but_preserves_existing_fiche_edits()
    {
        client.Fail = true;
        var employee = Employee();
        await Assert.ThrowsAsync<HttpRequestException>(() => Service.ApplyLinkOnEmployeeAsync(employee, 4551));
        var existing = Employee(4551);
        existing.HtelCode = "EXISTING";
        await Service.ApplyLinkOnEmployeeAsync(existing, null);
        Assert.Equal("EXISTING", existing.HtelCode);
    }

    [Fact]
    public async Task Deleted_upstream_technician_does_not_crash_regular_employee_update()
    {
        client.Rows = [Tech(2)];
        var existing = Employee(4551);
        existing.HtelTechnicienName = "OLD";
        await Service.ApplyLinkOnEmployeeAsync(existing, null);
        Assert.Equal("OLD", existing.HtelTechnicienName);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task Confirmed_identity_updates_names_and_publishes_the_existing_employee()
    {
        var employee = Employee(4551, first: "AARAB", last: "Fatima Zahra");
        db.Employees.Add(employee); await db.SaveChangesAsync(); client.Rows = [Tech()];
        var request = new HtelIdentityReviewRequest(employee.Id, 4551, employee.FirstName, employee.LastName,
            "AARAB Fatima Zahra", "Fatima Zahra", "AARAB");
        Assert.True(await Service.ReviewIdentityAsync(request));
        Assert.Equal("Fatima Zahra", employee.FirstName);
        Assert.Equal("AARAB", employee.LastName);
        var message = Assert.IsType<DirectoryEmployeeChangedMessage>(Assert.Single(outbox.Messages));
        Assert.Equal(employee.Id, message.EmployeeId);
        Assert.Equal(employee.Email, message.Email);
        Assert.True(employee.IsActive);
    }

    [Fact]
    public async Task Obsolete_comparison_and_unrelated_names_cannot_overwrite_identity()
    {
        var employee = Employee(4551); db.Employees.Add(employee); await db.SaveChangesAsync(); client.Rows = [Tech()];
        var request = new HtelIdentityReviewRequest(employee.Id, 4551, "OLD", "AARAB", "AARAB Fatima Zahra", "Fatima Zahra", "AARAB");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ReviewIdentityAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ReviewIdentityAsync(request with
            { ExpectedFirstName = employee.FirstName, ExpectedTechnicien = "OLD" }));
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ReviewIdentityAsync(request with
            { ExpectedFirstName = employee.FirstName, FirstName = "Autre" }));
        Assert.Empty(outbox.Messages);
    }

    [Fact]
    public async Task Empty_duplicate_or_invalid_snapshots_never_generate_absence_reports_or_writes()
    {
        db.Employees.Add(Employee(4551)); await db.SaveChangesAsync();
        foreach (var rows in new HtelTechnicienDto[][] { [], [Tech(), Tech()], [Tech(0)], [Tech(active: 2)], [Tech(name: "")] })
        {
            client.Rows = rows;
            await Assert.ThrowsAsync<HttpRequestException>(() => Service.GetLiaisonsAsync());
            await Assert.ThrowsAsync<HttpRequestException>(() => Service.SyncAsync());
        }
        Assert.Empty(outbox.Messages);
        Assert.True(db.Employees.Single().IsActive);
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task Database_rejects_two_accounts_for_one_HTEL_id_even_when_the_first_is_inactive()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DirectoryDbContext>().UseSqlite(connection).Options;
        using var first = new DirectoryDbContext(options);
        using var second = new DirectoryDbContext(options);
        await first.Database.EnsureCreatedAsync();
        first.Employees.Add(Employee(4551, false));
        second.Employees.Add(Employee(4551));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Explicit_creation_rejects_a_name_that_does_not_belong_to_the_selected_HTEL_identity()
    {
        client.Rows = [Tech()];
        var employee = Employee(first: "Ahlam", last: "AISSI");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ApplyLinkOnEmployeeAsync(employee, 4551));
        Assert.Null(employee.IdTechnicien);
    }

    [Fact]
    public void Unlinked_duplicate_of_an_already_linked_technician_is_visible_for_review()
    {
        var linked = Employee(4551);
        var duplicate = Employee();
        var report = HtelReconciliation.BuildReport([Tech()], [linked, duplicate]);
        var suggestion = Assert.Single(report.Ambiguous);
        Assert.Equal(linked.Id.ToString(), suggestion.LinkedEmployeeId);
        Assert.Equal(duplicate.Id.ToString(), Assert.Single(suggestion.Candidates).EmployeeId);
        Assert.Empty(report.OrphansHtel);
    }
    private sealed class FakeHtel : IHtelTechnicienClient
    {
        public IReadOnlyList<HtelTechnicienDto> Rows { get; set; } = [];
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task<IReadOnlyList<HtelTechnicienDto>> GetTechniciensAsync(CancellationToken ct = default)
        { Calls++; if (Fail) throw new HttpRequestException("HTEL indisponible"); return Task.FromResult(Rows); }
    }
    private sealed class RecordingOutbox : IOutboxWriter
    {
        public List<object> Messages { get; } = [];
        public Task EnqueueAsync<T>(T message, string? aggregateId = null, string? correlationId = null, CancellationToken ct = default) where T : class
        { Messages.Add(message); return Task.CompletedTask; }
    }
}
