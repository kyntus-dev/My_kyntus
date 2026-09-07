using System.Data;
using Documentation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Documentation.Infrastructure;

/// <summary>
/// Fonctions PostgreSQL absentes d'EF EnsureCreated (ex. numérotation REQ).
/// </summary>
internal static class DocumentationSchemaBootstrap
{
    private const string NextDocumentRequestNumberSql =
        """
        CREATE OR REPLACE FUNCTION documentation.next_document_request_number(p_tenant text)
        RETURNS text
        LANGUAGE plpgsql
        AS $$
        DECLARE
          v_year int := EXTRACT(YEAR FROM now())::int;
          v_next int;
        BEGIN
          PERFORM pg_advisory_xact_lock(hashtext(p_tenant), v_year);

          INSERT INTO documentation.document_request_sequences (tenant_id, year, last_value)
          VALUES (p_tenant, v_year, 1)
          ON CONFLICT (tenant_id, year) DO UPDATE
            SET last_value = documentation.document_request_sequences.last_value + 1
          RETURNING last_value INTO v_next;

          RETURN format('REQ-%s-%s', v_year, lpad(v_next::text, 6, '0'));
        END;
        $$;
        """;

    internal static async Task ApplyPostSchemaObjectsAsync(
        DocumentationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await FunctionExistsAsync(db, cancellationToken).ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync(NextDocumentRequestNumberSql, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "Documentation schema bootstrap — fonction documentation.next_document_request_number créée.");
            }
            else
            {
                logger.LogInformation(
                    "Documentation schema bootstrap — documentation.next_document_request_number déjà présente.");
            }
        }
        catch (PostgresException pgEx) when (pgEx.SqlState is "42501")
        {
            if (await FunctionExistsAsync(db, cancellationToken).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "Documentation schema bootstrap — fonction REQ déjà provisionnée (droits CREATE insuffisants, ignoré).");
            }
            else
            {
                logger.LogError(
                    pgEx,
                    "Documentation schema bootstrap — fonction REQ absente et création refusée (42501). Exécutez init/sql/documentation_004_next_document_request_number.sql en superuser.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Documentation schema bootstrap — création next_document_request_number non appliquée (schéma absent ou droits limités).");
        }

        await ApplyPerformanceIndexesAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await ApplyDirectoryUsersHrProfileColumnsAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await ApplyTemplateVariableMappingColumnsAsync(db, logger, cancellationToken).ConfigureAwait(false);
        await EnsureDefaultOrganisationUnitsAsync(db, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unités org minimales pour le provisionnement JWT / sync (évite FK 23503 sur directory_users).
    /// </summary>
    internal static async Task EnsureDefaultOrganisationUnitsAsync(
        DocumentationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default,
        string tenantId = "atlas-tech-demo")
    {
        // IDs alignés avec Documentation:Sync:DefaultPoleId / DefaultCelluleId / DefaultDepartementId.
        const string sql =
            """
            INSERT INTO documentation.organisation_units
              (id, tenant_id, parent_id, unit_type, code, name, created_at, updated_at)
            VALUES
              ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa01', {0}, NULL,
               'pole', 'DEFAULT-POLE', 'Pôle par défaut', NOW(), NOW()),
              ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa02', {0}, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa01',
               'cellule', 'DEFAULT-CELLULE', 'Cellule par défaut', NOW(), NOW()),
              ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa03', {0}, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa02',
               'departement', 'DEFAULT-DEPT', 'Département par défaut', NOW(), NOW())
            ON CONFLICT (id) DO NOTHING;
            """;

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, [tenantId], cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Documentation schema bootstrap — unités organisationnelles par défaut vérifiées (tenant={Tenant}).",
                tenantId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation schema bootstrap — unités org par défaut non appliquées.");
        }
    }

    internal static async Task ApplyDirectoryUsersHrProfileColumnsAsync(
        DocumentationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            ALTER TABLE documentation.directory_users
              ADD COLUMN IF NOT EXISTS cin character varying(32);
            ALTER TABLE documentation.directory_users
              ADD COLUMN IF NOT EXISTS rib character varying(64);
            ALTER TABLE documentation.directory_users
              ADD COLUMN IF NOT EXISTS immatriculation_cnss character varying(32);
            """;

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Documentation schema bootstrap — colonnes HR profile directory_users vérifiées.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation schema bootstrap — colonnes HR profile directory_users non appliquées.");
        }
    }

    internal static async Task ApplyTemplateVariableMappingColumnsAsync(
        DocumentationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            ALTER TABLE documentation.document_template_variables
              ADD COLUMN IF NOT EXISTS source_field_key character varying(128);
            ALTER TABLE documentation.document_template_variables
              ADD COLUMN IF NOT EXISTS mapping_confidence character varying(16);
            ALTER TABLE documentation.document_template_variables
              ADD COLUMN IF NOT EXISTS mapping_confirmed boolean NOT NULL DEFAULT false;
            """;

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Documentation schema bootstrap — colonnes mapping variables de modèle vérifiées.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation schema bootstrap — colonnes mapping variables de modèle non appliquées.");
        }
    }

    internal static async Task ApplyPerformanceIndexesAsync(
        DocumentationDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            CREATE INDEX IF NOT EXISTS ix_document_requests_tenant_status_created
              ON documentation.document_requests (tenant_id, status, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_document_requests_requester_user_id
              ON documentation.document_requests (requester_user_id);
            CREATE INDEX IF NOT EXISTS ix_document_requests_beneficiary_user_id
              ON documentation.document_requests (beneficiary_user_id);
            CREATE INDEX IF NOT EXISTS ix_generated_documents_request_created
              ON documentation.generated_documents (document_request_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_directory_users_tenant_email
              ON documentation.directory_users (tenant_id, lower(email));
            """;

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Documentation schema bootstrap — index performance vérifiés ou créés.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation schema bootstrap — index performance non appliqués.");
        }
    }

    private static async Task<bool> FunctionExistsAsync(
        DocumentationDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT 1
            FROM pg_proc p
            INNER JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'documentation'
              AND p.proname = 'next_document_request_number'
            LIMIT 1
            """;
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null and not DBNull;
    }
}
