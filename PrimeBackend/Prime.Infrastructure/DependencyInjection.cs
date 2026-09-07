using Kyntus.Caching;
using Kyntus.Iam;
using Kyntus.Messaging.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prime.Application.Abstractions;
using Prime.Infrastructure.Messaging;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Shared.Kernel.Push;

namespace Prime.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPrimeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isTesting)
    {
        var directoryBase = configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080";

        services.AddScoped<PrimeOrgStructureCommandService>(sp =>
            new PrimeOrgStructureCommandService(
                sp.GetRequiredService<PrimeDbContext>(),
                sp.GetService<IRebacClient>()));
        services.AddScoped<PrimeAdminDbReadService>();
        services.AddScoped<IPrimeAdminReadAppService>(sp => sp.GetRequiredService<PrimeAdminDbReadService>());
        services.AddKyntusOutbox<PrimeDbContext>();
        services.AddKyntusCaching(configuration);
        services.AddKyntusIam();
        services.AddDesktopPushClient(configuration);
        services.AddScoped<IPermissionCatalog, PrimePermissionCatalog>();
        services.AddHttpContextAccessor();
        services.AddTransient<DirectoryForwardAuthorizationHandler>();
        services.AddHttpClient<IRebacClient, DirectoryHttpRebacClient>(c =>
                c.BaseAddress = new Uri(directoryBase.TrimEnd('/') + "/"))
            .AddHttpMessageHandler<DirectoryForwardAuthorizationHandler>();
        services.AddHttpClient<IPrimeDirectoryAsOfClient, PrimeDirectoryAsOfClient>(c => c.BaseAddress = new Uri(directoryBase));
        services.AddScoped<IOrgStructureEventPublisher, OrgStructureEventPublisher>();
        services.AddScoped<PrimeAuditLogService>();
        services.AddScoped<PrimeOrgScopeService>(sp =>
            new PrimeOrgScopeService(
                sp.GetService<PrimeDbContext>(),
                sp.GetService<IRebacClient>(),
                sp.GetService<ICacheService>()));
        services.AddScoped<PrimeRpQueryService>(sp =>
            new PrimeRpQueryService(
                sp.GetService<PrimeDbContext>(),
                sp.GetService<IPrimeDirectoryAsOfClient>(),
                sp.GetService<PrimeOrgScopeService>()));
        services.AddScoped<IPrimeRpAppService>(sp => sp.GetRequiredService<PrimeRpQueryService>());

        var conn = configuration.GetConnectionString("DefaultConnection");
        services.AddSingleton<SlowQueryCommandInterceptor>();
        if (isTesting || !string.IsNullOrWhiteSpace(conn))
        {
            if (isTesting)
                services.AddDbContext<PrimeDbContext>((sp, o) =>
                {
                    o.UseSqlite(conn ?? "DataSource=prime_characterization_test.db");
                    o.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
                });
            else
                services.AddDbContext<PrimeDbContext>((sp, o) =>
                {
                    o.UseNpgsql(conn!);
                    o.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
                });

            if (!isTesting && configuration.GetValue("Prime:ApplyMigrations", true))
                services.AddHostedService<PrimeDatabaseInitializer>();

            services.AddScoped<IEmployeeDirectorySyncService, EmployeeDirectorySyncService>();
            services.AddScoped<AnomalyDetectionService>();
            services.AddScoped<PrimeValidationWorkflowRuntime>();
            services.AddScoped<PrimeFicheValidationSubmissionService>();
            services.AddScoped<PrimeValidationListService>();
            services.AddScoped<PrimeFicheValidationHistoryService>();
            services.AddScoped<PrimeRbacReadService>();
            services.AddScoped<IRbacAdminService, RbacAdminService>();
            services.AddScoped<IWorkflowConfigAdminService, WorkflowConfigAdminService>();
            services.AddScoped<IAuditLogAdminService, AuditLogAdminService>();
            services.AddScoped<IAnomalyAdminService, AnomalyAdminService>();
            services.AddScoped<IGlobalPoolWorkflowAdminService, GlobalPoolWorkflowAdminService>();
            services.AddScoped<IEmployeePrimeServiceFicheAppService, EmployeePrimeServiceFicheAppService>();
            services.AddScoped<IPrimeValidationAppService, PrimeValidationAppService>();
            services.AddScoped<ISupervisorCellulePrimeDraftAppService, SupervisorCellulePrimeDraftAppService>();
            services.AddScoped<ISupervisorCampaignAppService, SupervisorCampaignAppService>();
            services.AddScoped<ISupervisorPrimeFicheAppService, SupervisorPrimeFicheAppService>();
            services.AddScoped<IPrimeCelluleDraftGlobalPoolAppService, PrimeCelluleDraftGlobalPoolAppService>();
            services.AddScoped<IPrimeFichePreviewAppService, PrimeFichePreviewAppService>();
            services.AddScoped<IPrimePilotageAppService, PrimePilotageAppService>();
            services.AddScoped<IPrimeGlobalPoolStakeholderAppService, PrimeGlobalPoolStakeholderAppService>();
            services.AddScoped<IPrimeGlobalPoolScopeAppService, PrimeGlobalPoolScopeAppService>();
            services.AddScoped<IServicePrimeIndicatorsAppService, ServicePrimeIndicatorsAppService>();
            services.AddScoped<ICommonLinePonderationResolver, CommonLinePonderationResolver>();
            services.AddScoped<ICommonLinePonderationsAppService, CommonLinePonderationsAppService>();
            services.AddScoped<IPrimePeriodRecapReportsAppService, PrimePeriodRecapReportsAppService>();
            services.AddScoped<IPrimeFicheImportAppService, PrimeFicheImportAppService>();
            services.AddScoped<IPrimeCoreQueryAppService, PrimeCoreQueryAppService>();
            services.AddScoped<IAllowanceQueryAppService, AllowanceQueryAppService>();
            services.AddScoped<IAllowanceOperationsAppService, AllowanceOperationsAppService>();
            services.AddScoped<IPrimeOrgAssignmentsAppService, PrimeOrgAssignmentsAppService>();
            services.AddScoped<PrimeJwtEmployeeProvisioner>();
            services.AddScoped<IPrimeRequestUserResolver, PrimeRequestUserResolver>();
            services.AddScoped<GlobalPoolWorkflowService>();
            services.AddScoped<PrimeGlobalSynthesisReadinessService>();
            services.AddScoped<PrimeGlobalSynthesisService>();
            services.AddScoped<PrimeGlobalSynthesisLineService>();
            services.AddScoped<PrimeGlobalSynthesisPaymentService>();
            services.AddHttpClient<IPlanningAbsenceClient, PlanningAbsenceClient>();
            services.AddScoped<IPrimeAbsenceSanctionConfigService, PrimeAbsenceSanctionConfigService>();
            services.AddScoped<PrimeAbsenceSanctionService>();
            services.AddScoped<IPrimeAbsenceSanctionConfigAppService, PrimeAbsenceSanctionConfigAppService>();
            services.AddScoped<PrimeFicheMergedPreviewAccessService>();
            services.AddScoped<PrimeFicheImportService>();
            services.AddScoped<AllowanceScopeService>(sp =>
                new AllowanceScopeService(
                    sp.GetRequiredService<PrimeDbContext>(),
                    sp.GetService<IRebacClient>()));
            services.AddScoped<AllowanceCatalogService>();
            services.AddScoped<AllowanceRequestService>();
            services.AddScoped<AllowanceTeamPilotageService>();
            services.AddScoped<AllowanceRuleEngineService>();
        }

        services.AddMassTransit(x =>
        {
            if (!isTesting)
            {
                x.AddConsumer<PrimeDirectoryProjectionConsumer>();
                x.AddConsumer<PrimeDirectoryOrgProjectionConsumer>();
                x.AddConsumer<PrimeBusinessDepartmentProjectionConsumer>();
            }

            if (isTesting)
            {
                x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));
            }
            else
            {
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(configuration["RabbitMQ:Host"] ?? "rabbitmq", "/", h =>
                    {
                        h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                        h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                    });

                    cfg.ReceiveEndpoint("prime-directory-projection", e =>
                        e.ConfigureConsumer<PrimeDirectoryProjectionConsumer>(ctx));
                    cfg.ReceiveEndpoint("prime-directory-org", e =>
                        e.ConfigureConsumer<PrimeDirectoryOrgProjectionConsumer>(ctx));
                    cfg.ReceiveEndpoint("prime-business-department", e =>
                        e.ConfigureConsumer<PrimeBusinessDepartmentProjectionConsumer>(ctx));

                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return services;
    }
}
