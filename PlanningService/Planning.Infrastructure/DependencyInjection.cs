using Kyntus.Caching;
using Kyntus.Iam;
using Kyntus.Messaging.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Infrastructure.Messaging.Consumers;
using Planning.Infrastructure.Messaging.Publishers;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;
using Planning.Infrastructure.Services.EmployeeImport;
using Shared.Kernel.Push;
using PlanningServiceImpl = Planning.Infrastructure.Services.PlanningService;

namespace Planning.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPlanningInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isTesting)
    {
        services.AddSingleton<SlowQueryCommandInterceptor>();
        if (isTesting)
        {
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseSqlite(configuration.GetConnectionString("DefaultConnection")
                    ?? "DataSource=planning_characterization_test.db");
                options.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
            });
        }
        else
        {
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
                options.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
            });
        }

        services.AddKyntusOutbox<AppDbContext>();
        services.AddKyntusCaching(configuration);
        services.AddKyntusIamViaDirectoryHttp(
            configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080");
        services.AddDesktopPushClient(configuration);
        services.AddHttpClient("PlanningMailApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IPlanningEmailSender, MailKitPlanningEmailSender>();
        services.AddScoped<IPilotPlanningPersonalEmailService, PilotPlanningPersonalEmailService>();

        services.AddScoped<IFloorService, FloorService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPlanningCongeService, PlanningCongeService>();
        services.AddScoped<IUserLegacyExcelService, UserLegacyExcelService>();
        services.AddScoped<IServiceService, ServiceService>();
        services.AddScoped<ISubServiceService, SubServiceService>();
        services.AddHttpClient("DirectorySync");
        services.AddHttpClient("FormationSync");
        services.AddScoped<IDirectoryEmployeeEnsureClient, DirectoryEmployeeEnsureClient>();
        services.AddScoped<IDirectoryEmployeeWriteClient, DirectoryEmployeeWriteClient>();
        services.AddScoped<IFormationInitialTrainingClient, FormationInitialTrainingClient>();
        services.AddScoped<IDirectoryHierarchyClient, DirectoryHierarchyClient>();
        services.AddHttpClient<IUserService, UserService>((sp, client) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Auth:BaseUrl"] ?? "http://kyntus_auth_backend:8080/";
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            var apiKey = configuration["InternalServices:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Service-Key", apiKey);
            }
        });
        services.AddScoped<IContractService, ContractService>();
        services.AddScoped<IPlanningOrgMirrorService, PlanningOrgMirrorService>();
        services.AddScoped<IOrgReconciliationService, OrgReconciliationService>();
        services.AddScoped<IPlanningService, PlanningServiceImpl>();
        services.AddScoped<IPlanningLeaveImpactService, PlanningLeaveImpactService>();
        services.AddScoped<IPlanningPerimeterResolver, PlanningPerimeterResolver>();
        services.AddScoped<IPlanningChangeRequestService, PlanningChangeRequestService>();
        services.AddScoped<IPlanningExceptionalRequestService, PlanningExceptionalRequestService>();
        services.AddScoped<IPlanningReinforcementRequestService, PlanningReinforcementRequestService>();
        services.AddScoped<IPlanningPendingRequestsAlertService, PlanningPendingRequestsAlertService>();
        services.AddHostedService<WeeklyPlanningAutoGeneratorHostedService>();
        services.AddHostedService<PendingRequestsReminderHostedService>();
        services.AddScoped<IPlanningOrgMirrorService, PlanningOrgMirrorService>();
        services.AddScoped<IDirectoryOrgWriteClient, DirectoryOrgWriteClient>();

        services.AddScoped<IEmployeeFieldService, EmployeeFieldService>();
        services.AddSingleton<IEmployeeImportCredentialsStore, EmployeeImportCredentialsStore>();
        services.AddScoped<IEmployeeImportConfigService, EmployeeImportConfigService>();
        services.AddScoped<IEmployeeImportSessionStore, EmployeeImportSessionStore>();
        services.AddScoped<IEmployeeImportOrgResolver, EmployeeImportOrgResolver>();
        services.AddScoped<IEmployeeImportOrgGapAnalyzer, EmployeeImportOrgGapAnalyzer>();
        services.AddScoped<IEmployeeImportOrgProvisioner, EmployeeImportDirectoryOrgProvisioner>();
        services.AddScoped<IEmployeeImportStructureAssignmentService, EmployeeImportStructureAssignmentService>();
        services.AddScoped<IEmployeeImportExecutor, EmployeeImportExecutor>();
        services.AddScoped<IImportExecutionJournal, ImportExecutionJournal>();
        services.AddScoped<IEmployeeImportService, EmployeeImportService>();
        services.AddScoped<IEmployeeImportUserPersistence, EmployeeImportUserPersistence>();
        services.AddSingleton<IEmployeeImportExecuteQueue, EmployeeImportExecuteQueue>();
        services.AddHostedService<EmployeeImportExecuteHostedService>();
        services.AddSingleton<EmployeeImportFileParser>();
        services.AddSingleton<EmployeeImportColumnMatcher>();
        services.AddScoped<IEmployeeDocumentFieldQueryService, EmployeeDocumentFieldQueryService>();
        services.AddScoped<EmployeeImportTemplateBuilder>();

        services.AddScoped<IReclamationService, ReclamationService>();
        services.AddScoped<IPropositionService, PropositionService>();
        services.AddScoped<IReclamationNotificationService, ReclamationNotificationService>();
        services.AddScoped<INewsletterService, NewsletterService>();
        services.AddScoped<IMediaAssetService, MediaAssetService>();
        services.AddScoped<ITicketCommentService, TicketCommentService>();
        services.AddScoped<IEmployePublisher, EmployePublisher>();

        if (isTesting)
        {
            services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
        }
        else
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<CongeValideConsumer>();
                x.AddConsumer<CongeRefuseConsumer>();
                x.AddConsumer<OrgStructureConsumer>();
                x.AddConsumer<DirectoryEmployeeProjectionConsumer>();
                x.AddConsumer<DirectoryEmployeeHrProfileProjectionConsumer>();
                x.AddConsumer<PlanningDirectoryOrgProjectionConsumer>();
                x.AddConsumer<InitialTrainingRejectedConsumer>();
                x.AddConsumer<InitialTrainingCompletedConsumer>();
                x.AddConsumer<TrainingSessionAssignedConsumer>();
                x.AddConsumer<TrainingSessionAnimatorAssignedConsumer>();
                x.AddConsumer<TrainingSessionStartedConsumer>();
                x.AddConsumer<InitialTrainingMissingDocumentsAlertConsumer>();
                x.AddConsumer<TrainingQuizPublishedConsumer>();
                x.AddConsumer<TrainingQuizAttemptSubmittedConsumer>();
                x.AddConsumer<TrainingQuizNeedsGradingConsumer>();
                x.AddConsumer<TrainingQuizValidatedConsumer>();
                x.AddConsumer<TrainingQuizRejectedConsumer>();
                x.AddConsumer<TrainingQuizResultReadyConsumer>();
                x.AddConsumer<CatalogEnrollmentDeadlineReminderConsumer>();
                x.AddConsumer<CatalogFormationAvailableConsumer>();

                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(configuration["RabbitMQ:Host"] ?? "rabbitmq", "/", h =>
                    {
                        h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                        h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                    });

                    cfg.ReceiveEndpoint("planning-conge-valide", e =>
                        e.ConfigureConsumer<CongeValideConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-conge-refuse", e =>
                        e.ConfigureConsumer<CongeRefuseConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-org-structure", e =>
                        e.ConfigureConsumer<OrgStructureConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-directory-employee", e =>
                        e.ConfigureConsumer<DirectoryEmployeeProjectionConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-directory-hr-profile", e =>
                        e.ConfigureConsumer<DirectoryEmployeeHrProfileProjectionConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-directory-org", e =>
                        e.ConfigureConsumer<PlanningDirectoryOrgProjectionConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-initial-training-rejected", e =>
                        e.ConfigureConsumer<InitialTrainingRejectedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-initial-training-completed", e =>
                        e.ConfigureConsumer<InitialTrainingCompletedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-session-assigned", e =>
                        e.ConfigureConsumer<TrainingSessionAssignedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-session-animator", e =>
                        e.ConfigureConsumer<TrainingSessionAnimatorAssignedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-session-started", e =>
                        e.ConfigureConsumer<TrainingSessionStartedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-initial-training-missing-docs", e =>
                        e.ConfigureConsumer<InitialTrainingMissingDocumentsAlertConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-published", e =>
                        e.ConfigureConsumer<TrainingQuizPublishedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-attempt-submitted", e =>
                        e.ConfigureConsumer<TrainingQuizAttemptSubmittedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-needs-grading", e =>
                        e.ConfigureConsumer<TrainingQuizNeedsGradingConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-validated", e =>
                        e.ConfigureConsumer<TrainingQuizValidatedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-rejected", e =>
                        e.ConfigureConsumer<TrainingQuizRejectedConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-training-quiz-result-ready", e =>
                        e.ConfigureConsumer<TrainingQuizResultReadyConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-catalog-enrollment-deadline", e =>
                        e.ConfigureConsumer<CatalogEnrollmentDeadlineReminderConsumer>(ctx));
                    cfg.ReceiveEndpoint("planning-catalog-formation-available", e =>
                        e.ConfigureConsumer<CatalogFormationAvailableConsumer>(ctx));

                    cfg.ConfigureEndpoints(ctx);
                });
            });
        }

        return services;
    }
}
