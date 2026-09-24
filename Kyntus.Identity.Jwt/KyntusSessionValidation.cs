using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kyntus.Identity.Jwt;

public static class KyntusSessionValidationExtensions
{
    public static IServiceCollection AddKyntusSessionValidation(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["SessionValidation:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return services; // Existing non-Compose/test environments.
        var endpoint = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/Auth/session/validate");
        services.AddHttpClient<KyntusSessionValidator>(client =>
        {
            client.BaseAddress = endpoint;
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                if (previous is not null) await previous(context);
                if (context.Result?.Failure is not null) return;
                var token = KyntusSessionValidator.ReadToken(context.HttpContext);
                var validator = context.HttpContext.RequestServices.GetRequiredService<KyntusSessionValidator>();
                if (token is null || !await validator.IsValidAsync(token, context.HttpContext.RequestAborted))
                    context.Fail("Session révoquée ou changement de mot de passe requis.");
            };
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, SessionRevalidationStartupFilter>());
        return services;
    }
}

public sealed class KyntusSessionValidator(HttpClient client)
{
    public async Task<bool> IsValidAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.StatusCode == System.Net.HttpStatusCode.NoContent;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    public static string? ReadToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (AuthenticationHeaderValue.TryParse(authorization, out var header)
            && header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            return header.Parameter;
        if (context.Request.Path.StartsWithSegments("/hubs"))
            return context.Request.Query["access_token"].FirstOrDefault();
        return null;
    }
}

internal sealed class SessionRevalidationStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            var token = KyntusSessionValidator.ReadToken(context);
            if (!context.Request.Path.StartsWithSegments("/hubs") || string.IsNullOrWhiteSpace(token))
            {
                await continuation();
                return;
            }
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var validator = context.RequestServices.GetRequiredService<KyntusSessionValidator>();
            var monitor = MonitorAsync(context, validator, token, cancel.Token);
            try { await continuation(); }
            finally
            {
                cancel.Cancel();
                await monitor;
            }
        });
        next(app);
    };

    private static async Task MonitorAsync(HttpContext context, KyntusSessionValidator validator, string token, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(ct))
                if (!await validator.IsValidAsync(token, ct))
                {
                    context.Abort();
                    return;
                }
        }
        catch (OperationCanceledException) { }
    }
}
