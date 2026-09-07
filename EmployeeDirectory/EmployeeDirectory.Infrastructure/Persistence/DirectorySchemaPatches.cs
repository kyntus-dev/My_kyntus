using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Persistence;

public static class DirectorySchemaPatches
{
    public static async Task ApplyAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        // Chaque patch est isolé : un échec (ex. contrainte legacy) ne doit pas
        // empêcher les suivants (HTEL, colonnes HR, etc.).
        await EnsureOutboxTableAsync(db, ct);
        await EnsureIamPermissionsTableAsync(db, ct);
        await EnsureEmployeeRowVersionDefaultAsync(db, ct);
        await EnsureBusinessDepartmentSchemaAsync(db, ct);
        await EnsureOrgPoleBusinessDepartmentAsync(db, ct);
        await EnsureEmployeeManagersAndHrProfileAsync(db, ct);
        await EnsureDateDebutFormationColumnAsync(db, ct);
        await EnsureNumeroCarteAutoentrepreneurColumnAsync(db, ct);
        await EnsureEmailPersonnelColumnAsync(db, ct);
        await EnsureEmployeeHtelColumnsAsync(db, ct);
        await EnsureOrgAssignmentNodeUniquenessAsync(db, ct);
        await EnsurePerfIndexesAsync(db, ct);
    }

    /// <summary>
    /// Applique les patches en continuant après une erreur non fatale, pour ne pas
    /// bloquer HTEL / colonnes récentes derrière un script HR legacy.
    /// </summary>
    public static async Task ApplyBestEffortAsync(
        DirectoryDbContext db,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        await RunPatchAsync(nameof(EnsureOutboxTableAsync), () => EnsureOutboxTableAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureIamPermissionsTableAsync), () => EnsureIamPermissionsTableAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureEmployeeRowVersionDefaultAsync), () => EnsureEmployeeRowVersionDefaultAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureBusinessDepartmentSchemaAsync), () => EnsureBusinessDepartmentSchemaAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureOrgPoleBusinessDepartmentAsync), () => EnsureOrgPoleBusinessDepartmentAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureEmployeeManagersAndHrProfileAsync), () => EnsureEmployeeManagersAndHrProfileAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureDateDebutFormationColumnAsync), () => EnsureDateDebutFormationColumnAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureNumeroCarteAutoentrepreneurColumnAsync), () => EnsureNumeroCarteAutoentrepreneurColumnAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureEmailPersonnelColumnAsync), () => EnsureEmailPersonnelColumnAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureEmployeeHtelColumnsAsync), () => EnsureEmployeeHtelColumnsAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsureOrgAssignmentNodeUniquenessAsync), () => EnsureOrgAssignmentNodeUniquenessAsync(db, ct), log);
        await RunPatchAsync(nameof(EnsurePerfIndexesAsync), () => EnsurePerfIndexesAsync(db, ct), log);
    }

    private static async Task RunPatchAsync(
        string name,
        Func<Task> patch,
        ILogger? log)
    {
        try
        {
            await patch();
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Directory schema patch {Patch} failed — continuing.", name);
        }
    }

    /// <summary>Index lecture fréquents (filtres employés / assignments actifs) — capacité ≥300 users.</summary>
    public static async Task EnsurePerfIndexesAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_employees_IsActive_Role"
                ON employees ("IsActive", "Role");
            CREATE INDEX IF NOT EXISTS "IX_employees_IsActive_PoleId"
                ON employees ("IsActive", "PoleId");
            CREATE INDEX IF NOT EXISTS "IX_employees_Email_Active_Lower"
                ON employees (lower("Email"))
                WHERE "IsActive" = true;
            CREATE INDEX IF NOT EXISTS "IX_org_assignments_EffectiveTo_Active"
                ON org_assignments ("EffectiveTo")
                WHERE "EffectiveTo" IS NULL;
            """,
            ct);
    }

    public static async Task EnsureEmployeeHtelColumnsAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "IdTechnicien" integer NULL;
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "HtelCode" character varying(128) NULL;
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "HtelTechnicienName" character varying(256) NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_employees_IdTechnicien"
                ON employees ("IdTechnicien")
                WHERE "IdTechnicien" IS NOT NULL;
            """,
            ct);
    }

    /// <summary>
    /// Multi-responsables : un employé ne peut être titulaire qu'une fois par (Kind, NodeId),
    /// mais plusieurs employés peuvent coexister sur le même nœud.
    /// </summary>
    public static async Task EnsureOrgAssignmentNodeUniquenessAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP INDEX IF EXISTS "IX_org_assignments_Kind_NodeId_Active";

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_org_assignments_Kind_NodeId_Employee_Active"
                ON org_assignments ("Kind", "NodeId", "EmployeeId")
                WHERE "EffectiveTo" IS NULL;

            CREATE INDEX IF NOT EXISTS "IX_org_assignments_Kind_NodeId"
                ON org_assignments ("Kind", "NodeId");
            """,
            ct);
    }

    public static async Task EnsureIamPermissionsTableAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS iam_permissions (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Role" character varying(64) NOT NULL,
                "Action" character varying(128) NOT NULL,
                "Scope" character varying(32) NOT NULL,
                "IsAllowed" boolean NOT NULL DEFAULT TRUE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_iam_permissions_Role_Action_Scope"
                ON iam_permissions ("Role", "Action", "Scope");
            """,
            ct);
    }

    public static async Task EnsureOutboxTableAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS outbox_messages (
                "Id" uuid NOT NULL PRIMARY KEY,
                "MessageType" character varying(512) NOT NULL,
                "PayloadJson" text NOT NULL,
                "CorrelationId" text NULL,
                "AggregateId" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ProcessedAt" timestamp with time zone NULL,
                "Error" text NULL,
                "RetryCount" integer NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS "IX_outbox_messages_ProcessedAt" ON outbox_messages ("ProcessedAt");
            """,
            ct);
    }

    /// <summary>
    /// PostgreSQL n'a pas de rowversion auto : la colonne doit avoir une valeur à l'INSERT.
    /// </summary>
    public static async Task EnsureEmployeeRowVersionDefaultAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employees
                ALTER COLUMN "RowVersion" SET DEFAULT '\x00'::bytea;
            UPDATE employees
                SET "RowVersion" = '\x00'::bytea
                WHERE "RowVersion" IS NULL;
            """,
            ct);
    }

    public static async Task EnsureBusinessDepartmentSchemaAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS business_departments (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Code" character varying(64) NOT NULL,
                "Name" character varying(256) NOT NULL,
                "Kind" integer NOT NULL DEFAULT 0,
                "ManagerEmployeeId" uuid NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_business_departments_Code" ON business_departments ("Code");

            CREATE TABLE IF NOT EXISTS department_pole_assignments (
                "Id" uuid NOT NULL PRIMARY KEY,
                "BusinessDepartmentId" uuid NOT NULL REFERENCES business_departments("Id") ON DELETE CASCADE,
                "PoleId" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_department_pole_assignments_Dept_Pole"
                ON department_pole_assignments ("BusinessDepartmentId", "PoleId");

            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "BusinessDepartmentId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_employees_BusinessDepartmentId" ON employees ("BusinessDepartmentId");
            """,
            ct);
    }

    /// <summary>
    /// FK org_poles → business_departments ; backfill depuis department_pole_assignments
    /// (première liaison par pôle, tri CreatedAt puis Id).
    /// </summary>
    public static async Task EnsureOrgPoleBusinessDepartmentAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE org_poles ADD COLUMN IF NOT EXISTS "BusinessDepartmentId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_org_poles_BusinessDepartmentId" ON org_poles ("BusinessDepartmentId");

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_org_poles_business_departments_BusinessDepartmentId'
                ) THEN
                    ALTER TABLE org_poles
                        ADD CONSTRAINT "FK_org_poles_business_departments_BusinessDepartmentId"
                        FOREIGN KEY ("BusinessDepartmentId") REFERENCES business_departments("Id") ON DELETE SET NULL;
                END IF;
            END $$;

            UPDATE org_poles p
            SET "BusinessDepartmentId" = sub."BusinessDepartmentId"
            FROM (
                SELECT DISTINCT ON (a."PoleId")
                    a."PoleId",
                    a."BusinessDepartmentId"
                FROM department_pole_assignments a
                ORDER BY a."PoleId", a."CreatedAt" ASC, a."Id" ASC
            ) sub
            WHERE p."Id" = sub."PoleId"
              AND p."BusinessDepartmentId" IS NULL;
            """,
            ct);
    }

    public static async Task EnsureEmployeeManagersAndHrProfileAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "ChefDeProjetId" uuid NULL;
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "SuperviseurId" uuid NULL;
            ALTER TABLE employees ADD COLUMN IF NOT EXISTS "ReferentTechniqueId" uuid NULL;

            CREATE TABLE IF NOT EXISTS employee_hr_profiles (
                "EmployeeId" uuid NOT NULL PRIMARY KEY REFERENCES employees("Id") ON DELETE CASCADE,
                "DateNaissance" date NULL,
                "VilleNaissance" character varying(128) NULL,
                "Nationalite" character varying(128) NULL,
                "Sexe" character varying(16) NULL,
                "SituationFamiliale" character varying(64) NULL,
                "NombreEnfants" integer NULL,
                "Cin" character varying(32) NULL,
                "Adresse" character varying(512) NULL,
                "Telephone1" character varying(32) NULL,
                "TelephoneUrgence" character varying(32) NULL,
                "RelationUrgence" character varying(128) NULL,
                "Rib" character varying(64) NULL,
                "ImmatriculationInterne" character varying(64) NULL,
                "ImmatriculationCnss" character varying(64) NULL,
                "DateEntree" date NULL,
                "DateEmbauche" date NULL,
                "DateAnciennete" date NULL,
                "DateSortie" date NULL,
                "DateEvolutionPoste" date NULL,
                "AncienPoste" character varying(256) NULL,
                "AncienService" character varying(256) NULL,
                "NiveauScolaire" character varying(128) NULL,
                "IntitulesEtudes" character varying(512) NULL,
                "EnFormation" boolean NOT NULL DEFAULT FALSE,
                "DateDebutFormation" date NULL,
                "DateFinFormationPrevue" date NULL,
                "NiveauExpertiseMetier" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                "UpdatedAt" timestamp with time zone NULL
            );

            ALTER TABLE employee_hr_profiles
                ALTER COLUMN "EnFormation" SET DEFAULT FALSE;
            UPDATE employee_hr_profiles
                SET "EnFormation" = FALSE
                WHERE "EnFormation" IS NULL;

            UPDATE employee_hr_profiles p
            SET "DateEmbauche" = e."HireDate"::date
            FROM employees e
            WHERE p."EmployeeId" = e."Id"
              AND p."DateEmbauche" IS NULL
              AND e."HireDate" IS NOT NULL;

            INSERT INTO employee_hr_profiles ("EmployeeId", "DateEmbauche", "EnFormation", "CreatedAt")
            SELECT e."Id", e."HireDate"::date, FALSE, NOW()
            FROM employees e
            WHERE NOT EXISTS (
                SELECT 1 FROM employee_hr_profiles p WHERE p."EmployeeId" = e."Id"
            )
              AND e."HireDate" IS NOT NULL;
            """,
            ct);
    }

    public static async Task EnsureDateDebutFormationColumnAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employee_hr_profiles ADD COLUMN IF NOT EXISTS "DateDebutFormation" date NULL;
            """,
            ct);
    }

    public static async Task EnsureNumeroCarteAutoentrepreneurColumnAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employee_hr_profiles ADD COLUMN IF NOT EXISTS "NumeroCarteAutoentrepreneur" character varying(64) NULL;
            """,
            ct);
    }

    public static async Task EnsureEmailPersonnelColumnAsync(DirectoryDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE employee_hr_profiles ADD COLUMN IF NOT EXISTS "EmailPersonnel" character varying(256) NULL;
            """,
            ct);
    }
}
