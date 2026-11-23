using EmployeeDirectory.Application.Dtos;
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

    /// <summary>v54 : « Chef de projet » et « RP » ne font plus qu'une ligne — celle de RP.</summary>
    [Fact]
    public void Chef_de_projet_est_le_RP()
    {
        Assert.DoesNotContain("Chef de projet", PlatformAccessCatalog.Roles);
        Assert.Contains("RP", PlatformAccessCatalog.Roles);
        Assert.Equal("RP", PlatformAccessCatalog.NormalizeRole("Chef de projet"));
        Assert.Equal("RP", PlatformAccessCatalog.NormalizeRole("chef de projet"));
    }

    /// <summary>v54 : le RP a les droits du superviseur sur les horaires et la validation.</summary>
    [Fact]
    public void Le_RP_a_les_droits_de_planification_du_superviseur()
    {
        var droits = PlatformAccessCatalog.SeedRowsForRole("RP").Select(r => r.Action).ToList();
        Assert.Contains("planning.shift-config.Configure", droits);
        Assert.Contains("planning.validation.Validate", droits);
        Assert.Contains("planning.generate.Act", droits);
    }

    /// <summary>v54 : les droits cochés sur « Chef de projet » sont repris sur « RP », pas perdus.</summary>
    [Fact]
    public async Task Fusion_reprend_les_droits_du_chef_de_projet_sur_le_RP()
    {
        var db = new DirectoryDbContext(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IamPermissions.Add(new IamPermission
        {
            Id = Guid.NewGuid(),
            Role = "Chef de projet",
            Action = "planning.shift-config.Configure",
            Scope = "Global",
            IsAllowed = true,
        });
        await db.SaveChangesAsync();

        var admin = new DirectoryIamAdminService(db);
        await admin.FusionnerAliasAsync();
        await admin.FusionnerAliasAsync();

        Assert.False(await db.IamPermissions.AnyAsync(p => p.Role == "Chef de projet"));
        Assert.True(await db.IamPermissions.AnyAsync(
            p => p.Role == "RP" && p.Action == "planning.shift-config.Configure"
                 && p.Scope == "Global" && p.IsAllowed));
    }

    /// <summary>v54 : sur une base déjà en service, le démarrage ne réécrit plus les droits.</summary>
    [Fact]
    public async Task Base_existante_les_droits_ne_sont_pas_reecrits_au_demarrage()
    {
        var db = new DirectoryDbContext(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IamPermissions.Add(new IamPermission
        {
            Id = Guid.NewGuid(),
            Role = "Superviseur",
            Action = "planning.shift-config.Configure",
            Scope = "Global",
            IsAllowed = true,
        });
        await db.SaveChangesAsync();

        await new DirectoryIamAdminService(db).SeedDefaultsIfEmptyAsync();

        // v61 : seules les rubriques ajoutées depuis (presences.encadrement ; v64 : absences du planning)
        // reçoivent leurs droits par défaut.
        var ajoutees = PlatformAccessCatalog.RubriquesAjoutees.Select(r => r + ".").ToList();
        var lignes = await db.IamPermissions.Select(p => p.Action).ToListAsync();
        Assert.Equal(1, lignes.Count(a => !ajoutees.Any(a.StartsWith)));
        Assert.True(await db.IamPermissions.AnyAsync(
            p => p.Role == "Superviseur" && p.Scope == "Global" && p.IsAllowed));
    }

    [Fact]
    public async Task Rubrique_ajoutee_v61_droits_par_defaut_poses_une_seule_fois()
    {
        var db = new DirectoryDbContext(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.IamPermissions.Add(new IamPermission
        {
            Id = Guid.NewGuid(), Role = "RP", Action = "presences.View", Scope = "Pole", IsAllowed = true,
        });
        await db.SaveChangesAsync();
        var service = new DirectoryIamAdminService(db);

        await service.SeedDefaultsIfEmptyAsync();

        Assert.True(await db.IamPermissions.AnyAsync(p => p.Role == "RP" && p.Action == "presences.encadrement.View" && p.Scope == "Pole" && p.IsAllowed));
        Assert.True(await db.IamPermissions.AnyAsync(p => p.Role == "RP" && p.Action == "presences.encadrement.Act" && p.IsAllowed));
        Assert.False(await db.IamPermissions.AnyAsync(p => p.Role == "Superviseur" && p.Action.StartsWith("presences.encadrement.")));

        // Décoché par le Super Admin : jamais recoché au redémarrage suivant.
        await service.UpsertAsync(new UpsertIamPermissionRequest("RP", "presences.encadrement.Act", "Pole", false));
        await service.SeedDefaultsIfEmptyAsync();
        Assert.False(await db.IamPermissions.AnyAsync(p => p.Role == "RP" && p.Action == "presences.encadrement.Act" && p.IsAllowed));
        Assert.Equal(1, await db.IamPermissions.CountAsync(p => p.Role == "RP" && p.Action == "presences.encadrement.View"));
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
