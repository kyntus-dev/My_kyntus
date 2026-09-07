using Documentation.Application.Abstractions;
using Documentation.Application.Configuration;
using Documentation.Application.DocumentTemplates;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.Ai;
using Documentation.Infrastructure.Context;
using Documentation.Infrastructure.Messaging;
using Documentation.Infrastructure.Persistence;
using Documentation.Infrastructure.Services;
using Documentation.Infrastructure.Storage;
using Kyntus.Caching;
using Kyntus.Iam;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Documentation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isTesting)
    {
        services.AddKyntusIamViaDirectoryHttp(
            configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080");
        services.AddHttpContextAccessor();
        services.AddKyntusCaching(configuration);
        services.AddScoped<DocumentationCorrelationContext>();
        services.AddScoped<DocumentationUserContext>();
        services.AddScoped<IDocumentationTenantAccessor, DocumentationTenantAccessor>();
        services.AddScoped<DocumentationWorkflowService>();
        services.AddScoped<IDocumentationWorkflowAppService, DocumentationWorkflowAppService>();
        services.AddScoped<AiDirectDocumentFillOrchestrator>();
        services.AddScoped<IAiApiKeyAdminAppService, AiApiKeyAdminAppService>();
        services.AddScoped<IAiDirectDocumentAppService, AiDirectDocumentAppService>();
        services.AddSingleton<IStructuredDocumentDocxExportService, StructuredDocumentDocxExportService>();
        services.AddScoped<IDocumentationRequestContext>(sp => sp.GetRequiredService<DocumentationUserContext>());
        services.Configure<RibValidationOptions>(
            configuration.GetSection(RibValidationOptions.SectionName));
        services.AddSingleton<IRibValidationService, RibValidationService>();
        services.AddSingleton<ITemplatePlaceholderNormalizationService, TemplatePlaceholderNormalizationService>();
        services.AddSingleton<ITemplateEngineService, TemplateEngineService>();
        services.AddSingleton<IOriginalDocxTemplateRenderService, OriginalDocxTemplateRenderService>();
        services.AddSingleton<IPdfExportService, PdfExportService>();

        services.Configure<DocumentTemplatesInfrastructureOptions>(
            configuration.GetSection(DocumentTemplatesInfrastructureOptions.SectionName));
        services.Configure<AiTemplateOptions>(
            configuration.GetSection(AiTemplateOptions.SectionPath));
        services.Configure<DocumentBrandingOptions>(
            configuration.GetSection(DocumentBrandingOptions.SectionName));
        services.Configure<DocumentWorkflowOptions>(
            configuration.GetSection(DocumentWorkflowOptions.SectionName));
        services.AddSingleton<ITemplateBlobStorage, S3CompatibleTemplateBlobStorage>();
        services.AddScoped<IAiApiKeyResolver, AiApiKeyResolver>();
        services.AddHttpClient<IAiTemplateContentGenerator, OpenAiCompatibleTemplateContentGenerator>();
        services.AddScoped<DirectoryUserSyncService>();
        services.AddScoped<DocumentationJwtDirectoryProvisioner>();
        services.AddScoped<DocumentationDirectoryUserLookup>();
        services.AddHttpClient<IPlanningEmployeeFieldsClient, PlanningEmployeeFieldsClient>(client =>
        {
            var baseUrl = configuration["Planning:BaseUrl"] ?? "http://localhost:8521";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddScoped<IDocumentTemplateEmployeeMappingService, DocumentTemplateEmployeeMappingService>();
        services.AddScoped<IDocumentTemplateManagementService, DocumentTemplateManagementService>();
        services.AddScoped<IDocumentTypeQueryService, DocumentTypeQueryService>();

        if (isTesting)
        {
            services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
        }
        else
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<EmployeDirectorySyncConsumer>();
                x.AddConsumer<OrgStructureDirectorySyncConsumer>();
                x.AddConsumer<OrgAssignmentDirectorySyncConsumer>();
                x.AddConsumer<DirectoryEmployeeDocumentationProjectionConsumer>();
                x.AddConsumer<DirectoryEmployeeHrProfileDocumentationProjectionConsumer>();

                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(configuration["RabbitMQ:Host"] ?? "rabbitmq", "/", h =>
                    {
                        h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                        h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                    });

                    cfg.ReceiveEndpoint("documentation-employe-sync", e =>
                    {
                        e.ConfigureConsumer<EmployeDirectorySyncConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("documentation-org-structure", e =>
                    {
                        e.ConfigureConsumer<OrgStructureDirectorySyncConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("documentation-directory-projection", e =>
                    {
                        e.ConfigureConsumer<DirectoryEmployeeDocumentationProjectionConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("documentation-directory-hr-profile", e =>
                    {
                        e.ConfigureConsumer<DirectoryEmployeeHrProfileDocumentationProjectionConsumer>(ctx);
                    });

                    cfg.ConfigureEndpoints(ctx);
                });
            });
        }

        if (!isTesting)
        {
            var documentationCs = configuration.GetConnectionString("Documentation")
                ?? throw new InvalidOperationException("ConnectionStrings:Documentation manquante (voir appsettings).");

            var csb = new NpgsqlConnectionStringBuilder(documentationCs);
            var documentationPassword = configuration["DocumentationDb:Password"];
            if (!string.IsNullOrEmpty(documentationPassword))
                csb.Password = documentationPassword;

            if (string.IsNullOrWhiteSpace(csb.SearchPath))
                csb.SearchPath = "documentation, public";

            const string DocEnum = "documentation";
            var documentationDataSourceBuilder = new NpgsqlDataSourceBuilder(csb.ConnectionString);
            documentationDataSourceBuilder.MapEnum<DocumentRequestStatus>($"{DocEnum}.document_request_status");
            documentationDataSourceBuilder.MapEnum<GeneratedDocumentStatus>($"{DocEnum}.generated_document_status");
            documentationDataSourceBuilder.MapEnum<WorkflowNotificationKey>($"{DocEnum}.workflow_notification_key");
            documentationDataSourceBuilder.MapEnum<WorkflowActionKey>($"{DocEnum}.workflow_action_key");
            documentationDataSourceBuilder.MapEnum<AppRole>($"{DocEnum}.app_role");
            documentationDataSourceBuilder.MapEnum<StorageType>($"{DocEnum}.storage_type");
            documentationDataSourceBuilder.MapEnum<DocumentTemplateKind>($"{DocEnum}.document_template_kind");
            var documentationDataSource = documentationDataSourceBuilder.Build();
            services.AddSingleton(documentationDataSource);

            services.AddDbContext<DocumentationDbContext>((sp, options) =>
            {
                options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "documentation");
                });
                options.UseSnakeCaseNamingConvention();
            });

            services.AddScoped<IDocumentationDmsAdminAppService, DocumentationDmsAdminAppService>();
            services.AddScoped<IDirectoryQueryAppService, DirectoryQueryAppService>();
            services.AddScoped<IDocumentRequestAppService, DocumentRequestAppService>();
            services.AddScoped<IAuditLogQueryAppService, AuditLogQueryAppService>();
            services.AddHttpClient<IPlanningEmployeeFieldsClient, PlanningEmployeeFieldsClient>(client =>
            {
                var baseUrl = configuration["Planning:BaseUrl"] ?? "http://localhost:8521";
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(8);
            });
            services.AddScoped<IDocumentTemplateEmployeeMappingService, DocumentTemplateEmployeeMappingService>();
            services.AddScoped<IDocumentTemplateVariableMergeService, DocumentTemplateVariableMergeService>();
            services.AddScoped<IGeneratedDocumentAppService, GeneratedDocumentAppService>();
            services.AddScoped<IDocumentTemplateAppService, DocumentTemplateAppService>();
            services.AddScoped<IDocumentWorkflowGenerationAppService, DocumentWorkflowGenerationAppService>();
        }

        return services;
    }
}
