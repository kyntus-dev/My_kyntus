using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Domain.Interfaces;
using EmployeeDirectory.Infrastructure.Messaging;
using EmployeeDirectory.Infrastructure.Messaging.Consumers;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Caching;
using Kyntus.Iam;
using Kyntus.Messaging.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeDirectory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDirectoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection est requis. Définir via appsettings, user-secrets ou variable d'environnement.");

        var useSqlite = conn.StartsWith("DataSource=", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase);

        services.AddSingleton<SlowQueryCommandInterceptor>();
        if (useSqlite)
            services.AddDbContext<DirectoryDbContext>((sp, o) =>
            {
                o.UseSqlite(conn);
                o.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
            });
        else
            services.AddDbContext<DirectoryDbContext>((sp, o) =>
            {
                o.UseNpgsql(conn);
                o.AddInterceptors(sp.GetRequiredService<SlowQueryCommandInterceptor>());
            });
        services.AddKyntusOutbox<DirectoryDbContext>();
        services.AddKyntusIam();
        services.AddKyntusCaching(configuration);
        services.AddScoped<IDirectoryCacheInvalidator, DirectoryCacheInvalidator>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DirectoryDbContext>());

        services.AddScoped<IDirectoryReadService, DirectoryReadService>();
        services.AddScoped<IOrgStructuralRoleExclusivityService, OrgStructuralRoleExclusivityService>();
        services.AddScoped<IPilotRotationTenureService, PilotRotationTenureService>();
        services.AddScoped<IDirectoryWriteService, DirectoryWriteService>();
        services.AddScoped<IDirectoryReconciliationService, DirectoryReconciliationService>();
        services.AddHttpClient("DirectoryReconcile");
        services.AddHttpClient("Htel", (sp, client) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Htel:BaseUrl"]?.Trim() ?? "https://htel-groupe.fr/";
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            var apiKey = configuration["Htel:ApiKey"]?.Trim();
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
        });
        services.AddScoped<IHtelTechnicienClient, HtelTechnicienClient>();
        services.AddScoped<IHtelFusionService, HtelFusionService>();
        services.AddScoped<DirectoryHierarchyService>();
        services.AddScoped<IOrgResponsibilityResolver, OrgResponsibilityResolver>();
        services.AddScoped<IIamReadService, DirectoryIamReadService>();
        services.AddScoped<IIamAdminService, DirectoryIamAdminService>();
        services.AddScoped<IPermissionCatalog, DirectoryIamReadService>();
        services.AddScoped<IRebacClient, DirectoryIamReadService>();

        services.AddScoped<DirectoryEmployeSyncService>();
        services.AddScoped<DirectoryOrgSyncService>();
        services.AddScoped<DirectoryAssignmentSyncService>();

        var rabbitHost = configuration["RabbitMQ:Host"] ?? "localhost";
        var rabbitUser = configuration["RabbitMQ:Username"] ?? "guest";
        var rabbitPass = configuration["RabbitMQ:Password"] ?? "guest";

        if (useSqlite)
        {
            services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
        }
        else
        {
        services.AddMassTransit(x =>
        {
            x.AddConsumer<DirectoryEmployeSyncConsumer>();
            x.AddConsumer<DirectoryOrgSyncConsumer>();
            x.AddConsumer<DirectoryAssignmentSyncConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(rabbitHost, "/", h =>
                {
                    h.Username(rabbitUser);
                    h.Password(rabbitPass);
                });

                cfg.ReceiveEndpoint("directory-employe-sync", e =>
                {
                    e.ConfigureConsumer<DirectoryEmployeSyncConsumer>(ctx);
                });
                cfg.ReceiveEndpoint("directory-org-structure", e =>
                {
                    e.ConfigureConsumer<DirectoryOrgSyncConsumer>(ctx);
                });
                cfg.ReceiveEndpoint("directory-org-assignment", e =>
                {
                    e.ConfigureConsumer<DirectoryAssignmentSyncConsumer>(ctx);
                });
            });
        });
        }

        return services;
    }
}
