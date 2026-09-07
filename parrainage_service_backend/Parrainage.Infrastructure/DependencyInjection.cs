using Kyntus.Caching;
using Kyntus.Iam;
using MassTransit;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parrainage.Application.Abstractions;
using Parrainage.Infrastructure.Messaging;
using Parrainage.Infrastructure.Persistence;
using Parrainage.Infrastructure.Services;

namespace Parrainage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddParrainageInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isTesting)
    {
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

        services.AddKyntusIamViaDirectoryHttp(
            configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080");
        services.AddHttpContextAccessor();
        services.AddKyntusCaching(configuration);
        services.AddScoped<ParrainagePolicyService>();

        var conn = configuration.GetConnectionString("DefaultConnection");
        if (isTesting || !string.IsNullOrWhiteSpace(conn))
        {
            if (isTesting)
                services.AddDbContext<ParrainageDbContext>(o => o.UseSqlite(conn ?? "DataSource=parrainage_characterization_test.db"));
            else
                services.AddDbContext<ParrainageDbContext>(o => o.UseNpgsql(conn!));

            services.AddScoped<ReferralRuleResolver>();
            services.AddHttpClient<IPlanningEmploymentCheckClient, PlanningEmploymentCheckClient>();
            services.AddScoped<ReferralWorkflowService>();
            services.AddScoped<ReferralEligibilityService>();
            services.AddSingleton<ReferralCvStorageService>();
            services.AddScoped<IParrainageRequestUserResolver, ParrainageRequestUserResolver>();
            services.AddScoped<IReferralAppService, ReferralAppService>();
            services.AddScoped<IPaymentAppService, PaymentAppService>();
            services.AddScoped<INotificationAppService, NotificationAppService>();
            services.AddScoped<ISystemConfigAppService, SystemConfigAppService>();
            services.AddScoped<IReferralRulesAppService, ReferralRulesAppService>();
            services.AddScoped<IParrainageAuditAppService, ParrainageAuditAppService>();
            services.AddScoped<IOrgHierarchyQueryService, OrgHierarchyQueryService>();
            services.AddScoped<IAnomalyQueryService, AnomalyQueryService>();
            services.AddScoped<IAdminExportAppService, AdminExportAppService>();

            if (!isTesting)
            {
                services.AddHostedService<ParrainageDatabaseInitializer>();
                services.AddHostedService<ReferralEligibilityHostedService>();
            }
        }

        if (isTesting)
        {
            services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
        }
        else
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<EmployePortalSyncConsumer>();
                x.AddConsumer<OrgAssignmentPortalSyncConsumer>();
                x.AddConsumer<DirectoryEmployeePortalProjectionConsumer>();
                x.AddConsumer<InitialTrainingRejectedReferralConsumer>();
                x.AddConsumer<InitialTrainingCompletedReferralConsumer>();

                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(configuration["RabbitMQ:Host"] ?? "rabbitmq", "/", h =>
                    {
                        h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                        h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                    });

                    cfg.ReceiveEndpoint("parrainage-employe-sync", e =>
                    {
                        e.ConfigureConsumer<EmployePortalSyncConsumer>(ctx);
                        e.ConfigureConsumer<OrgAssignmentPortalSyncConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("parrainage-directory-projection", e =>
                    {
                        e.ConfigureConsumer<DirectoryEmployeePortalProjectionConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("parrainage-initial-training-rejected", e =>
                    {
                        e.ConfigureConsumer<InitialTrainingRejectedReferralConsumer>(ctx);
                    });

                    cfg.ReceiveEndpoint("parrainage-initial-training-completed", e =>
                    {
                        e.ConfigureConsumer<InitialTrainingCompletedReferralConsumer>(ctx);
                    });

                    cfg.ConfigureEndpoints(ctx);
                });
            });
        }

        return services;
    }
}
