using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kyntus.Identity.Jwt;

public static class KyntusJwtExtensions
{
    public static IServiceCollection AddKyntusJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"]
            ?? throw new InvalidOperationException("JwtSettings:Secret manquant.");
        var issuer = jwtSettings["Issuer"] ?? "AuthService";
        var audience = jwtSettings["Audience"] ?? "AuthServiceClient";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Garder "sub" / types courts tels quels (sinon "sub" → NameIdentifier et GetSubjectId casse).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddKyntusSessionValidation(configuration);
        services.AddAuthorization();
        return services;
    }
}

public static class KyntusClaimsPrincipalExtensions
{
    public static string? GetEmail(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;
        return principal.FindFirstValue(ClaimTypes.Email)?.Trim()
            ?? principal.FindFirstValue("email")?.Trim();
    }

    public static string? GetAuthRole(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;
        return principal.FindFirstValue(ClaimTypes.Role)?.Trim()
            ?? principal.FindFirstValue("role")?.Trim();
    }

    public static Guid? GetSubjectId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        foreach (var raw in EnumerateSubjectCandidates(principal))
        {
            if (Guid.TryParse(raw, out var g) && g != Guid.Empty)
                return g;
        }

        return null;
    }

    public static int? GetAuthUserId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        // « sub » peut être remappé vers NameIdentifier (GUID) : ne pas le parser en int.
        foreach (var claim in principal.FindAll(ClaimTypes.NameIdentifier))
        {
            if (int.TryParse(claim.Value, out var id) && id > 0)
                return id;
        }

        var raw = principal.FindFirstValue("nameid");
        return int.TryParse(raw, out var named) && named > 0 ? named : null;
    }

    private static IEnumerable<string> EnumerateSubjectCandidates(ClaimsPrincipal principal)
    {
        // JWT "sub" (prévu) — parfois remappé vers NameIdentifier si MapInboundClaims=true.
        foreach (var claim in principal.FindAll("sub"))
        {
            if (!string.IsNullOrWhiteSpace(claim.Value))
                yield return claim.Value.Trim();
        }

        foreach (var claim in principal.FindAll(ClaimTypes.NameIdentifier))
        {
            // Auth met aussi l'id numérique en NameIdentifier : on ne garde que les GUID.
            if (!string.IsNullOrWhiteSpace(claim.Value) && claim.Value.Contains('-'))
                yield return claim.Value.Trim();
        }
    }
}

