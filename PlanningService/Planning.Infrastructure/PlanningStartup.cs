using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Planning.Application.Abstractions;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure;

public static class PlanningStartup
{
    /// <summary>
    /// Schéma + seed uniquement — doit rester rapide pour que /health réponde
    /// avant le timeout Compose (sinon gateway/frontends restent en Created).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var maxRetries = 10;
            var canConnect = false;
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    canConnect = await db.Database.CanConnectAsync();
                    if (canConnect)
                        break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⏳ Attente DB... tentative {i + 1}/{maxRetries}: {ex.Message}");
                }

                Thread.Sleep(3000);
            }

            if (canConnect)
            {
                try
                {
                    await db.Database.MigrateAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ MigrateAsync ignoré (patches SQL suivent): {ex.Message}");
                }

                await TryPatch("outbox", () => PlanningSchemaPatches.EnsureOutboxTableAsync(db));
                await TryPatch("planning-notifications", () => PlanningSchemaPatches.EnsurePlanningNotificationsTableAsync(db));
                await TryPatch("user-hr-profiles", () => PlanningSchemaPatches.EnsureUserHrProfilesTableAsync(db));
                await TryPatch("date-debut-formation", () => PlanningSchemaPatches.EnsureDateDebutFormationColumnAsync(db));
                await TryPatch("numero-carte-autoentrepreneur", () => PlanningSchemaPatches.EnsureNumeroCarteAutoentrepreneurColumnAsync(db));
                await TryPatch("email-personnel", () => PlanningSchemaPatches.EnsureEmailPersonnelColumnAsync(db));
                await TryPatch("conge-source-demande", () => PlanningSchemaPatches.EnsureCongeSourceDemandeIdColumnAsync(db));
                await TryPatch("shift-template", () => PlanningSchemaPatches.EnsureShiftTemplateAndValidationSchemaAsync(db));
                await TryPatch("shift-kind", () => PlanningSchemaPatches.EnsureShiftKindColumnAsync(db));
                await TryPatch("break-slots", () => PlanningSchemaPatches.EnsureBreakSlotsAndCriticalCellAsync(db));
                await TryPatch("planning-change-requests", () => PlanningSchemaPatches.EnsurePlanningChangeRequestsTableAsync(db));
                await TryPatch("planning-exceptional-requests", () => PlanningSchemaPatches.EnsurePlanningExceptionalRequestsTableAsync(db));
                await TryPatch("shift-assignment-exceptional", () => PlanningSchemaPatches.EnsureShiftAssignmentExceptionalFlagAsync(db));
                await TryPatch("planning-reinforcement-requests", () => PlanningSchemaPatches.EnsurePlanningReinforcementRequestsTableAsync(db));
                await TryPatch("pending-request-reminders", () => PlanningSchemaPatches.EnsurePendingRequestReminderColumnsAsync(db));
                await TryPatch("planning-sender-from", () => PlanningSchemaPatches.EnsurePlanningSenderFromColumnsAsync(db));
                await TryPatch("employee-import-source-file", () => PlanningSchemaPatches.EnsureEmployeeImportSourceFileColumnsAsync(db));
                await TryPatch("user-htel", () => PlanningSchemaPatches.EnsureUserHtelColumnsAsync(db));
                await TryPatch("user-saturday-work-mode", () => PlanningSchemaPatches.EnsureUserSaturdayWorkModeColumnAsync(db));
                await TryPatch("user-special-case", () => PlanningSchemaPatches.EnsureUserSpecialCaseColumnsAsync(db));
                await TryPatch("user-plateau-training", () => PlanningSchemaPatches.EnsureUserPlateauTrainingColumnAsync(db));
                await TryPatch("shift-mode-profiles", () => PlanningSchemaPatches.EnsureShiftModeProfilesSchemaAsync(db));
                await TryPatch("saturday-shift-config", () => PlanningSchemaPatches.EnsureSaturdayShiftConfigColumnAsync(db));
                await TryPatch("media-ticket-comments", () => PlanningSchemaPatches.EnsureMediaAndTicketCommentsAsync(db));
                await TryPatch("perf-indexes", () => PlanningSchemaPatches.EnsurePerfIndexesAsync(db));
                Console.WriteLine("✅ Patches schéma Planning appliqués.");
            }
        }

        using (var scope = services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await PlanningRoleSeed.EnsureCatalogAsync(context);
        }
    }

    /// <summary>
    /// Syncs lourdes (Auth, employés, newsletter, Directory) après écoute HTTP
    /// pour ne pas bloquer le healthcheck Docker Compose.
    /// </summary>
    public static void RegisterPostListenBootstrap(IServiceProvider services, IConfiguration configuration)
    {
        var hostLifetime = services.GetRequiredService<IHostApplicationLifetime>();
        hostLifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var scope = services.CreateScope())
                    {
                        var planningService = scope.ServiceProvider.GetRequiredService<IPlanningService>();
                        await planningService.SyncNewEmployeesAsync();
                    }

                    using (var scope = services.CreateScope())
                    {
                        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                        await userService.SyncMissingAuthUsersAsync();
                    }

                    if (configuration.GetValue("Directory:EnablePlanningBootstrap", false))
                    {
                        using var scope = services.CreateScope();
                        try
                        {
                            await PlanningDirectoryBootstrap.SyncExistingUsersToDirectoryAsync(scope.ServiceProvider);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Planning → Directory bootstrap ignoré: {ex.Message}");
                        }
                    }

                    using (var scope = services.CreateScope())
                    {
                        var newsletterService = scope.ServiceProvider.GetRequiredService<INewsletterService>();
                        await newsletterService.RepairCampaignAnalyticsUserIdsAsync();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await PlanningDirectoryOrgBootstrap.SyncFromDirectoryAsync(services);

                    try
                    {
                        await DockerComposePlanningEnrichmentSeed.ApplyIfEnabledAsync(services, configuration);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Planning enrichment ignoré: {ex.Message}");
                    }

                    try
                    {
                        await DockerComposeFormationNotificationsSeed.ApplyIfEnabledAsync(services, configuration);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Notifications formation seed ignoré: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Bootstrap post-écoute ignoré: {ex.Message}");
                }
            });
        });
    }

    private static async Task TryPatch(string name, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Patch {name} ignoré: {ex.Message}");
        }
    }
}
