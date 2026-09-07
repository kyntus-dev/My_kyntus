using Microsoft.EntityFrameworkCore;

namespace Planning.Infrastructure.Persistence;

public static class PlanningSchemaPatches
{
    public static async Task EnsureOutboxTableAsync(AppDbContext db, CancellationToken ct = default)
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

    public static async Task EnsurePlanningNotificationsTableAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PlanningNotifications" (
                "Id" serial PRIMARY KEY,
                "UserId" integer NOT NULL,
                "AuthUserId" integer NOT NULL,
                "WeeklyPlanningId" integer NULL,
                "WeekCode" character varying(64) NOT NULL DEFAULT '',
                "SubServiceName" character varying(200) NOT NULL DEFAULT '',
                "Message" text NOT NULL,
                "IsRead" boolean NOT NULL DEFAULT false,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "ReadAt" timestamp with time zone NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PlanningNotifications_AuthUserId" ON "PlanningNotifications" ("AuthUserId");
            ALTER TABLE "PlanningNotifications" ALTER COLUMN "WeekCode" TYPE character varying(64);
            """,
            ct);
    }

    public static async Task EnsureUserHrProfilesTableAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS user_hr_profiles (
                "UserId" integer NOT NULL PRIMARY KEY,
                "ChefDeProjetId" uuid NULL,
                "SuperviseurId" uuid NULL,
                "ReferentTechniqueId" uuid NULL,
                "DateNaissance" date NULL,
                "VilleNaissance" character varying(200) NULL,
                "Nationalite" character varying(100) NULL,
                "Sexe" character varying(20) NULL,
                "SituationFamiliale" character varying(50) NULL,
                "NombreEnfants" integer NULL,
                "Cin" character varying(50) NULL,
                "Adresse" text NULL,
                "Telephone1" character varying(50) NULL,
                "TelephoneUrgence" character varying(50) NULL,
                "RelationUrgence" character varying(100) NULL,
                "Rib" character varying(34) NULL,
                "ImmatriculationInterne" character varying(50) NULL,
                "ImmatriculationCnss" character varying(50) NULL,
                "DateEntree" date NULL,
                "DateEmbauche" date NULL,
                "DateAnciennete" date NULL,
                "DateSortie" date NULL,
                "DateEvolutionPoste" date NULL,
                "AncienPoste" character varying(200) NULL,
                "AncienService" character varying(200) NULL,
                "NiveauScolaire" character varying(100) NULL,
                "IntitulesEtudes" text NULL,
                "EnFormation" boolean NOT NULL DEFAULT false,
                "DateDebutFormation" date NULL,
                "DateFinFormationPrevue" date NULL,
                "NiveauExpertiseMetier" integer NULL,
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
            );
            """,
            ct);
    }

    public static async Task EnsureDateDebutFormationColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE user_hr_profiles ADD COLUMN IF NOT EXISTS "DateDebutFormation" date NULL;
            """,
            ct);
    }

    public static async Task EnsureNumeroCarteAutoentrepreneurColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE user_hr_profiles ADD COLUMN IF NOT EXISTS "NumeroCarteAutoentrepreneur" character varying(64) NULL;
            """,
            ct);
    }

    public static async Task EnsureEmailPersonnelColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE user_hr_profiles ADD COLUMN IF NOT EXISTS "EmailPersonnel" character varying(256) NULL;
            """,
            ct);
    }

    public static async Task EnsureCongeSourceDemandeIdColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Conges" ADD COLUMN IF NOT EXISTS "SourceDemandeId" uuid NULL;
            CREATE INDEX IF NOT EXISTS "IX_Conges_SourceDemandeId" ON "Conges" ("SourceDemandeId");
            """,
            ct);
    }

    /// <summary>
    /// Template shifts + consultations + auto-generate settings (idempotent).
    /// </summary>
    public static async Task EnsureShiftTemplateAndValidationSchemaAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SubServiceShiftConfigs" ADD COLUMN IF NOT EXISTS "IsTemplate" boolean NOT NULL DEFAULT false;

            ALTER TABLE "SubServiceShiftConfigs" ALTER COLUMN "WeekCode" DROP NOT NULL;
            ALTER TABLE "SubServiceShiftConfigs" ALTER COLUMN "WeekStartDate" DROP NOT NULL;

            -- Clone latest week configs into permanent templates (keep original rows as snapshots)
            INSERT INTO "SubServiceShiftConfigs" (
                "SubServiceId", "WeekCode", "WeekStartDate", "IsTemplate",
                "Label", "StartTime", "WorkHours",
                "BreakRangeStart", "BreakRangeEnd", "BreakDurationMinutes",
                "RequiredCount", "Percentage", "MinPresencePercent", "DisplayOrder", "CreatedAt"
            )
            SELECT
                c."SubServiceId", NULL, NULL, true,
                c."Label", c."StartTime", c."WorkHours",
                c."BreakRangeStart", c."BreakRangeEnd", c."BreakDurationMinutes",
                c."RequiredCount", c."Percentage", c."MinPresencePercent", c."DisplayOrder", now()
            FROM "SubServiceShiftConfigs" c
            INNER JOIN (
                SELECT "SubServiceId", MAX("WeekStartDate") AS max_start
                FROM "SubServiceShiftConfigs"
                WHERE "WeekCode" IS NOT NULL
                GROUP BY "SubServiceId"
            ) latest ON latest."SubServiceId" = c."SubServiceId" AND latest.max_start = c."WeekStartDate"
            WHERE COALESCE(c."IsTemplate", false) = false
              AND NOT EXISTS (
                SELECT 1 FROM "SubServiceShiftConfigs" t
                WHERE t."SubServiceId" = c."SubServiceId" AND t."IsTemplate" = true
              );
            DROP INDEX IF EXISTS "IX_SubServiceShiftConfigs_SubServiceId_WeekCode_Label";

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Template_SubServiceId_Label"
              ON "SubServiceShiftConfigs" ("SubServiceId", "Label")
              WHERE "IsTemplate" = TRUE;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Snapshot_SubServiceId_WeekCode_Label"
              ON "SubServiceShiftConfigs" ("SubServiceId", "WeekCode", "Label")
              WHERE "IsTemplate" = FALSE AND "WeekCode" IS NOT NULL;

            CREATE TABLE IF NOT EXISTS "PlanningConsultations" (
                "Id" serial PRIMARY KEY,
                "PlanningId" integer NOT NULL,
                "UserId" integer NOT NULL,
                "ConsultedAt" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "FK_PlanningConsultations_WeeklyPlannings_PlanningId"
                    FOREIGN KEY ("PlanningId") REFERENCES "WeeklyPlannings" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_PlanningConsultations_Users_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanningConsultations_PlanningId_UserId"
              ON "PlanningConsultations" ("PlanningId", "UserId");

            CREATE TABLE IF NOT EXISTS "PlanningAutoGenerateSettings" (
                "Id" character varying(32) NOT NULL PRIMARY KEY,
                "Enabled" boolean NOT NULL DEFAULT true,
                "DayOfWeek" integer NOT NULL DEFAULT 4,
                "HourLocal" integer NOT NULL DEFAULT 6,
                "MinuteLocal" integer NOT NULL DEFAULT 0,
                "TimeZone" character varying(64) NOT NULL DEFAULT 'Africa/Casablanca',
                "Target" character varying(32) NOT NULL DEFAULT 'NextWeek',
                "LastRunAt" timestamp with time zone NULL,
                "LastRunWeekCode" character varying(16) NULL,
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedByUserId" integer NULL
            );

            INSERT INTO "PlanningAutoGenerateSettings" ("Id", "Enabled", "DayOfWeek", "HourLocal", "MinuteLocal", "TimeZone", "Target", "UpdatedAt")
            VALUES ('default', true, 4, 6, 0, 'Africa/Casablanca', 'NextWeek', now())
            ON CONFLICT ("Id") DO NOTHING;
            """,
            ct);
    }

    /// <summary>Colonne ShiftKind sur SubServiceShiftConfigs (idempotent).</summary>
    public static async Task EnsureShiftKindColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SubServiceShiftConfigs" ADD COLUMN IF NOT EXISTS "ShiftKind" integer NOT NULL DEFAULT 0;
            """,
            ct);
    }

    /// <summary>BreakSlotsJson + IsCriticalCell sur SubServiceShiftConfigs (idempotent).</summary>
    public static async Task EnsureBreakSlotsAndCriticalCellAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SubServiceShiftConfigs" ADD COLUMN IF NOT EXISTS "BreakSlotsJson" character varying(128) NULL;
            ALTER TABLE "SubServiceShiftConfigs" ADD COLUMN IF NOT EXISTS "IsCriticalCell" boolean NOT NULL DEFAULT false;
            """,
            ct);
    }

    /// <summary>Table PlanningChangeRequests (idempotent) + colonnes workflow switch.</summary>
    public static async Task EnsurePlanningChangeRequestsTableAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PlanningChangeRequests" (
                "Id" serial PRIMARY KEY,
                "WeekCode" character varying(16) NOT NULL,
                "RequesterUserId" integer NOT NULL,
                "CurrentAssignmentId" integer NOT NULL,
                "Reason" character varying(1000) NOT NULL,
                "ProposedSwapUserId" integer NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "PartnerRespondedAt" timestamp with time zone NULL,
                "SupervisorProcessedByUserId" integer NULL,
                "ProcessedByUserId" integer NULL,
                "ProcessedAt" timestamp with time zone NULL,
                "RejectionReason" character varying(1000) NULL,
                CONSTRAINT "FK_PlanningChangeRequests_Users_Requester"
                    FOREIGN KEY ("RequesterUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningChangeRequests_ShiftAssignments"
                    FOREIGN KEY ("CurrentAssignmentId") REFERENCES "ShiftAssignments" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_PlanningChangeRequests_Users_Proposed"
                    FOREIGN KEY ("ProposedSwapUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_PlanningChangeRequests_Users_Processed"
                    FOREIGN KEY ("ProcessedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PlanningChangeRequests_Requester_WeekCode"
              ON "PlanningChangeRequests" ("RequesterUserId", "WeekCode");
            CREATE INDEX IF NOT EXISTS "IX_PlanningChangeRequests_Status_WeekCode"
              ON "PlanningChangeRequests" ("Status", "WeekCode");

            ALTER TABLE "PlanningChangeRequests"
              ADD COLUMN IF NOT EXISTS "PartnerRespondedAt" timestamp with time zone NULL;
            ALTER TABLE "PlanningChangeRequests"
              ADD COLUMN IF NOT EXISTS "SupervisorProcessedByUserId" integer NULL;
            ALTER TABLE "PlanningChangeRequests"
              ADD COLUMN IF NOT EXISTS "ProcessedByUserId" integer NULL;
            ALTER TABLE "PlanningChangeRequests"
              ADD COLUMN IF NOT EXISTS "ProcessedAt" timestamp with time zone NULL;
            """,
            ct);
    }

    public static async Task EnsureEmployeeImportSourceFileColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "EmployeeImportSessions"
              ADD COLUMN IF NOT EXISTS "FileContent" bytea NULL;
            ALTER TABLE "EmployeeImportSessions"
              ADD COLUMN IF NOT EXISTS "ContentType" character varying(128) NULL;
            ALTER TABLE "EmployeeImportJobs"
              ADD COLUMN IF NOT EXISTS "FileContent" bytea NULL;
            ALTER TABLE "EmployeeImportJobs"
              ADD COLUMN IF NOT EXISTS "ContentType" character varying(128) NULL;
            ALTER TABLE "EmployeeImportJobs"
              ADD COLUMN IF NOT EXISTS "ProcessedLignes" integer NOT NULL DEFAULT 0;
            ALTER TABLE "EmployeeImportJobs"
              ADD COLUMN IF NOT EXISTS "Status" character varying(32) NOT NULL DEFAULT 'Completed';
            ALTER TABLE "EmployeeImportJobs"
              ADD COLUMN IF NOT EXISTS "ErrorMessage" text NULL;
            """,
            ct);
    }

    public static async Task EnsureUserHtelColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Colonnes d'abord, seules : un échec d'index unique ne doit pas rollback l'ALTER
        // (PostgreSQL exécute un batch multi-statements dans une seule transaction).
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IdTechnicien" integer NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "HtelCode" character varying(128) NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "HtelTechnicienName" character varying(256) NULL;
            """,
            ct);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_IdTechnicien"
                    ON "Users" ("IdTechnicien")
                    WHERE "IdTechnicien" IS NOT NULL;
                """,
                ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Index unique Users.IdTechnicien ignoré: {ex.Message}");
        }
    }

    /// <summary>Colonne Users.SaturdayWorkMode (null = défaut Niveau).</summary>
    public static async Task EnsureUserSaturdayWorkModeColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SaturdayWorkMode" integer NULL;
            """,
            ct);
    }

    /// <summary>Colonnes cas particulier (exclusion pauses extrêmes +3h/+5h).</summary>
    public static async Task EnsureUserSpecialCaseColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsSpecialCase" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SpecialCaseDescription" character varying(500) NULL;
            """,
            ct);
    }

    /// <summary>Ticket formation plateau (jamais Opening/Closing).</summary>
    public static async Task EnsureUserPlateauTrainingColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsPlateauTraining" boolean NOT NULL DEFAULT false;
            """,
            ct);
    }

    /// <summary>Table PlanningExceptionalRequests (idempotent).</summary>
    public static async Task EnsurePlanningExceptionalRequestsTableAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PlanningExceptionalRequests" (
                "Id" serial PRIMARY KEY,
                "WeekCode" character varying(16) NOT NULL,
                "RequestedDate" date NOT NULL,
                "RequesterUserId" integer NOT NULL,
                "SubServiceId" integer NOT NULL,
                "RequestedShiftTemplateId" integer NOT NULL,
                "Reason" character varying(1000) NOT NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "JustificationRequired" boolean NOT NULL DEFAULT false,
                "JustificationFileName" character varying(260) NULL,
                "JustificationContentType" character varying(128) NULL,
                "JustificationContent" bytea NULL,
                "SupervisorProcessedByUserId" integer NULL,
                "SupervisorProcessedAt" timestamp with time zone NULL,
                "RhProcessedByUserId" integer NULL,
                "RhProcessedAt" timestamp with time zone NULL,
                "ProcessedByUserId" integer NULL,
                "ProcessedAt" timestamp with time zone NULL,
                "RejectionReason" character varying(1000) NULL,
                CONSTRAINT "FK_PlanningExceptionalRequests_Users_Requester"
                    FOREIGN KEY ("RequesterUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningExceptionalRequests_SubServices"
                    FOREIGN KEY ("SubServiceId") REFERENCES "SubServices" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningExceptionalRequests_ShiftTemplate"
                    FOREIGN KEY ("RequestedShiftTemplateId") REFERENCES "SubServiceShiftConfigs" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningExceptionalRequests_Users_Supervisor"
                    FOREIGN KEY ("SupervisorProcessedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_PlanningExceptionalRequests_Users_Rh"
                    FOREIGN KEY ("RhProcessedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_PlanningExceptionalRequests_Users_Processed"
                    FOREIGN KEY ("ProcessedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PlanningExceptionalRequests_Week_Sub_Status"
              ON "PlanningExceptionalRequests" ("WeekCode", "SubServiceId", "Status");
            CREATE INDEX IF NOT EXISTS "IX_PlanningExceptionalRequests_Requester_Created"
              ON "PlanningExceptionalRequests" ("RequesterUserId", "CreatedAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanningExceptionalRequests_Active_User_Date"
              ON "PlanningExceptionalRequests" ("RequesterUserId", "RequestedDate")
              WHERE "Status" IN (0, 1, 2);

            ALTER TABLE "PlanningExceptionalRequests"
              ADD COLUMN IF NOT EXISTS "ProcessedByUserId" integer NULL;
            ALTER TABLE "PlanningExceptionalRequests"
              ADD COLUMN IF NOT EXISTS "ProcessedAt" timestamp with time zone NULL;
            """,
            ct);

        // Séparé : si le changement de FK échoue, le DROP NOT NULL doit quand même rester.
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PlanningExceptionalRequests"
              ALTER COLUMN "RequestedShiftTemplateId" DROP NOT NULL;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'FK_PlanningExceptionalRequests_ShiftTemplate'
              ) THEN
                ALTER TABLE "PlanningExceptionalRequests"
                  DROP CONSTRAINT "FK_PlanningExceptionalRequests_ShiftTemplate";
              END IF;
              IF EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'FK_PlanningExceptionalRequests_SubServiceShiftConfigs_RequestedShiftTemplateId'
              ) THEN
                ALTER TABLE "PlanningExceptionalRequests"
                  DROP CONSTRAINT "FK_PlanningExceptionalRequests_SubServiceShiftConfigs_RequestedShiftTemplateId";
              END IF;
            END $$;
            """,
            ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$ BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'FK_PlanningExceptionalRequests_ShiftTemplate'
              ) THEN
                ALTER TABLE "PlanningExceptionalRequests"
                  ADD CONSTRAINT "FK_PlanningExceptionalRequests_ShiftTemplate"
                  FOREIGN KEY ("RequestedShiftTemplateId") REFERENCES "SubServiceShiftConfigs" ("Id")
                  ON DELETE SET NULL;
              END IF;
            END $$;
            """,
            ct);
    }

    /// <summary>Colonne ShiftAssignments.IsExceptionalRequest (tag DE sur la grille).</summary>
    public static async Task EnsureShiftAssignmentExceptionalFlagAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ShiftAssignments"
              ADD COLUMN IF NOT EXISTS "IsExceptionalRequest" boolean NOT NULL DEFAULT false;
            """,
            ct);
    }

    /// <summary>Tables renfort samedi + flag IsReinforcement (idempotent).</summary>
    public static async Task EnsurePlanningReinforcementRequestsTableAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PlanningReinforcementRequests" (
                "Id" serial PRIMARY KEY,
                "WeekCode" character varying(16) NOT NULL,
                "SaturdayDate" date NOT NULL,
                "SubServiceId" integer NOT NULL,
                "SlotsNeeded" integer NOT NULL DEFAULT 1,
                "Reason" character varying(1000) NOT NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "CreatedByUserId" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                "ClosedAt" timestamp with time zone NULL,
                "WeeklyPlanningId" integer NULL,
                CONSTRAINT "FK_PlanningReinforcementRequests_SubServices"
                  FOREIGN KEY ("SubServiceId") REFERENCES "SubServices" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningReinforcementRequests_Users_CreatedBy"
                  FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningReinforcementRequests_WeeklyPlannings"
                  FOREIGN KEY ("WeeklyPlanningId") REFERENCES "WeeklyPlannings" ("Id") ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_PlanningReinforcementRequests_Sub_Sat_Status"
              ON "PlanningReinforcementRequests" ("SubServiceId", "SaturdayDate", "Status");
            CREATE INDEX IF NOT EXISTS "IX_PlanningReinforcementRequests_Week_Sub"
              ON "PlanningReinforcementRequests" ("WeekCode", "SubServiceId");

            CREATE TABLE IF NOT EXISTS "PlanningReinforcementVolunteers" (
                "Id" serial PRIMARY KEY,
                "RequestId" integer NOT NULL,
                "UserId" integer NOT NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "RespondedAt" timestamp with time zone NULL,
                "SelectedAt" timestamp with time zone NULL,
                "SelectedShiftConfigId" integer NULL,
                CONSTRAINT "FK_PlanningReinforcementVolunteers_Request"
                  FOREIGN KEY ("RequestId") REFERENCES "PlanningReinforcementRequests" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_PlanningReinforcementVolunteers_Users"
                  FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_PlanningReinforcementVolunteers_ShiftConfig"
                  FOREIGN KEY ("SelectedShiftConfigId") REFERENCES "SubServiceShiftConfigs" ("Id") ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanningReinforcementVolunteers_Request_User"
              ON "PlanningReinforcementVolunteers" ("RequestId", "UserId");

            ALTER TABLE "ShiftAssignments"
              ADD COLUMN IF NOT EXISTS "IsReinforcement" boolean NOT NULL DEFAULT false;

            ALTER TABLE "PlanningReinforcementRequests"
              ADD COLUMN IF NOT EXISTS "SelectedByUserId" integer NULL;
            ALTER TABLE "PlanningReinforcementRequests"
              ADD COLUMN IF NOT EXISTS "ClosedByUserId" integer NULL;
            ALTER TABLE "PlanningReinforcementRequests"
              ADD COLUMN IF NOT EXISTS "CancelledByUserId" integer NULL;
            """,
            ct);
    }

    /// <summary>Colonnes anti-doublon rappels demandes pending (idempotent).</summary>
    public static async Task EnsurePendingRequestReminderColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "LastPendingJ1ReminderDate" date NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "LastValidationReminderWeekCode" character varying(16) NULL;
            """,
            ct);
    }

    /// <summary>Email expéditeur admin (From + nom affiché) et connexion SMTP admin.</summary>
    public static async Task EnsurePlanningSenderFromColumnsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SenderFromEmail" character varying(256) NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SenderDisplayName" character varying(128) NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SmtpHost" character varying(256) NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SmtpPort" integer NOT NULL DEFAULT 587;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SmtpUser" character varying(256) NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SmtpPassword" character varying(512) NULL;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "SmtpEnableSsl" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "MailSendMode" character varying(16) NOT NULL DEFAULT 'Smtp';
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "MailApiProvider" character varying(32) NOT NULL DEFAULT 'Brevo';
            ALTER TABLE "PlanningAutoGenerateSettings"
              ADD COLUMN IF NOT EXISTS "MailApiKey" character varying(512) NULL;
            """,
            ct);
    }

    /// <summary>
    /// Multi-modes de shifts : profils, flag cellule, FK mode, matrices hebdomadaires.
    /// Idempotent. Convertit le template existant en « Mode par défaut » si multi-mode activé plus tard.
    /// </summary>
    public static async Task EnsureShiftModeProfilesSchemaAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SubServices"
              ADD COLUMN IF NOT EXISTS "MultiShiftModesEnabled" boolean NOT NULL DEFAULT false;

            CREATE TABLE IF NOT EXISTS "ShiftModeProfiles" (
                "Id" serial PRIMARY KEY,
                "SubServiceId" integer NOT NULL,
                "Title" character varying(100) NOT NULL,
                "DisplayOrder" integer NOT NULL DEFAULT 0,
                "IsDefault" boolean NOT NULL DEFAULT false,
                "IsActive" boolean NOT NULL DEFAULT true,
                "MinPresencePercent" integer NOT NULL DEFAULT 70,
                "IsCriticalCell" boolean NOT NULL DEFAULT false,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NULL,
                "ArchivedAt" timestamp with time zone NULL,
                CONSTRAINT "FK_ShiftModeProfiles_SubServices"
                    FOREIGN KEY ("SubServiceId") REFERENCES "SubServices" ("Id") ON DELETE CASCADE
            );
            ALTER TABLE "ShiftModeProfiles"
              ADD COLUMN IF NOT EXISTS "IsCriticalCell" boolean NOT NULL DEFAULT false;
            CREATE INDEX IF NOT EXISTS "IX_ShiftModeProfiles_SubServiceId_Title"
              ON "ShiftModeProfiles" ("SubServiceId", "Title");

            ALTER TABLE "SubServiceShiftConfigs"
              ADD COLUMN IF NOT EXISTS "ShiftModeProfileId" integer NULL;
            DO $$ BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_SubServiceShiftConfigs_ShiftModeProfiles'
              ) THEN
                ALTER TABLE "SubServiceShiftConfigs"
                  ADD CONSTRAINT "FK_SubServiceShiftConfigs_ShiftModeProfiles"
                  FOREIGN KEY ("ShiftModeProfileId") REFERENCES "ShiftModeProfiles" ("Id")
                  ON DELETE SET NULL;
              END IF;
            END $$;

            DROP INDEX IF EXISTS "IX_SubServiceShiftConfigs_Template_SubServiceId_Label";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Template_SubService_Mode_Label"
              ON "SubServiceShiftConfigs" ("SubServiceId", "ShiftModeProfileId", "Label")
              WHERE "IsTemplate" = TRUE;

            DROP INDEX IF EXISTS "IX_SubServiceShiftConfigs_Snapshot_SubServiceId_WeekCode_Label";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Snapshot_SubService_Week_Mode_Label"
              ON "SubServiceShiftConfigs" ("SubServiceId", "WeekCode", "ShiftModeProfileId", "Label")
              WHERE "IsTemplate" = FALSE AND "WeekCode" IS NOT NULL;

            CREATE TABLE IF NOT EXISTS "WeeklyCellShiftModePlans" (
                "Id" serial PRIMARY KEY,
                "SubServiceId" integer NOT NULL,
                "WeekCode" character varying(16) NOT NULL,
                "WeekStartDate" date NOT NULL,
                "IsValidated" boolean NOT NULL DEFAULT false,
                "ValidatedAt" timestamp with time zone NULL,
                "ValidatedByUserId" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NULL,
                CONSTRAINT "FK_WeeklyCellShiftModePlans_SubServices"
                    FOREIGN KEY ("SubServiceId") REFERENCES "SubServices" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_WeeklyCellShiftModePlans_Users"
                    FOREIGN KEY ("ValidatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_WeeklyCellShiftModePlans_SubService_Week"
              ON "WeeklyCellShiftModePlans" ("SubServiceId", "WeekCode");

            CREATE TABLE IF NOT EXISTS "WeeklyEmployeeShiftModes" (
                "Id" serial PRIMARY KEY,
                "WeeklyCellShiftModePlanId" integer NOT NULL,
                "UserId" integer NOT NULL,
                "ShiftModeProfileId" integer NOT NULL,
                CONSTRAINT "FK_WeeklyEmployeeShiftModes_Plan"
                    FOREIGN KEY ("WeeklyCellShiftModePlanId") REFERENCES "WeeklyCellShiftModePlans" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_WeeklyEmployeeShiftModes_Users"
                    FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_WeeklyEmployeeShiftModes_Profiles"
                    FOREIGN KEY ("ShiftModeProfileId") REFERENCES "ShiftModeProfiles" ("Id") ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_WeeklyEmployeeShiftModes_Plan_User"
              ON "WeeklyEmployeeShiftModes" ("WeeklyCellShiftModePlanId", "UserId");

            ALTER TABLE "ShiftAssignments"
              ADD COLUMN IF NOT EXISTS "ShiftModeProfileId" integer NULL;
            ALTER TABLE "ShiftAssignments"
              ADD COLUMN IF NOT EXISTS "IsModeOverride" boolean NOT NULL DEFAULT false;
            DO $$ BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_ShiftAssignments_ShiftModeProfiles'
              ) THEN
                ALTER TABLE "ShiftAssignments"
                  ADD CONSTRAINT "FK_ShiftAssignments_ShiftModeProfiles"
                  FOREIGN KEY ("ShiftModeProfileId") REFERENCES "ShiftModeProfiles" ("Id")
                  ON DELETE SET NULL;
              END IF;
            END $$;
            """,
            ct);
    }

    public static async Task EnsureMediaAndTicketCommentsAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MediaAssets" (
                "Id" serial PRIMARY KEY,
                "OwnerType" integer NOT NULL DEFAULT 0,
                "OwnerId" integer NULL,
                "Kind" integer NOT NULL DEFAULT 0,
                "FileName" character varying(260) NOT NULL,
                "ContentType" character varying(128) NOT NULL DEFAULT 'application/octet-stream',
                "StoragePath" character varying(512) NOT NULL,
                "SizeBytes" bigint NOT NULL DEFAULT 0,
                "SortOrder" integer NOT NULL DEFAULT 0,
                "UploadedByUserId" character varying(128) NOT NULL DEFAULT '',
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS "IX_MediaAssets_OwnerType_OwnerId"
              ON "MediaAssets" ("OwnerType", "OwnerId");

            CREATE TABLE IF NOT EXISTS "TicketComments" (
                "Id" serial PRIMARY KEY,
                "OwnerType" integer NOT NULL,
                "OwnerId" integer NOT NULL,
                "AuthorId" character varying(128) NOT NULL,
                "AuthorNom" character varying(200) NOT NULL DEFAULT '',
                "Text" character varying(4000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS "IX_TicketComments_OwnerType_OwnerId"
              ON "TicketComments" ("OwnerType", "OwnerId");

            ALTER TABLE "NewsletterCampaigns"
              ADD COLUMN IF NOT EXISTS "RecipientUserIdsJson" text NULL;
            """,
            ct);
    }

    /// <summary>Index lecture fréquents (grille, demandes exceptionnelles, users actifs).</summary>
    public static async Task EnsurePerfIndexesAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ShiftAssignments_WeeklyPlanningId_AssignedDate"
              ON "ShiftAssignments" ("WeeklyPlanningId", "AssignedDate");
            CREATE INDEX IF NOT EXISTS "IX_PlanningExceptionalRequests_Status"
              ON "PlanningExceptionalRequests" ("Status");
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = 'Users'
              ) AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'Users' AND column_name = 'IsActive'
              ) THEN
                CREATE INDEX IF NOT EXISTS "IX_Users_IsActive"
                  ON "Users" ("IsActive");
              END IF;
            END $$;
            """,
            ct);
    }

    /// <summary>
    /// Shifts samedi optionnels : même Label autorisé weekday + samedi
    /// (index unique étendu avec AppliesToSaturday).
    /// </summary>
    public static async Task EnsureSaturdayShiftConfigColumnAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Colonne d'abord, seule : un échec d'index ne doit pas rollback l'ALTER
        // (PostgreSQL exécute un batch multi-statements dans une seule transaction).
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SubServiceShiftConfigs"
              ADD COLUMN IF NOT EXISTS "AppliesToSaturday" boolean NOT NULL DEFAULT false;
            """,
            ct);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                DROP INDEX IF EXISTS "IX_SubServiceShiftConfigs_Template_SubService_Mode_Label";
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Template_SubService_Mode_Sat_Label"
                  ON "SubServiceShiftConfigs" ("SubServiceId", "ShiftModeProfileId", "AppliesToSaturday", "Label")
                  WHERE "IsTemplate" = TRUE;

                DROP INDEX IF EXISTS "IX_SubServiceShiftConfigs_Snapshot_SubService_Week_Mode_Label";
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubServiceShiftConfigs_Snapshot_SubService_Week_Mode_Sat_Label"
                  ON "SubServiceShiftConfigs" ("SubServiceId", "WeekCode", "ShiftModeProfileId", "AppliesToSaturday", "Label")
                  WHERE "IsTemplate" = FALSE AND "WeekCode" IS NOT NULL;
                """,
                ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Index unique AppliesToSaturday ignoré: {ex.Message}");
        }
    }
}
