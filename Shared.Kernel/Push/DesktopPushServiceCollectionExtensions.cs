using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Kernel.Push;

public static class DesktopPushServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopPushClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IDesktopPushClient, DesktopPushClient>((_, client) =>
        {
            var baseUrl = configuration["Auth:BaseUrl"] ?? "http://kyntus_auth_backend:8080/";
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
            var apiKey = configuration["InternalServices:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Service-Key", apiKey);
            }
        });
        return services;
    }
}
