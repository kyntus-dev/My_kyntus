using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EmployeeDirectory.UnitTests;

/// <summary>v13 : Manager = Superviseur, Coach = Référent technique ; rôles créés par l'Admin.</summary>
public class PlatformAccessRoleFusionTests
{
    [Fact]
    public void Anciens_noms_absents_de_la_matrice_et_rediriges()
    {
        Assert.DoesNotContain("Manager", PlatformAccessCatalog.Roles);
        Assert.DoesNotContain("Coach", PlatformAccessCatalog.Roles);
        Assert.Equal("Superviseur", PlatformAccessCatalog.NormalizeRole("manager"));
        Assert.Equal("Référent technique", PlatformAccessCatalog.NormalizeRole("Coach"));
    }

    [Fact]
    public void Role_personnalise_accepte_nom_invalide_refuse()
    {
        Assert.True(PlatformAccessCatalog.TryNormalizeGrant("Chargé qualité", "planning.self.View", "Self", out var role, out _, out _, out var err));
        Assert.Null(err);
        Assert.Equal("Chargé qualité", role);
        Assert.False(PlatformAccessCatalog.TryNormalizeGrant("<script>", "planning.self.View", "Self", out _, out _, out _, out _));
    }

    [Fact]
    public async Task Fusion_ajoute_les_droits_Coach_au_referent_et_supprime_les_anciennes_lignes()
    {
        var db = new DirectoryDbContext(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IamPermissions.AddRange(
            new IamPermission { Id = Guid.NewGuid(), Role = "Coach", Action = "conges.history.View", Scope = "Cellule", IsAllowed = true },
            new IamPermission { Id = Guid.NewGuid(), Role = "Manager", Action = "qualite.admin.View", Scope = "Pole", IsAllowed = true });
        await db.SaveChangesAsync();

        var admin = new DirectoryIamAdminService(db);
        await admin.FusionnerAliasAsync();
        await admin.FusionnerAliasAsync();

        Assert.False(await db.IamPermissions.AnyAsync(p => p.Role == "Coach" || p.Role == "Manager"));
        Assert.True(await db.IamPermissions.AnyAsync(p => p.Role == "Référent technique" && p.Action == "conges.history.View" && p.IsAllowed));
        Assert.False(await db.IamPermissions.AnyAsync(p => p.Role == "Superviseur" && p.Action == "qualite.admin.View"));
    }
}
