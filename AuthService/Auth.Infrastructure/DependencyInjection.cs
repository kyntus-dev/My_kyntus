using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Repositories;
using Auth.Infrastructure.Security;
using Auth.Infrastructure.Services;
using Kyntus.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isTesting)
    {
        services.AddDbContext<AuthDbContext>(options =>
        {
            if (isTesting)
            {
                options.UseSqlite("Data Source=AuthCharacterizationTests;Mode=Memory;Cache=Shared");
            }
            else
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            }
        });

        services.AddKyntusCaching(configuration);
        services.AddHttpContextAccessor();
        services.AddScoped<IAccessAuditWriter, AccessAuditService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISubjectIdResolver, KyntusSubjectIdCatalog>();

        return services;
    }
}
