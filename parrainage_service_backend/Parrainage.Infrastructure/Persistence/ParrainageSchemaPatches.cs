using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Parrainage.Infrastructure.Persistence;

/// <summary>
/// Correctifs idempotents lorsque le schéma a été créé par l'ancien <c>EnsureCreated()</c>
/// sans historique <c>__EFMigrationsHistory</c>.
/// </summary>
public static class ParrainageSchemaPatches
{
    public const string InitialMigrationId = "20260602124434_InitialParrainage";
    public const string AddReferralNotesMigrationId = "20260602130710_AddReferralNotes";
    public const string AddReferralCandidateEmployeeIdMigrationId = "20260706120000_AddReferralCandidateEmployeeId";
    private const string EfProductVersion = "8.0.11";

    /// <summary>
    /// Enregistre les migrations déjà satisfaites par le schéma existant, puis laisse <see cref="DatabaseFacade.MigrateAsync"/> appliquer le reste.
    /// </summary>
    public static async Task BaselineLegacySchemaAsync(
        ParrainageDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "parrainage_audit_log", ct))
            return;

        await EnsureMigrationsHistoryTableAsync(db, ct);

        var initialRecorded = await RecordMigrationIfMissingAsync(db, InitialMigrationId, ct);
        if (initialRecorded)
        {
            logger.LogWarning(
                "PARRAINAGE : schéma détecté sans historique EF — migration {MigrationId} marquée comme appliquée (ancien EnsureCreated).",
                InitialMigrationId);
        }

        if (await ColumnExistsAsync(db, "parrainage_referral", "Notes", ct))
        {
            var notesRecorded = await RecordMigrationIfMissingAsync(db, AddReferralNotesMigrationId, ct);
            if (notesRecorded)
            {
                logger.LogInformation(
                    "PARRAINAGE : colonne Notes déjà présente — migration {MigrationId} marquée comme appliquée.",
                    AddReferralNotesMigrationId);
            }
        }
    }

    /// <summary>
    /// Correctifs idempotents pour colonnes ajoutées sans migration EF découverte (ex. Designer manquant).
    /// </summary>
    public static async Task ApplyPendingSchemaAsync(
        ParrainageDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "parrainage_referral", ct))
            return;

        if (!await ColumnExistsAsync(db, "parrainage_referral", "CandidateEmployeeId", ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE parrainage_referral
                ADD COLUMN IF NOT EXISTS "CandidateEmployeeId" character varying(128) NULL;
                """,
                ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_parrainage_referral_CandidateEmployeeId"
                ON parrainage_referral ("CandidateEmployeeId");
                """,
                ct);
            logger.LogInformation("PARRAINAGE : colonne CandidateEmployeeId ajoutée (patch runtime).");
        }

        await EnsureMigrationsHistoryTableAsync(db, ct);
        await RecordMigrationIfMissingAsync(db, AddReferralCandidateEmployeeIdMigrationId, ct);

        await EnsurePerfIndexesAsync(db, ct);
    }

    /// <summary>Index lecture fréquents (listes filtrées par statut / tri created_at).</summary>
    public static async Task EnsurePerfIndexesAsync(ParrainageDbContext db, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(db, "parrainage_referral", ct))
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_parrainage_referral_Status_CreatedAt"
                ON parrainage_referral ("Status", "CreatedAt");
            """,
            ct);
    }

    private static async Task EnsureMigrationsHistoryTableAsync(ParrainageDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """,
            ct);
    }

    private static async Task<bool> RecordMigrationIfMissingAsync(
        ParrainageDbContext db,
        string migrationId,
        CancellationToken ct)
    {
        if (await MigrationAppliedAsync(db, migrationId, ct))
            return false;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT {0}, {1}
            WHERE NOT EXISTS (
                SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = {0});
            """,
            [migrationId, EfProductVersion],
            ct);
        return true;
    }

    private static Task<bool> MigrationAppliedAsync(
        ParrainageDbContext db,
        string migrationId,
        CancellationToken ct) =>
        ScalarBoolAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = @migrationId);
            """,
            ct,
            ("@migrationId", migrationId));

    private static Task<bool> TableExistsAsync(ParrainageDbContext db, string tableName, CancellationToken ct) =>
        ScalarBoolAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = @tableName);
            """,
            ct,
            ("@tableName", tableName));

    private static Task<bool> ColumnExistsAsync(
        ParrainageDbContext db,
        string tableName,
        string columnName,
        CancellationToken ct) =>
        ScalarBoolAsync(
            db,
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND column_name = @columnName);
            """,
            ct,
            ("@tableName", tableName),
            ("@columnName", columnName));

    private static async Task<bool> ScalarBoolAsync(
        ParrainageDbContext db,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                AddParameter(cmd, name, value);

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

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
