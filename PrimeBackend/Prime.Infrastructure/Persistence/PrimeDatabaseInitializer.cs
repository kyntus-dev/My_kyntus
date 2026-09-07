using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Prime.Infrastructure.Persistence;

public sealed class PrimeDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PrimeDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrimeDbContext>();

        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PRIME : échec MigrateAsync — tentative correctif schéma OrgOptional.");
        }

        try
        {
            await PrimeSchemaPatches.EnsureOrgOptionalAndDraftRootPoleAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PRIME : correctif schéma OrgOptional non appliqué.");
            throw;
        }

        try
        {
            await PrimeSchemaPatches.EnsureGlobalPoolSynthesisLineSchemaAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureAbsenceSanctionConfigTableAsync(db, cancellationToken);
            logger.LogInformation("PRIME : correctif schéma lignes synthèse globale appliqué.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PRIME : correctif schéma lignes synthèse globale non appliqué.");
            throw;
        }

        try
        {
            await PrimeSchemaPatches.EnsureEmployeeFicheDetailSnapshotColumnsAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureEmployeeManagerIdColumnsAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureOperationalValidationWorkflowAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureRhThenComptaGlobalPoolWorkflowAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureOutboxTableAsync(db, cancellationToken);
            logger.LogInformation("PRIME : correctif schéma fiche employé + workflow opérationnel appliqué.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PRIME : correctif schéma snapshot détail fiche employé non appliqué.");
            throw;
        }

        try
        {
            await PrimeSchemaPatches.EnsureAllowanceTrackSchemaAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureServicePoleLinePonderationTableAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureCommonLinePonderationTableAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsureEmployeeServiceIndicatorPonderationTableAsync(db, cancellationToken);
            await PrimeSchemaPatches.EnsurePerfIndexesAsync(db, cancellationToken);
            logger.LogInformation("PRIME : schéma Allowances + pondérations RACC/SAV appliqué.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PRIME : correctif schéma Allowances / pole-line ponderation non appliqué.");
            throw;
        }

        await EnsurePrimeMetierTablesExistAsync(db, cancellationToken);

        var enrich = configuration.GetValue("Prime:EnrichDemoData", false)
            && string.Equals(configuration["KYNTUS_DEMO_ENRICHMENT"] ?? "false", "true", StringComparison.OrdinalIgnoreCase);
        if (enrich)
        {
            try
            {
                await PrimeDbEnrichmentSeeder.EnrichAsync(db, force: false, cancellationToken, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PRIME : enrichissement démo ignoré.");
            }
        }
        else
        {
            logger.LogInformation("PRIME : base prête (sans seed démo).");
        }
    }

    /// <summary>
    /// Après Migrate, vérifie que le schéma métier V2 est bien présent. Sinon les API renvoient 42P01 peu lisibles.
    /// </summary>
    private static async Task EnsurePrimeMetierTablesExistAsync(PrimeDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = 'prime_workflow_step');
                """;
            var scalar = await cmd.ExecuteScalarAsync(ct);
            var present = scalar switch
            {
                true => true,
                false => false,
                long l => l != 0,
                int i => i != 0,
                _ => false,
            };
            if (present)
                return;

            throw new InvalidOperationException(
                "Schéma PRIME incomplet : la table « prime_workflow_step » est absente dans prime_db. "
                + "Les migrations EF n’ont pas été appliquées (ou le volume PostgreSQL est incohérent). "
                + "Corrigez en appliquant les migrations : depuis le dossier PrimeBackend, "
                + "`dotnet ef database update` avec la chaîne vers prime_db ; "
                + "en dev Docker, vous pouvez recréer le volume : `docker compose down -v` puis `docker compose up -d` (données locales perdues).");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
