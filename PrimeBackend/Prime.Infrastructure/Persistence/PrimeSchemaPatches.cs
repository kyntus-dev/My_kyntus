using Microsoft.EntityFrameworkCore;

namespace Prime.Infrastructure.Persistence;

/// <summary>
/// Correctifs idempotents si une migration EF n’a pas été appliquée (ex. fichier sans Designer).
/// </summary>
public static class PrimeSchemaPatches
{
    public static async Task EnsureOrgOptionalAndDraftRootPoleAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee", ct) ||
            !await TableExistsAsync(db, "prime_supervisor_cellule_prime_draft", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'prime_employee'
                  AND column_name = 'CelluleId' AND is_nullable = 'NO'
              ) THEN
                ALTER TABLE prime_employee ALTER COLUMN "CelluleId" DROP NOT NULL;
              END IF;
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'prime_employee'
                  AND column_name = 'ServiceId' AND is_nullable = 'NO'
              ) THEN
                ALTER TABLE prime_employee ALTER COLUMN "ServiceId" DROP NOT NULL;
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'prime_supervisor_cellule_prime_draft'
                  AND column_name = 'RootPoleId'
              ) THEN
                ALTER TABLE prime_supervisor_cellule_prime_draft
                  ADD COLUMN "RootPoleId" character varying(128);
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_supervisor_cellule_prime_draft AS d
            SET "RootPoleId" = c."PoleId"
            FROM prime_cellule AS c
            WHERE (d."RootPoleId" IS NULL OR d."RootPoleId" = '')
              AND d."CelluleId" = c."Id";
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_supervisor_cellule_prime_draft AS d
            SET "RootPoleId" = d."CelluleId"
            WHERE (d."RootPoleId" IS NULL OR d."RootPoleId" = '')
              AND EXISTS (SELECT 1 FROM prime_pole p WHERE p."Id" = d."CelluleId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM prime_supervisor_cellule_prime_draft d
            WHERE d."RootPoleId" IS NULL OR d."RootPoleId" = ''
               OR NOT EXISTS (SELECT 1 FROM prime_pole p WHERE p."Id" = d."RootPoleId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'prime_supervisor_cellule_prime_draft'
                  AND column_name = 'RootPoleId' AND is_nullable = 'YES'
              ) THEN
                ALTER TABLE prime_supervisor_cellule_prime_draft
                  ALTER COLUMN "RootPoleId" SET NOT NULL;
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DROP INDEX IF EXISTS "IX_prime_supervisor_cellule_prime_draft_SupervisorUserId_CelluleId_Period_TemplateId";
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_prime_supervisor_cellule_prime_draft_SupervisorUserId_CelluleId_Period_TemplateId"
              ON prime_supervisor_cellule_prime_draft ("SupervisorUserId", "CelluleId", "Period", "TemplateId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM prime_supervisor_cellule_prime_draft a
            USING prime_supervisor_cellule_prime_draft b
            WHERE a.ctid < b.ctid
              AND a."SupervisorUserId" = b."SupervisorUserId"
              AND a."RootPoleId" = b."RootPoleId"
              AND a."Period" = b."Period";
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_supervisor_cellule_prime_draft_SupervisorUserId_RootPoleId_Period"
              ON prime_supervisor_cellule_prime_draft ("SupervisorUserId", "RootPoleId", "Period");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_prime_supervisor_cellule_prime_draft_RootPoleId"
              ON prime_supervisor_cellule_prime_draft ("RootPoleId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'FK_prime_supervisor_cellule_prime_draft_prime_pole_RootPoleId'
              ) THEN
                ALTER TABLE prime_supervisor_cellule_prime_draft
                  ADD CONSTRAINT "FK_prime_supervisor_cellule_prime_draft_prime_pole_RootPoleId"
                  FOREIGN KEY ("RootPoleId") REFERENCES prime_pole ("Id") ON DELETE RESTRICT;
              END IF;
            END $$;
            """,
            ct);

        await EnsureValidationQueueRepairAsync(db, ct);
        await EnsureFicheValidationHistoryTableAsync(db, ct);
        await EnsureGlobalPoolScopeSynthesisTablesAsync(db, ct);
    }

    /// <summary>Ajoute les colonnes paiement à la table ligne de synthèse (bases déjà créées).</summary>
    public static async Task EnsureSynthesisLinePaymentColumnsAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_global_pool_synthesis_line", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "PaymentStatus" character varying(32) NOT NULL DEFAULT 'Unpaid';
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "PaidByUserId" character varying(128);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "PaymentReference" character varying(256);
            """,
            ct);
    }

    /// <summary>Ajoute les colonnes double validation RH/Manager sur les lignes de synthèse (bases déjà créées).</summary>
    public static async Task EnsureSynthesisLineDualDecisionColumnsAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_global_pool_synthesis_line", ct))
            return;

        // Colonne par colonne (IF NOT EXISTS) : évite les états partiels si RhDecision existe sans ManagerDecidedAt.
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RhDecision" character varying(32) NOT NULL DEFAULT 'Pending';
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RhDecidedByUserId" character varying(128);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RhDecidedAt" timestamp with time zone;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RhRejectionReason" character varying(2048);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "ManagerDecision" character varying(32) NOT NULL DEFAULT 'Pending';
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "ManagerDecidedByUserId" character varying(128);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "ManagerDecidedAt" timestamp with time zone;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "ManagerRejectionReason" character varying(2048);
            """,
            ct);
    }

    /// <summary>Correctifs colonnes ligne synthèse (paiement + double validation) — idempotent au démarrage.</summary>
    public static async Task EnsureGlobalPoolSynthesisLineSchemaAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        await EnsureSynthesisLinePaymentColumnsAsync(db, ct);
        await EnsureSynthesisLineDualDecisionColumnsAsync(db, ct);
        await EnsureSynthesisLineAbsenceSanctionColumnsAsync(db, ct);
    }

    public static async Task EnsureAbsenceSanctionConfigTableAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee", ct))
            return;
        if (await TableExistsAsync(db, "prime_absence_sanction_config", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS prime_absence_sanction_config (
                "Id" character varying(32) NOT NULL,
                "DivisorDays" integer NOT NULL DEFAULT 26,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "UpdatedByUserId" character varying(128),
                CONSTRAINT "PK_prime_absence_sanction_config" PRIMARY KEY ("Id")
            );
            INSERT INTO prime_absence_sanction_config ("Id", "DivisorDays", "UpdatedAt")
            VALUES ('default', 26, NOW())
            ON CONFLICT ("Id") DO NOTHING;
            """,
            ct);
    }

    public static async Task EnsureSynthesisLineAbsenceSanctionColumnsAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_global_pool_synthesis_line", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "AbsenceDayCount" integer NOT NULL DEFAULT 0;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "SanctionAmount" numeric(12,2) NOT NULL DEFAULT 0;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RegularizationAmount" numeric(12,2) NOT NULL DEFAULT 0;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "NetPayableAmount" numeric(12,2);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "AbsenceComputedAt" timestamp with time zone;
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RegularizationUpdatedByUserId" character varying(128);
            ALTER TABLE prime_global_pool_synthesis_line
              ADD COLUMN IF NOT EXISTS "RegularizationUpdatedAt" timestamp with time zone;
            """,
            ct);
    }

    /// <summary>
    /// Colonnes snapshot détail fiche employé (migration 20260608140000 non appliquée sur certaines bases).
    /// </summary>
    public static async Task EnsureEmployeeFicheDetailSnapshotColumnsAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee_prime_service_fiche", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_employee_prime_service_fiche
              ADD COLUMN IF NOT EXISTS "DetailGridJson" text;
            ALTER TABLE prime_employee_prime_service_fiche
              ADD COLUMN IF NOT EXISTS "DetailGridPreviewSheetName" character varying(256);
            ALTER TABLE prime_employee_prime_service_fiche
              ADD COLUMN IF NOT EXISTS "TemplateVersionRef" character varying(256);
            ALTER TABLE prime_employee_prime_service_fiche
              ADD COLUMN IF NOT EXISTS "DetailGridFrozenAt" timestamp with time zone;
            ALTER TABLE prime_employee_prime_service_fiche
              ADD COLUMN IF NOT EXISTS "PonderationsSnapshotJson" text;
            """,
            ct);
    }

    public static async Task EnsureGlobalPoolScopeSynthesisTablesAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee_prime_service_fiche", ct))
            return;
        if (await TableExistsAsync(db, "prime_global_pool_scope_synthesis", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE prime_global_pool_scope_synthesis (
                "Id" uuid NOT NULL,
                "Period" character varying(16) NOT NULL,
                "ScopeType" character varying(16) NOT NULL,
                "ScopeId" character varying(128) NOT NULL,
                "ScopeDisplayName" character varying(512) NOT NULL DEFAULT '',
                "ExcelContent" bytea,
                "FileName" character varying(512),
                "GeneratedAt" timestamp with time zone,
                "GeneratedByUserId" character varying(128),
                "ManagerApprovedAt" timestamp with time zone,
                "ManagerApprovedByUserId" character varying(128),
                "RhApprovedAt" timestamp with time zone,
                "RhApprovedByUserId" character varying(128),
                "ComptaAckAt" timestamp with time zone,
                "ComptaAckByUserId" character varying(128),
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_prime_global_pool_scope_synthesis" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX "IX_prime_global_pool_scope_synthesis_Period_ScopeType_ScopeId"
                ON prime_global_pool_scope_synthesis ("Period", "ScopeType", "ScopeId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE prime_global_pool_synthesis_line (
                "Id" uuid NOT NULL,
                "ScopeSynthesisId" uuid NOT NULL,
                "FicheId" uuid NOT NULL,
                "EmployeeId" character varying(128) NOT NULL,
                "ServiceId" character varying(128) NOT NULL,
                "PrimeAmount" numeric(12,2),
                "ChallengeAmount" numeric(12,2),
                "TotalAmount" numeric(12,2),
                "LineStatus" character varying(32) NOT NULL DEFAULT 'PendingReview',
                "RhDecision" character varying(32) NOT NULL DEFAULT 'Pending',
                "RhDecidedByUserId" character varying(128),
                "RhDecidedAt" timestamp with time zone,
                "RhRejectionReason" character varying(2048),
                "ManagerDecision" character varying(32) NOT NULL DEFAULT 'Pending',
                "ManagerDecidedByUserId" character varying(128),
                "ManagerDecidedAt" timestamp with time zone,
                "ManagerRejectionReason" character varying(2048),
                "RejectedByUserId" character varying(128),
                "RejectedByRole" character varying(64),
                "RejectedAt" timestamp with time zone,
                "RejectionReason" character varying(2048),
                "PaymentStatus" character varying(32) NOT NULL DEFAULT 'Unpaid',
                "PaidAt" timestamp with time zone,
                "PaidByUserId" character varying(128),
                "PaymentReference" character varying(256),
                CONSTRAINT "PK_prime_global_pool_synthesis_line" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_prime_global_pool_synthesis_line_scope"
                    FOREIGN KEY ("ScopeSynthesisId") REFERENCES prime_global_pool_scope_synthesis ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_prime_global_pool_synthesis_line_ScopeSynthesisId_FicheId"
                ON prime_global_pool_synthesis_line ("ScopeSynthesisId", "FicheId");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE prime_global_pool_synthesis_line_history (
                "Id" uuid NOT NULL,
                "LineId" uuid NOT NULL,
                "At" timestamp with time zone NOT NULL,
                "Action" character varying(32) NOT NULL,
                "ActorUserId" character varying(128) NOT NULL,
                "ActorRole" character varying(64) NOT NULL,
                "Comment" character varying(2048),
                CONSTRAINT "PK_prime_global_pool_synthesis_line_history" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_prime_global_pool_synthesis_line_history_line"
                    FOREIGN KEY ("LineId") REFERENCES prime_global_pool_synthesis_line ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_prime_global_pool_synthesis_line_history_LineId_At"
                ON prime_global_pool_synthesis_line_history ("LineId", "At");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'prime_global_pool_approval'
                  AND column_name = 'ScopeSynthesisId'
              ) THEN
                ALTER TABLE prime_global_pool_approval ADD COLUMN "ScopeSynthesisId" uuid;
                ALTER TABLE prime_global_pool_approval ALTER COLUMN "DraftId" DROP NOT NULL;
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_prime_global_pool_approval_scope_synthesis'
              ) THEN
                ALTER TABLE prime_global_pool_approval
                  ADD CONSTRAINT "FK_prime_global_pool_approval_scope_synthesis"
                  FOREIGN KEY ("ScopeSynthesisId")
                  REFERENCES prime_global_pool_scope_synthesis ("Id") ON DELETE CASCADE;
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_global_pool_approval_ScopeSynthesisId_StepId"
                ON prime_global_pool_approval ("ScopeSynthesisId", "StepId")
                WHERE "ScopeSynthesisId" IS NOT NULL;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT '20260524140000_GlobalPoolScopeSynthesis', '8.0.11'
            WHERE NOT EXISTS (
                SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = '20260524140000_GlobalPoolScopeSynthesis');
            """,
            ct);
    }

    /// <summary>
    /// Table historique validation fiche (migration <c>20260524120000</c> sans Designer / base déjà migrée).
    /// </summary>
    public static async Task EnsureFicheValidationHistoryTableAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee_prime_service_fiche", ct))
            return;
        if (await TableExistsAsync(db, "prime_employee_fiche_validation_history", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE prime_employee_fiche_validation_history (
                "Id" uuid NOT NULL,
                "FicheId" uuid NOT NULL,
                "At" timestamp with time zone NOT NULL,
                "Action" character varying(32) NOT NULL,
                "FromStatus" character varying(64) NOT NULL,
                "ToStatus" character varying(64) NOT NULL,
                "ActorUserId" character varying(128) NOT NULL,
                "ActorRole" character varying(64) NOT NULL,
                "ActorDisplayName" character varying(256),
                "Comment" character varying(2048),
                "PrimeAmount" numeric(12,2),
                "ChallengeAmount" numeric(12,2),
                "TotalAmount" numeric(12,2),
                CONSTRAINT "PK_prime_employee_fiche_validation_history" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_prime_employee_fiche_validation_history_prime_employee_prime_~"
                    FOREIGN KEY ("FicheId")
                    REFERENCES prime_employee_prime_service_fiche ("Id")
                    ON DELETE CASCADE
            );
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX "IX_prime_employee_fiche_validation_history_FicheId_At"
                ON prime_employee_fiche_validation_history ("FicheId", "At");
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT '20260524120000_EmployeeFicheValidationHistory', '8.0.11'
            WHERE NOT EXISTS (
                SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = '20260524120000_EmployeeFicheValidationHistory');
            """,
            ct);
    }

    /// <summary>Rattrapage SQL : fiches complètes + brouillon Validated (même superviseur / cellule / période) → Pending.</summary>
    public static async Task EnsureValidationQueueRepairAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee_prime_service_fiche", ct) ||
            !await TableExistsAsync(db, "prime_supervisor_cellule_prime_draft", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_employee_prime_service_fiche f
            SET "ValidationStatus" = 'Pending', "UpdatedAt" = NOW()
            FROM prime_supervisor_cellule_prime_draft d
            WHERE f."CellulePrimeDraftId" = d."Id"
              AND f."ValidationStatus" IN ('AwaitingData', 'NotStarted')
              AND UPPER(TRIM(f."FillingStatus")) = 'COMPLETE'
              AND UPPER(TRIM(d."Status")) = 'VALIDATED';
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_employee_prime_service_fiche f
            SET "ValidationStatus" = 'Pending',
                "CellulePrimeDraftId" = d."Id",
                "UpdatedAt" = NOW()
            FROM prime_supervisor_cellule_prime_draft d
            WHERE f."SupervisorUserId" = d."SupervisorUserId"
              AND f."CelluleId" = d."CelluleId"
              AND f."Period" = d."Period"
              AND f."ValidationStatus" IN ('AwaitingData', 'NotStarted')
              AND UPPER(TRIM(f."FillingStatus")) = 'COMPLETE'
              AND UPPER(TRIM(d."Status")) = 'VALIDATED'
              AND NOT EXISTS (
                SELECT 1 FROM prime_supervisor_cellule_prime_draft d2
                WHERE d2."Id" = f."CellulePrimeDraftId"
                  AND UPPER(TRIM(d2."Status")) = 'VALIDATED'
              );
            """,
            ct);
    }

    private static Task<bool> TableExistsAsync(PrimeDbContext db, string tableName, CancellationToken ct) =>
        tableName switch
        {
            "prime_employee" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_employee');
                """,
                ct),
            "prime_supervisor_cellule_prime_draft" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_supervisor_cellule_prime_draft');
                """,
                ct),
            "prime_employee_prime_service_fiche" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_employee_prime_service_fiche');
                """,
                ct),
            "prime_employee_fiche_validation_history" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_employee_fiche_validation_history');
                """,
                ct),
            "prime_workflow_step" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_workflow_step');
                """,
                ct),
            "prime_rbac_permission" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_rbac_permission');
                """,
                ct),
            "prime_global_pool_scope_synthesis" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_global_pool_scope_synthesis');
                """,
                ct),
            "prime_global_pool_synthesis_line" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_global_pool_synthesis_line');
                """,
                ct),
            "prime_absence_sanction_config" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_absence_sanction_config');
                """,
                ct),
            "prime_common_line_ponderation" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_common_line_ponderation');
                """,
                ct),
            "prime_service" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_service');
                """,
                ct),
            "prime_service_pole_line_ponderation" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_service_pole_line_ponderation');
                """,
                ct),
            "prime_service_prime_indicator" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_service_prime_indicator');
                """,
                ct),
            "prime_employee_service_indicator_ponderation" => ScalarBoolAsync(
                db,
                """
                SELECT EXISTS (
                  SELECT 1 FROM information_schema.tables
                  WHERE table_schema = 'public' AND table_name = 'prime_employee_service_indicator_ponderation');
                """,
                ct),
            _ => Task.FromResult(false),
        };

    private static async Task<bool> ScalarBoolAsync(PrimeDbContext db, string sql, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            var scalar = await cmd.ExecuteScalarAsync(ct);
            return scalar switch
            {
                true => true,
                false => false,
                long l => l != 0,
                int i => i != 0,
                _ => false,
            };
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Colonnes responsabilités manager sur prime_employee (DirectoryEmployeeChangedMessage).</summary>
    public static async Task EnsureEmployeeManagerIdColumnsAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_employee", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_employee ADD COLUMN IF NOT EXISTS "ReferentTechniqueId" character varying(128) NULL;
            ALTER TABLE prime_employee ADD COLUMN IF NOT EXISTS "ChefDeProjetId" character varying(128) NULL;
            ALTER TABLE prime_employee ADD COLUMN IF NOT EXISTS "SuperviseurId" character varying(128) NULL;
            """,
            ct);
    }

    /// <summary>
    /// Répare le circuit opérationnel des fiches et ses permissions.
    /// Certaines bases ont conservé le flux V3 qui démarrait directement par le Superviseur.
    /// </summary>
    public static async Task EnsureOperationalValidationWorkflowAsync(
        PrimeDbContext db,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_workflow_step", ct) ||
            !await TableExistsAsync(db, "prime_rbac_permission", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_workflow_step
            SET "IsActive" = FALSE, "UpdatedAt" = NOW()
            WHERE "ApproverRole" IN ('RH', 'Manager', 'Comptabilité', 'Comptable', 'Admin', 'Audit', 'Pilote');

            INSERT INTO prime_workflow_step (
                "Id", "SortOrder", "ApproverRole", "FromStatus", "ToStatus",
                "IsActive", "SlaHours", "CapturesAmountsOnApproval", "TerminalApproved",
                "CreatedAt", "UpdatedAt")
            VALUES (
                gen_random_uuid(), 1, 'Référent technique', 'Pending', 'Référent technique Approved',
                TRUE, 48, TRUE, FALSE, NOW(), NULL)
            ON CONFLICT ("FromStatus", "ApproverRole", "ToStatus")
            DO UPDATE SET
                "SortOrder" = EXCLUDED."SortOrder",
                "IsActive" = TRUE,
                "SlaHours" = EXCLUDED."SlaHours",
                "CapturesAmountsOnApproval" = TRUE,
                "TerminalApproved" = FALSE,
                "UpdatedAt" = NOW();

            UPDATE prime_workflow_step
            SET "SortOrder" = 2,
                "FromStatus" = 'Référent technique Approved',
                "ToStatus" = 'Superviseur Approved',
                "IsActive" = TRUE,
                "TerminalApproved" = FALSE,
                "UpdatedAt" = NOW()
            WHERE "ApproverRole" = 'Superviseur';

            UPDATE prime_workflow_step
            SET "SortOrder" = 3,
                "FromStatus" = 'Superviseur Approved',
                "ToStatus" = 'Chef de projet Approved',
                "IsActive" = TRUE,
                "TerminalApproved" = TRUE,
                "UpdatedAt" = NOW()
            WHERE "ApproverRole" IN ('Chef de projet', 'RP');

            INSERT INTO prime_rbac_permission (
                "Id", "Role", "Action", "Scope", "IsAllowed", "CreatedAt", "UpdatedAt")
            VALUES
                (gen_random_uuid(), 'Référent technique', 'Read', 'Service', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Référent technique', 'Validate', 'Service', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Coach', 'Read', 'Service', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Coach', 'Validate', 'Service', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Superviseur', 'Read', 'Cellule', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Superviseur', 'Validate', 'Cellule', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Chef de projet', 'Read', 'Pole', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'Chef de projet', 'Validate', 'Pole', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'RP', 'Read', 'Pole', TRUE, NOW(), NULL),
                (gen_random_uuid(), 'RP', 'Validate', 'Pole', TRUE, NOW(), NULL)
            ON CONFLICT ("Role", "Action", "Scope")
            DO UPDATE SET "IsAllowed" = TRUE, "UpdatedAt" = NOW();
            """,
            ct);
    }

    /// <summary>
    /// Synthèse globale : RH puis Comptabilité. Désactive les étapes Manager sans toucher au circuit fiches.
    /// </summary>
    public static async Task EnsureRhThenComptaGlobalPoolWorkflowAsync(
        PrimeDbContext db,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_global_pool_workflow_step", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE prime_global_pool_workflow_step
            SET "IsActive" = FALSE, "UpdatedAt" = NOW()
            WHERE "ApproverRole" = 'Manager';

            INSERT INTO prime_global_pool_workflow_step (
                "Id", "SortOrder", "ApproverRole", "IsRequired", "IsActive", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), 1, 'RH', TRUE, TRUE, NOW(), NULL
            WHERE EXISTS (SELECT 1 FROM prime_global_pool_workflow_step)
              AND NOT EXISTS (
                  SELECT 1 FROM prime_global_pool_workflow_step WHERE "ApproverRole" = 'RH');

            UPDATE prime_global_pool_workflow_step
            SET "SortOrder" = 1, "IsRequired" = TRUE, "IsActive" = TRUE, "UpdatedAt" = NOW()
            WHERE "ApproverRole" = 'RH';

            INSERT INTO prime_global_pool_workflow_step (
                "Id", "SortOrder", "ApproverRole", "IsRequired", "IsActive", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), 2, 'Comptabilité', TRUE, TRUE, NOW(), NULL
            WHERE EXISTS (SELECT 1 FROM prime_global_pool_workflow_step)
              AND NOT EXISTS (
                  SELECT 1 FROM prime_global_pool_workflow_step
                  WHERE "ApproverRole" IN ('Comptabilité', 'Comptable'));

            UPDATE prime_global_pool_workflow_step
            SET "SortOrder" = 2, "IsRequired" = TRUE, "IsActive" = TRUE, "UpdatedAt" = NOW()
            WHERE "ApproverRole" IN ('Comptabilité', 'Comptable');
            """,
            ct);

        if (await TableExistsAsync(db, "prime_rbac_permission", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO prime_rbac_permission (
                    "Id", "Role", "Action", "Scope", "IsAllowed", "CreatedAt", "UpdatedAt")
                VALUES
                    (gen_random_uuid(), 'RH', 'Read', 'Global', TRUE, NOW(), NULL),
                    (gen_random_uuid(), 'Comptabilité', 'Read', 'Global', TRUE, NOW(), NULL),
                    (gen_random_uuid(), 'Comptable', 'Read', 'Global', TRUE, NOW(), NULL),
                    (gen_random_uuid(), 'Admin', 'Read', 'Global', TRUE, NOW(), NULL)
                ON CONFLICT ("Role", "Action", "Scope")
                DO UPDATE SET "IsAllowed" = TRUE, "UpdatedAt" = NOW();
                """,
                ct);
        }
    }

    public static async Task EnsureOutboxTableAsync(PrimeDbContext db, CancellationToken ct = default)
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

    public static async Task EnsureAllowanceTrackSchemaAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE prime_employee ADD COLUMN IF NOT EXISTS "BusinessDepartmentId" character varying(64) NULL;
            ALTER TABLE prime_employee ADD COLUMN IF NOT EXISTS "BusinessDepartmentKind" character varying(32) NULL;

            CREATE TABLE IF NOT EXISTS prime_business_department (
                "Id" character varying(64) NOT NULL PRIMARY KEY,
                "Code" character varying(64) NOT NULL,
                "Name" character varying(256) NOT NULL,
                "Kind" character varying(32) NOT NULL DEFAULT 'Operational',
                "ManagerEmployeeId" character varying(128) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE
            );

            CREATE TABLE IF NOT EXISTS prime_business_department_pole (
                "Id" uuid NOT NULL PRIMARY KEY,
                "BusinessDepartmentId" character varying(64) NOT NULL REFERENCES prime_business_department("Id") ON DELETE CASCADE,
                "PoleId" character varying(64) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_type (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Code" character varying(64) NOT NULL,
                "Label" character varying(256) NOT NULL,
                "Category" character varying(64) NOT NULL,
                "CalculationMode" character varying(32) NOT NULL DEFAULT 'Manual',
                "DefaultAmount" numeric(18,2) NULL,
                "MinAmount" numeric(18,2) NULL,
                "MaxAmount" numeric(18,2) NULL,
                "RequiresJustification" boolean NOT NULL DEFAULT FALSE,
                "ApplicableDepartmentKinds" character varying(64) NOT NULL DEFAULT 'Support',
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_allowance_type_Code" ON prime_allowance_type ("Code");

            CREATE TABLE IF NOT EXISTS prime_allowance_type_department (
                "Id" uuid NOT NULL PRIMARY KEY,
                "AllowanceTypeId" uuid NOT NULL REFERENCES prime_allowance_type("Id") ON DELETE CASCADE,
                "BusinessDepartmentId" character varying(64) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_request (
                "Id" uuid NOT NULL PRIMARY KEY,
                "EmployeeId" character varying(128) NOT NULL,
                "BusinessDepartmentId" character varying(64) NOT NULL,
                "AllowanceTypeId" uuid NOT NULL REFERENCES prime_allowance_type("Id"),
                "Period" character varying(16) NOT NULL,
                "Amount" numeric(18,2) NOT NULL,
                "Currency" character varying(8) NOT NULL DEFAULT 'MAD',
                "Reason" character varying(2048) NOT NULL DEFAULT '',
                "Source" character varying(32) NOT NULL DEFAULT 'Manual',
                "Status" character varying(32) NOT NULL DEFAULT 'Draft',
                "CreatedByUserId" character varying(128) NOT NULL,
                "RejectionReason" text NULL,
                "ManagerApprovedByUserId" character varying(128) NULL,
                "ManagerApprovedAt" timestamp with time zone NULL,
                "RhApprovedByUserId" character varying(128) NULL,
                "RhApprovedAt" timestamp with time zone NULL,
                "ComptaApprovedByUserId" character varying(128) NULL,
                "ComptaApprovedAt" timestamp with time zone NULL,
                "PaidAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_request_history (
                "Id" uuid NOT NULL PRIMARY KEY,
                "AllowanceRequestId" uuid NOT NULL REFERENCES prime_allowance_request("Id") ON DELETE CASCADE,
                "Action" character varying(32) NOT NULL,
                "FromStatus" character varying(32) NOT NULL,
                "ToStatus" character varying(32) NOT NULL,
                "ActorUserId" character varying(128) NOT NULL,
                "ActorRole" character varying(64) NOT NULL,
                "Comment" text NULL,
                "At" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_workflow_step (
                "Id" uuid NOT NULL PRIMARY KEY,
                "SortOrder" integer NOT NULL,
                "ApproverRole" character varying(64) NOT NULL,
                "IsRequired" boolean NOT NULL DEFAULT TRUE,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_rule (
                "Id" uuid NOT NULL PRIMARY KEY,
                "AllowanceTypeId" uuid NOT NULL REFERENCES prime_allowance_type("Id"),
                "BusinessDepartmentId" character varying(64) NOT NULL,
                "ConditionJson" text NOT NULL DEFAULT '{{}}',
                "FormulaJson" text NOT NULL DEFAULT '{{}}',
                "DataSource" character varying(64) NOT NULL DEFAULT 'Manual',
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS prime_allowance_no_bonus_marker (
                "Id" uuid NOT NULL PRIMARY KEY,
                "EmployeeId" character varying(128) NOT NULL,
                "BusinessDepartmentId" character varying(64) NOT NULL,
                "Period" character varying(16) NOT NULL,
                "MarkedByUserId" character varying(128) NOT NULL,
                "Comment" character varying(512) NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_allowance_no_bonus_marker_EmployeeId_Period"
                ON prime_allowance_no_bonus_marker ("EmployeeId", "Period");
            """,
            ct);
    }

    /// <summary>
    /// Pondérations RACC/SAV par service (migration 20260810140000).
    /// </summary>
    public static async Task EnsureServicePoleLinePonderationTableAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_service", ct))
            return;
        if (await TableExistsAsync(db, "prime_service_pole_line_ponderation", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS prime_service_pole_line_ponderation (
                "Id" uuid NOT NULL,
                "ServiceId" character varying(128) NOT NULL,
                "TemplateStableId" character varying(256) NOT NULL,
                "Label" character varying(512) NOT NULL,
                "SortOrder" integer NOT NULL,
                "PonderationPrimePct" numeric(9,4) NULL,
                "PonderationChallengePct" numeric(9,4) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_prime_service_pole_line_ponderation" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_prime_service_pole_line_ponderation_prime_service_ServiceId"
                    FOREIGN KEY ("ServiceId") REFERENCES prime_service ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_prime_service_pole_line_ponderation_ServiceId"
                ON prime_service_pole_line_ponderation ("ServiceId");
            CREATE INDEX IF NOT EXISTS "IX_prime_service_pole_line_ponderation_ServiceId_SortOrder"
                ON prime_service_pole_line_ponderation ("ServiceId", "SortOrder");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_service_pole_line_ponderation_ServiceId_TemplateStableId"
                ON prime_service_pole_line_ponderation ("ServiceId", "TemplateStableId");
            """,
            ct);
    }

    /// <summary>
    /// Pondérations partie commune versionnées cellule/service + copie des anciennes lignes par service.
    /// </summary>
    public static async Task EnsureCommonLinePonderationTableAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_service", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS prime_common_line_ponderation (
                "Id" uuid NOT NULL,
                "ScopeType" character varying(16) NOT NULL,
                "ScopeId" character varying(128) NOT NULL,
                "TemplateId" character varying(128) NOT NULL,
                "TemplateStableId" character varying(256) NOT NULL,
                "Label" character varying(512) NOT NULL,
                "Contract" character varying(32) NOT NULL DEFAULT '',
                "SortOrder" integer NOT NULL,
                "PonderationPrimePct" numeric(9,4) NULL,
                "PonderationChallengePct" numeric(9,4) NULL,
                "EffectiveFrom" timestamp with time zone NOT NULL,
                "EffectiveTo" timestamp with time zone NULL,
                "CreatedBy" character varying(128) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_prime_common_line_ponderation" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_prime_common_line_ponderation_scope_template_from"
                ON prime_common_line_ponderation ("ScopeType", "ScopeId", "TemplateId", "TemplateStableId", "EffectiveFrom");
            CREATE INDEX IF NOT EXISTS "IX_prime_common_line_ponderation_scope_stable_to"
                ON prime_common_line_ponderation ("ScopeType", "ScopeId", "TemplateStableId", "EffectiveTo");
            """,
            ct);

        if (!await TableExistsAsync(db, "prime_service_pole_line_ponderation", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO prime_common_line_ponderation (
                "Id", "ScopeType", "ScopeId", "TemplateId", "TemplateStableId",
                "Label", "Contract", "SortOrder",
                "PonderationPrimePct", "PonderationChallengePct",
                "EffectiveFrom", "EffectiveTo", "CreatedBy", "CreatedAt")
            SELECT
                gen_random_uuid(),
                'Service',
                p."ServiceId",
                '',
                p."TemplateStableId",
                COALESCE(p."Label", ''),
                '',
                p."SortOrder",
                p."PonderationPrimePct",
                p."PonderationChallengePct",
                TIMESTAMPTZ '2000-01-01 00:00:00+00',
                NULL,
                NULL,
                COALESCE(p."CreatedAt", NOW())
            FROM prime_service_pole_line_ponderation p
            WHERE NOT EXISTS (
                SELECT 1 FROM prime_common_line_ponderation c
                WHERE c."ScopeType" = 'Service'
                  AND c."ScopeId" = p."ServiceId"
                  AND c."TemplateStableId" = p."TemplateStableId"
                  AND c."EffectiveTo" IS NULL);
            """,
            ct);
    }

    /// <summary>Index lecture fréquents (pondérations + fiches période/statut).</summary>
    public static async Task EnsurePerfIndexesAsync(PrimeDbContext db, CancellationToken ct = default)
    {
        if (await TableExistsAsync(db, "prime_common_line_ponderation", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_prime_common_line_ponderation_scope_template_active"
                  ON prime_common_line_ponderation ("ScopeType", "ScopeId", "TemplateId", "EffectiveFrom")
                  WHERE "EffectiveTo" IS NULL;
                CREATE INDEX IF NOT EXISTS "IX_prime_common_line_ponderation_scope_from_to"
                  ON prime_common_line_ponderation ("ScopeType", "ScopeId", "EffectiveFrom", "EffectiveTo");
                """,
                ct);
        }

        if (await TableExistsAsync(db, "prime_employee_prime_service_fiche", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_prime_employee_prime_service_fiche_Period_ValidationStatus"
                  ON prime_employee_prime_service_fiche ("Period", "ValidationStatus");
                """,
                ct);
        }
    }

    /// <summary>Overrides pondération indicateur par agent (partie service).</summary>
    public static async Task EnsureEmployeeServiceIndicatorPonderationTableAsync(
        PrimeDbContext db,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "prime_service_prime_indicator", ct))
            return;
        if (await TableExistsAsync(db, "prime_employee_service_indicator_ponderation", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS prime_employee_service_indicator_ponderation (
                "Id" uuid NOT NULL,
                "EmployeeId" character varying(128) NOT NULL,
                "ServicePrimeIndicatorId" uuid NOT NULL,
                "PonderationPrimePct" numeric(9,4) NULL,
                "PonderationChallengePct" numeric(9,4) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "PK_prime_employee_service_indicator_ponderation" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_prime_employee_service_indicator_ponderation_indicator"
                    FOREIGN KEY ("ServicePrimeIndicatorId")
                    REFERENCES prime_service_prime_indicator ("Id")
                    ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_prime_employee_service_indicator_ponderation_employee_ind"
                ON prime_employee_service_indicator_ponderation ("EmployeeId", "ServicePrimeIndicatorId");
            CREATE INDEX IF NOT EXISTS "IX_prime_employee_service_indicator_ponderation_indicator"
                ON prime_employee_service_indicator_ponderation ("ServicePrimeIndicatorId");
            """,
            ct);
    }
}
