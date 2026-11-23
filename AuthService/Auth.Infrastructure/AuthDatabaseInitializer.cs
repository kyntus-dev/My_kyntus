using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Infrastructure;

public static class AuthDatabaseInitializer
{
    public static void Initialize(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var migrated = false;
        const int maxRetries = 30;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                db.Database.Migrate();
                Console.WriteLine("Auth migrations applied.");
                migrated = true;
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Waiting for DB... attempt {i + 1}/{maxRetries}: {ex.Message}");
                Thread.Sleep(3000);
            }
        }

        if (!migrated)
        {
            Console.WriteLine("ERROR: EF migrations not applied after retries; skipping seed until next restart.");
            return;
        }

        // v60 — Colonne du marqueur Super Admin (faux pour tous les comptes existants).
        try
        {
            EnsureSuperAdminColumn(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: colonne IsSuperAdmin non vérifiée : {ex.Message}");
        }

        try
        {
            EnsureSubjectIdColumn(db);
            EnsureAccessAuditTable(db);
            EnsurePushTables(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: SubjectId schema repair skipped: {ex.Message}");
        }

        // v54 — true seulement à la toute première installation : les rôles ne sont jamais recréés
        // sur une base déjà en service.
        var rolesSeedees = !db.Roles.Any();
        if (rolesSeedees)
        {
            db.Roles.AddRange(
                new Role { Id = 1, Name = "Employee", Description = "Employee du système", CreatedAt = DateTime.UtcNow },
                new Role { Id = 2, Name = "RH", Description = "Responsable des ressources humaines", CreatedAt = DateTime.UtcNow },
                new Role { Id = 3, Name = "Manager", Description = "Manager de planning", CreatedAt = DateTime.UtcNow },
                new Role { Id = 4, Name = "Coach", Description = "Coach des équipes", CreatedAt = DateTime.UtcNow },
                new Role { Id = 5, Name = "RP", Description = "Responsable de production", CreatedAt = DateTime.UtcNow },
                new Role { Id = 6, Name = "Admin", Description = "Administrateur système", CreatedAt = DateTime.UtcNow },
                new Role { Id = 7, Name = "Audit", Description = "Auditeur interne", CreatedAt = DateTime.UtcNow },
                new Role { Id = 8, Name = "Formateur", Description = "Équipe formation — parcours initiale", CreatedAt = DateTime.UtcNow },
                new Role { Id = 9, Name = "Superviseur", Description = "Superviseur de cellule PRIME", CreatedAt = DateTime.UtcNow },
                new Role { Id = 10, Name = "Qualiticien", Description = "Qualiticien — planification formation continue", CreatedAt = DateTime.UtcNow },
                new Role { Id = 11, Name = "Comptabilité", Description = "Comptabilité — synthèse globale / paiement", CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
            Console.WriteLine("Auth roles seeded.");
        }

        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var subjectIdResolver = scope.ServiceProvider.GetRequiredService<ISubjectIdResolver>();

        if (!db.Users.Any())
        {
            if (!configuration.GetValue("DemoSeed:Enabled", false))
            {
                Console.WriteLine("DemoSeed désactivé — seed utilisateurs ignoré.");
            }
            else
            {
                SeedDemoUsers(db, passwordHasher, configuration, subjectIdResolver);
            }
        }
        else if (configuration.GetValue("DemoSeed:Enabled", false))
        {
            EnsureDemoUsers(db, passwordHasher, configuration, subjectIdResolver);
            SyncDemoPasswords(db, passwordHasher, configuration);
            DeactivateLegacyMockUsers(db);
        }

        // v54 — Les rôles de connexion ne sont plus créés ni renommés au démarrage d'une base déjà
        // en service : l'organisation des rôles faite dans l'application doit tenir dans le temps.
        // Ils ne sont posés qu'à la toute première installation (table des rôles vide ci-dessus).
        if (rolesSeedees)
        {
            try
            {
                EnsureRole(db, "Ingénieur", "Ingénieur — cellule IT");
                EnsureRole(db, "Support IT", "Support IT — cellule IT");
                EnsureComptabiliteRole(db);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: rôles de base non ajoutés : {ex.Message}");
            }
        }

        try
        {
            if (configuration.GetValue("DemoSeed:Enabled", false))
            {
                EnsureSuperviseurAccount(db, passwordHasher, configuration, subjectIdResolver);
                EnsureQualiticienAccount(db, passwordHasher, configuration, subjectIdResolver);
                SyncDemoPasswords(db, passwordHasher, configuration);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: superviseur/qualiticien seed skipped: {ex.Message}");
        }

        // v60 — Compte Super Admin « test admin » (créé s'il manque ; aucun autre compte modifié).
        try
        {
            EnsureSuperAdminAccount(db, passwordHasher, configuration, subjectIdResolver);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: compte Super Admin non vérifié : {ex.Message}");
        }

        try
        {
            RealignKnownSubjectIds(db, subjectIdResolver);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: SubjectId realign skipped: {ex.Message}");
        }

        foreach (var user in db.Users.Where(u => u.SubjectId == Guid.Empty).ToList())
            user.SubjectId = subjectIdResolver.ResolveForEmail(user.Email);

        db.SaveChanges();
    }

    /// <summary>Réaligne SubjectId des emails des comptes rôle @kyntus.ma.</summary>
    static void RealignKnownSubjectIds(AuthDbContext db, ISubjectIdResolver subjectIdResolver)
    {
        var emails = new[]
        {
            "employee@kyntus.ma", "rh@kyntus.ma", "manager@kyntus.ma", "coach@kyntus.ma",
            "rp@kyntus.ma", "admin@kyntus.ma", "audit@kyntus.ma", "formation@kyntus.ma",
            "superviseur@kyntus.ma", "qualiticien@kyntus.ma",
        };
        var changed = 0;
        foreach (var email in emails)
        {
            var expected = subjectIdResolver.ResolveForEmail(email);
            var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email);
            if (user is null || user.SubjectId == expected)
                continue;
            // Évite collision UNIQUE SubjectId
            if (db.Users.Any(u => u.Id != user.Id && u.SubjectId == expected))
                continue;
            user.SubjectId = expected;
            changed++;
        }

        if (changed > 0)
        {
            db.SaveChanges();
            Console.WriteLine($"Auth SubjectId realigned ({changed}).");
        }
    }

    static void EnsureSuperAdminColumn(AuthDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"IsSuperAdmin\" boolean NOT NULL DEFAULT false;");
        Console.WriteLine("Auth IsSuperAdmin column ensured.");
    }

    /// <summary>
    /// v60 — Compte Super Admin : rôle Admin + marqueur Super Admin. Nom du compte : « test admin »
    /// (SuperAdminSeed:Username), e-mail SuperAdminSeed:Email (défaut test.admin@kyntus.ma), mot de
    /// passe SuperAdminSeed:Password (KYNTUS_SUPERADMIN_PASSWORD), à défaut celui du compte admin démo.
    /// Un compte existant garde son mot de passe ; seuls le marqueur et le rôle Admin sont garantis.
    /// </summary>
    static void EnsureSuperAdminAccount(
        AuthDbContext db,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver)
    {
        if (!configuration.GetValue("SuperAdminSeed:Enabled", true))
        {
            Console.WriteLine("SuperAdminSeed désactivé — compte Super Admin non vérifié.");
            return;
        }

        var email = configuration["SuperAdminSeed:Email"]?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            email = "test.admin@kyntus.ma";
        var username = configuration["SuperAdminSeed:Username"]?.Trim();
        if (string.IsNullOrWhiteSpace(username))
            username = "test admin";

        var adminRole = db.Roles.FirstOrDefault(r => r.Name == "Admin");
        if (adminRole is null)
        {
            Console.WriteLine("WARNING: rôle Admin introuvable — compte Super Admin non créé.");
            return;
        }

        var emailLower = email.ToLower();
        var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == emailLower);
        if (user is null)
        {
            var password = configuration["SuperAdminSeed:Password"]?.Trim();
            if (string.IsNullOrWhiteSpace(password))
                password = configuration["DemoSeed:Passwords:Admin"]?.Trim();
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine(
                    "WARNING: SuperAdminSeed:Password non défini (KYNTUS_SUPERADMIN_PASSWORD) — compte Super Admin non créé.");
                return;
            }

            db.Users.Add(new User
            {
                Username = username.Length > 50 ? username[..50] : username,
                Email = email,
                SubjectId = subjectIdResolver.ResolveForEmail(email),
                PasswordHash = hasher.HashPassword(password),
                RoleId = adminRole.Id,
                IsActive = true,
                IsSuperAdmin = true,
                CreatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
            Console.WriteLine($"Auth Super Admin « {username} » ({email}) créé.");
            return;
        }

        if (user.IsSuperAdmin && user.RoleId == adminRole.Id)
            return;
        user.IsSuperAdmin = true;
        user.RoleId = adminRole.Id;
        user.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();
        Console.WriteLine($"Auth Super Admin ({email}) : marqueur Super Admin et rôle Admin rétablis.");
    }

    static void EnsureAccessAuditTable(AuthDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AccessAuditEvents" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "At" timestamp with time zone NOT NULL,
                "EventType" character varying(32) NOT NULL,
                "Email" character varying(160) NOT NULL,
                "UserId" integer NULL,
                "IpAddress" character varying(64) NULL,
                "UserAgent" character varying(512) NULL,
                "Message" character varying(256) NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_AccessAuditEvents_At" ON "AccessAuditEvents" ("At");
            """);
        Console.WriteLine("Auth access audit table ensured.");
    }

    static void EnsurePushTables(AuthDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "PushSubscriptions" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "AuthUserId" integer NOT NULL,
                "Endpoint" character varying(1024) NOT NULL,
                "P256dh" character varying(256) NOT NULL,
                "Auth" character varying(256) NOT NULL,
                "UserAgent" character varying(512) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PushSubscriptions_Endpoint" ON "PushSubscriptions" ("Endpoint");
            CREATE INDEX IF NOT EXISTS "IX_PushSubscriptions_AuthUserId" ON "PushSubscriptions" ("AuthUserId");

            CREATE TABLE IF NOT EXISTS "PushInboxItems" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "AuthUserId" integer NOT NULL,
                "Title" character varying(80) NOT NULL,
                "Body" character varying(140) NOT NULL,
                "Tag" character varying(32) NOT NULL,
                "Url" character varying(200) NOT NULL,
                "IsRead" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PushInboxItems_AuthUserId_CreatedAt"
                ON "PushInboxItems" ("AuthUserId", "CreatedAt");
            """);
        Console.WriteLine("Auth push tables ensured.");
    }

    static void EnsureSubjectIdColumn(AuthDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Users' AND column_name = 'SubjectId'
              ) THEN
                ALTER TABLE "Users" ADD COLUMN "SubjectId" uuid;
              END IF;
            END $$;

            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111103'::uuid WHERE lower("Email") = lower('employee@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111104'::uuid WHERE lower("Email") = lower('rh@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111105'::uuid WHERE lower("Email") = lower('manager@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111106'::uuid WHERE lower("Email") = lower('coach@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111107'::uuid WHERE lower("Email") = lower('rp@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111108'::uuid WHERE lower("Email") = lower('admin@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111109'::uuid WHERE lower("Email") = lower('audit@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111110'::uuid WHERE lower("Email") = lower('formation@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111111'::uuid WHERE lower("Email") = lower('superviseur@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = '11111111-1111-4111-8111-111111111112'::uuid WHERE lower("Email") = lower('qualiticien@kyntus.ma');
            UPDATE "Users" SET "SubjectId" = gen_random_uuid() WHERE "SubjectId" IS NULL;

            ALTER TABLE "Users" ALTER COLUMN "SubjectId" SET NOT NULL;

            DROP INDEX IF EXISTS "IX_Users_SubjectId";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_SubjectId" ON "Users" ("SubjectId");

            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT '20260603120000_AddUserSubjectId', '8.0.0'
            WHERE NOT EXISTS (
              SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260603120000_AddUserSubjectId'
            );
            """);
        Console.WriteLine("Auth SubjectId schema ensured.");
    }

    static void SeedDemoUsers(
        AuthDbContext db,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver)
    {
        db.Users.AddRange(BuildDemoUsers(passwordHasher, configuration, subjectIdResolver));
        db.SaveChanges();
        Console.WriteLine("Auth users seeded.");
    }

    static void EnsureDemoUsers(
        AuthDbContext db,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver)
    {
        var added = 0;
        foreach (var user in BuildDemoUsers(passwordHasher, configuration, subjectIdResolver))
        {
            if (db.Users.Any(u => u.Email.ToLower() == user.Email.ToLower()))
                continue;

            db.Users.Add(user);
            added++;
        }

        if (added > 0)
        {
            db.SaveChanges();
            Console.WriteLine($"Auth demo users ensured ({added} added).");
        }
    }

    static IEnumerable<User> BuildDemoUsers(
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver) =>
    [
        SeedUser("Employee", "employee@kyntus.ma", DemoPassword(configuration, "Employee"), 1, passwordHasher, subjectIdResolver),
        SeedUser("rh", "rh@kyntus.ma", DemoPassword(configuration, "RH"), 2, passwordHasher, subjectIdResolver),
        SeedUser("manager", "manager@kyntus.ma", DemoPassword(configuration, "Manager"), 3, passwordHasher, subjectIdResolver),
        SeedUser("coach", "coach@kyntus.ma", DemoPassword(configuration, "Coach"), 4, passwordHasher, subjectIdResolver),
        SeedUser("rp", "rp@kyntus.ma", DemoPassword(configuration, "RP"), 5, passwordHasher, subjectIdResolver),
        SeedUser("admin", "admin@kyntus.ma", DemoPassword(configuration, "Admin"), 6, passwordHasher, subjectIdResolver),
        SeedUser("audit", "audit@kyntus.ma", DemoPassword(configuration, "Audit"), 7, passwordHasher, subjectIdResolver),
        SeedUser("equipeformation", "formation@kyntus.ma", DemoPassword(configuration, "Formation"), 8, passwordHasher, subjectIdResolver),
        SeedUser("superviseur", "superviseur@kyntus.ma", DemoPassword(configuration, "Superviseur"), 9, passwordHasher, subjectIdResolver),
        SeedUser("qualiticien", "qualiticien@kyntus.ma", DemoPassword(configuration, "Qualiticien"), 10, passwordHasher, subjectIdResolver),
    ];

    /// <summary>Aligne les hashs des comptes rôle sur DemoSeed:Passwords (ex. Azerty@2026).</summary>
    static void SyncDemoPasswords(
        AuthDbContext db,
        IPasswordHasher passwordHasher,
        IConfiguration configuration)
    {
        var rolePasswordByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["employee@kyntus.ma"] = DemoPassword(configuration, "Employee"),
            ["rh@kyntus.ma"] = DemoPassword(configuration, "RH"),
            ["manager@kyntus.ma"] = DemoPassword(configuration, "Manager"),
            ["coach@kyntus.ma"] = DemoPassword(configuration, "Coach"),
            ["rp@kyntus.ma"] = DemoPassword(configuration, "RP"),
            ["admin@kyntus.ma"] = DemoPassword(configuration, "Admin"),
            ["audit@kyntus.ma"] = DemoPassword(configuration, "Audit"),
            ["formation@kyntus.ma"] = DemoPassword(configuration, "Formation"),
            ["superviseur@kyntus.ma"] = DemoPassword(configuration, "Superviseur"),
            ["qualiticien@kyntus.ma"] = DemoPassword(configuration, "Qualiticien"),
        };

        var updated = 0;
        foreach (var (email, password) in rolePasswordByEmail)
        {
            var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
            if (user is null)
                continue;
            if (passwordHasher.VerifyPassword(user.PasswordHash, password))
                continue;

            user.PasswordHash = passwordHasher.HashPassword(password);
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0)
        {
            db.SaveChanges();
            Console.WriteLine($"Auth demo passwords synced ({updated} updated).");
        }
    }

    /// <summary>Désactive les anciens comptes mock hors set rôles @kyntus.ma.</summary>
    static void DeactivateLegacyMockUsers(AuthDbContext db)
    {
        var legacyEmails = new[]
        {
            "formateur@gmail.com",
            "formateur@kyntus.ma",
            "yasmine.elamrani@atlas-tech-demo.dev",
            "fatima.alaoui@atlas-tech-demo.dev",
        };

        var deactivated = 0;
        foreach (var email in legacyEmails)
        {
            var user = db.Users.FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
            if (user is null || !user.IsActive)
                continue;
            user.IsActive = false;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.UpdatedAt = DateTime.UtcNow;
            deactivated++;
        }

        if (deactivated > 0)
        {
            db.SaveChanges();
            Console.WriteLine($"Auth legacy mock users deactivated ({deactivated}).");
        }
    }

    static User SeedUser(
        string username,
        string email,
        string password,
        int roleId,
        IPasswordHasher hasher,
        ISubjectIdResolver subjectIdResolver) =>
        new()
        {
            Username = username,
            Email = email,
            SubjectId = subjectIdResolver.ResolveForEmail(email),
            PasswordHash = hasher.HashPassword(password),
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

    static string DemoPassword(IConfiguration configuration, string roleKey)
    {
        var password = configuration[$"DemoSeed:Passwords:{roleKey}"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                $"DemoSeed:Passwords:{roleKey} est requis pour le seed démo (appsettings ou KYNTUS_DEMO_SEED_*).");
        return password;
    }

    /// <summary>Ajoute un rôle s'il n'existe pas (aucune autre donnée modifiée).</summary>
    static void EnsureRole(AuthDbContext db, string name, string description)
    {
        if (db.Roles.Any(r => r.Name == name))
            return;
        var nextId = (db.Roles.Max(r => (int?)r.Id) ?? 0) + 1;
        db.Roles.Add(new Role { Id = nextId, Name = name, Description = description, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Console.WriteLine($"Auth role {name} added.");
    }

    static void EnsureComptabiliteRole(AuthDbContext db)
    {
        var role = db.Roles.FirstOrDefault(r => r.Name == "Comptabilité")
                   ?? db.Roles.FirstOrDefault(r => r.Name == "Comptable");
        if (role == null)
        {
            var nextId = (db.Roles.Max(r => (int?)r.Id) ?? 0) + 1;
            db.Roles.Add(new Role
            {
                Id = nextId,
                Name = "Comptabilité",
                Description = "Comptabilité — synthèse globale / paiement",
                CreatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
            Console.WriteLine("Auth role Comptabilité added.");
            return;
        }

        if (role.Name == "Comptable")
        {
            role.Name = "Comptabilité";
            role.Description = "Comptabilité — synthèse globale / paiement";
            db.SaveChanges();
            Console.WriteLine("Auth role Comptable renamed to Comptabilité.");
        }
    }

    static void EnsureSuperviseurAccount(
        AuthDbContext db,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver)
    {
        var role = db.Roles.FirstOrDefault(r => r.Name == "Superviseur");
        if (role == null)
        {
            var nextId = (db.Roles.Max(r => (int?)r.Id) ?? 0) + 1;
            role = new Role
            {
                Id = nextId,
                Name = "Superviseur",
                Description = "Superviseur de cellule PRIME",
                CreatedAt = DateTime.UtcNow,
            };
            db.Roles.Add(role);
            db.SaveChanges();
            Console.WriteLine("Auth role Superviseur added.");
        }

        if (db.Users.Any(u => u.Email.ToLower() == "superviseur@kyntus.ma"))
            return;

        db.Users.Add(new User
        {
            Username = "superviseur",
            Email = "superviseur@kyntus.ma",
            SubjectId = subjectIdResolver.ResolveForEmail("superviseur@kyntus.ma"),
            PasswordHash = hasher.HashPassword(DemoPassword(configuration, "Superviseur")),
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        Console.WriteLine("Auth user superviseur@kyntus.ma added.");
    }

    static void EnsureQualiticienAccount(
        AuthDbContext db,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ISubjectIdResolver subjectIdResolver)
    {
        var role = db.Roles.FirstOrDefault(r => r.Name == "Qualiticien");
        if (role == null)
        {
            var nextId = (db.Roles.Max(r => (int?)r.Id) ?? 0) + 1;
            role = new Role
            {
                Id = nextId,
                Name = "Qualiticien",
                Description = "Qualiticien — planification formation continue",
                CreatedAt = DateTime.UtcNow,
            };
            db.Roles.Add(role);
            db.SaveChanges();
            Console.WriteLine("Auth role Qualiticien added.");
        }

        if (db.Users.Any(u => u.Email.ToLower() == "qualiticien@kyntus.ma"))
            return;

        // Réutilise le mot de passe Formation si Qualiticien non défini (évite plantage seed).
        string password;
        try
        {
            password = DemoPassword(configuration, "Qualiticien");
        }
        catch
        {
            password = DemoPassword(configuration, "Formation");
        }

        db.Users.Add(new User
        {
            Username = "qualiticien",
            Email = "qualiticien@kyntus.ma",
            SubjectId = subjectIdResolver.ResolveForEmail("qualiticien@kyntus.ma"),
            PasswordHash = hasher.HashPassword(password),
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        Console.WriteLine("Auth user qualiticien@kyntus.ma added.");
    }
}
