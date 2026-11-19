using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// v57 — Le périmètre d'un chef de projet (RP) est exactement ce qui lui est inscrit : les pôles
/// dont il est responsable et ceux des contrats qu'il dirige. Plus d'élargissement automatique aux
/// autres contrats portant le même premier mot (« Bytel Kyntus » n'ouvre plus « Bytel TISSEO »).
/// </summary>
public class PerimetreChefDeProjetTests : IDisposable
{
    private static readonly Guid Kyntus = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tisseo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly DirectoryDbContext _db;
    private readonly Guid _rp = Guid.NewGuid();

    public PerimetreChefDeProjetTests()
    {
        _db = new DirectoryDbContext(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        _db.BusinessDepartments.AddRange(
            new BusinessDepartment { Id = Kyntus, Code = "OP-001", Name = "Bytel Kyntus", Kind = BusinessDepartmentKind.Operational, IsActive = true },
            new BusinessDepartment { Id = Tisseo, Code = "OP-002", Name = "Bytel TISSEO", Kind = BusinessDepartmentKind.Operational, IsActive = true });
        _db.OrgPoles.AddRange(
            new OrgPole { Id = "pole-tech", Name = "Pôle technique", BusinessDepartmentId = Kyntus },
            new OrgPole { Id = "pole-btb", Name = "Pôle BTB", BusinessDepartmentId = Kyntus },
            new OrgPole { Id = "pole-tisseo", Name = "Pôle TISSEO", BusinessDepartmentId = Tisseo },
            new OrgPole { Id = "pole-archive", Name = "Ancien pôle", BusinessDepartmentId = Tisseo, IsArchived = true });
        _db.Employees.Add(new Employee
        {
            Id = _rp,
            Email = "rp@kyntus.com",
            FirstName = "Chef",
            LastName = "Projet",
            Role = "RP",
            IsActive = true,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void Inscrire(string poleId) =>
        _db.OrgAssignments.Add(new OrgAssignment
        {
            Id = Guid.NewGuid(),
            EmployeeId = _rp,
            Kind = OrgAssignmentKind.ChefDeProjet,
            NodeId = poleId,
            NodeLevel = OrgNodeLevel.Pole,
            EffectiveFrom = DateTime.UtcNow.AddDays(-30),
        });

    [Fact]
    public async Task Le_perimetre_se_limite_aux_poles_inscrits()
    {
        Inscrire("pole-tech");
        await _db.SaveChangesAsync();

        var poles = await PerimetreActivite.PolesChefDeProjetAsync(_db, _rp);

        Assert.Equal(["pole-tech"], poles);
        Assert.DoesNotContain("pole-tisseo", poles);  // même activité « Bytel », mais pas inscrit
        Assert.DoesNotContain("pole-btb", poles);     // même contrat, mais pas inscrit
    }

    [Fact]
    public async Task Un_contrat_dirige_ouvre_ses_poles_actifs_seulement()
    {
        var contrat = await _db.BusinessDepartments.FirstAsync(d => d.Id == Tisseo);
        contrat.ManagerEmployeeId = _rp;
        await _db.SaveChangesAsync();

        var poles = await PerimetreActivite.PolesChefDeProjetAsync(_db, _rp);

        Assert.Equal(["pole-tisseo"], poles);
        Assert.DoesNotContain("pole-archive", poles);
        Assert.DoesNotContain("pole-tech", poles);
    }

    [Fact]
    public async Task Une_responsabilite_retiree_sort_du_perimetre_immediatement()
    {
        Inscrire("pole-tech");
        Inscrire("pole-tisseo");
        await _db.SaveChangesAsync();
        Assert.Equal(2, (await PerimetreActivite.PolesChefDeProjetAsync(_db, _rp)).Count);

        // Retrait du contrat TISSEO : la responsabilité est close.
        var retiree = await _db.OrgAssignments.FirstAsync(a => a.NodeId == "pole-tisseo");
        retiree.EffectiveTo = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var poles = await PerimetreActivite.PolesChefDeProjetAsync(_db, _rp);

        Assert.Equal(["pole-tech"], poles);
    }

    [Fact]
    public async Task Sans_responsabilite_le_perimetre_est_vide()
    {
        Assert.Empty(await PerimetreActivite.PolesChefDeProjetAsync(_db, _rp));
    }
}
