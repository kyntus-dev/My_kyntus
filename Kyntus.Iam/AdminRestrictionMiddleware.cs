using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Kyntus.Iam;

/// <summary>
/// Appels de service à service (le JWT de l'utilisateur est relayé) : marqués d'un jeton dérivé du
/// secret JWT partagé, pour que la restriction d'un administrateur ne s'applique qu'à la requête
/// d'entrée (gateway ou accès direct), jamais aux effets internes d'une action autorisée.
/// Le jeton n'est envoyé qu'aux hôtes internes (noms de service Docker, localhost).
/// </summary>
public static class KyntusInternalHop
{
    public const string HeaderName = "X-Kyntus-Internal-Hop";

    private static volatile string? _token;

    public static void Configure(string? jwtSecret)
    {
        if (string.IsNullOrWhiteSpace(jwtSecret))
            return;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtSecret));
        _token = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("kyntus-internal-hop-v1")));
    }

    public static bool IsInternal(HttpRequest request)
    {
        var token = _token;
        if (token is null || !request.Headers.TryGetValue(HeaderName, out var provided))
            return false;
        var a = Encoding.UTF8.GetBytes(provided.ToString());
        var b = Encoding.UTF8.GetBytes(token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static void Apply(HttpRequestMessage request)
    {
        var token = _token;
        if (token is null || !IsInternalHost(request.RequestUri))
            return;
        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, token);
    }

    private static bool IsInternalHost(Uri? uri) =>
        uri is not null && uri.IsAbsoluteUri && (uri.IsLoopback || !uri.Host.Contains('.'));
}

internal sealed class KyntusInternalHopHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        KyntusInternalHop.Apply(request);
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Ajoute le marqueur interne à tous les HttpClient créés par la fabrique du service.</summary>
internal sealed class KyntusInternalHopHandlerFilter : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, new KyntusInternalHopHandler());
        };
}

/// <summary>
/// Appels vers AuthService (clé interne) : signale que l'action vient du Super Admin. Sans ce signal,
/// AuthService refuse de modifier, désactiver ou supprimer le compte Super Admin.
/// </summary>
public sealed class ActingSuperAdminHeaderHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string HeaderName = "X-Kyntus-Acting-Super-Admin";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(HeaderName);
        if (KyntusAdminClaims.IsSuperAdmin(httpContextAccessor.HttpContext?.User))
            request.Headers.TryAddWithoutValidation(HeaderName, "true");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Applique, à l'entrée de chaque service, les fonctionnalités retirées (403) :
/// <list type="bullet">
/// <item>v60 — à un administrateur, par le Super Admin ;</item>
/// <item>v66 — à tout autre compte, dans « Accès par rôle » ou « Accès par compte » (retraits explicites,
///   compte &gt; rôle). Avant, un droit retiré à un rôle disparaissait du menu mais son API restait
///   ouverte : un appel direct (URL, outil, ancien onglet) passait encore.</item>
/// </list>
/// Sans effet pour le Super Admin, et pour tout compte sans retrait : le comportement historique est
/// strictement conservé.
/// </summary>
public sealed class AdminRestrictionMiddleware(RequestDelegate next, ILogger<AdminRestrictionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true
            || HttpMethods.IsOptions(context.Request.Method)
            || KyntusAdminClaims.IsSuperAdmin(user)
            || KyntusInternalHop.IsInternal(context.Request))
        {
            await next(context);
            return;
        }

        var feature = AdminFeatureRouteMap.Resolve(context.Request.Method, context.Request.Path.Value);
        var subjectId = KyntusAdminClaims.SubjectId(user);
        if (feature is null || subjectId is null)
        {
            await next(context);
            return;
        }

        var provider = context.RequestServices.GetService<IAdminRestrictionProvider>();
        if (provider is null)
        {
            await next(context);
            return;
        }

        var snapshot = await provider.GetAsync(subjectId.Value, context.RequestAborted);
        var admin = KyntusAdminClaims.IsAdmin(user);
        // v66 : les rôles n'ont pas de verbes Supprimer / Exporter — ils suivent Modifier / Consulter.
        var verbe = admin ? feature.Verb : AdminRestrictionRules.VerbePourRole(feature.Verb);
        if (!snapshot.Restricted || feature.Screens.Any(s => !snapshot.IsDenied(s, verbe)))
        {
            await next(context);
            return;
        }

        var action = $"{feature.Screens[0]}.{verbe}";
        logger.LogInformation(
            "Accès refusé (droit retiré) : {Profil} {SubjectId} {Method} {Path} → {Action}",
            admin ? "admin" : KyntusAdminClaims.Role(user), subjectId, context.Request.Method, context.Request.Path.Value, action);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(admin
            ? new
            {
                code = "ADMIN_RESTRICTED",
                action,
                error = "Fonctionnalité retirée par le Super Admin.",
                message = "Cette fonctionnalité vous a été retirée par le Super Admin.",
            }
            : new
            {
                code = "ACCESS_REVOKED",
                action,
                error = "Droit retiré dans « Accès par rôle » ou « Accès par compte ».",
                message = "Cette fonctionnalité vous a été retirée (Accès par rôle / Accès par compte).",
            }, context.RequestAborted);
    }
}

/// <summary>
/// Restrictions lues dans Employee Directory (<c>GET /api/iam/admin-restrictions/me</c>, JWT relayé),
/// gardées quelques secondes en mémoire : un droit rendu ou retiré s'applique sans reconnexion.
/// En cas d'indisponibilité de l'annuaire, l'administrateur garde le comportement historique.
/// </summary>
public sealed class DirectoryHttpAdminRestrictionProvider(
    HttpClient http,
    IMemoryCache cache,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DirectoryHttpAdminRestrictionProvider> logger) : IAdminRestrictionProvider
{
    // v66 : 10 s (au lieu de 15) — un droit retiré s'applique en quelques secondes dans tous les
    // services, sans reconnexion, tout en gardant un appel à l'annuaire au plus toutes les 10 s par compte
    // (le contrôle couvre désormais tous les rôles, pas seulement les administrateurs).
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(5);

    public async Task<AdminRestrictionSnapshot> GetAsync(Guid subjectId, CancellationToken ct = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (KyntusAdminClaims.IsSuperAdmin(user) && KyntusAdminClaims.SubjectId(user) == subjectId)
            return AdminRestrictionSnapshot.SuperAdminSnapshot;
        // Seules les restrictions de l'appelant sont lisibles (JWT relayé) : pas de lecture pour un tiers.
        if (KyntusAdminClaims.SubjectId(user) != subjectId)
            return AdminRestrictionSnapshot.None;

        var key = $"kyntus:admin-restrictions:{subjectId:N}";
        if (cache.TryGetValue(key, out AdminRestrictionSnapshot? cached) && cached is not null)
            return cached;

        try
        {
            var dto = await http.GetFromJsonAsync<AdminRestrictionsMeDto>("api/iam/admin-restrictions/me", ct);
            var snapshot = dto is null
                ? AdminRestrictionSnapshot.None
                : new AdminRestrictionSnapshot(dto.SuperAdmin, dto.Denied ?? []);
            cache.Set(key, snapshot, Ttl);
            return snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Restrictions administrateur indisponibles (annuaire) pour {SubjectId}.", subjectId);
            cache.Set(key, AdminRestrictionSnapshot.None, FailureTtl);
            return AdminRestrictionSnapshot.None;
        }
    }

    private sealed record AdminRestrictionsMeDto(bool SuperAdmin, bool Restricted, IReadOnlyList<string>? Denied);
}

public static class AdminRestrictionExtensions
{
    /// <summary>
    /// Enregistre le contrôle des restrictions administrateur. <paramref name="useDirectoryHttp"/> :
    /// lecture via l'API Employee Directory (tous les services sauf l'annuaire lui-même, qui fournit
    /// son propre <see cref="IAdminRestrictionProvider"/>).
    /// </summary>
    public static IServiceCollection AddKyntusAdminRestrictions(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useDirectoryHttp = true)
    {
        KyntusInternalHop.Configure(configuration["JwtSettings:Secret"]);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, KyntusInternalHopHandlerFilter>());
        services.TryAddTransient<ActingSuperAdminHeaderHandler>();

        if (useDirectoryHttp)
        {
            var baseUrl = configuration["Directory:BaseUrl"]?.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "http://employee-directory-backend:8080";
            services.TryAddTransient<DirectoryForwardAuthorizationHandler>();
            services.AddHttpClient<DirectoryHttpAdminRestrictionProvider>(c =>
                {
                    c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                    c.Timeout = TimeSpan.FromSeconds(2);
                })
                .AddHttpMessageHandler<DirectoryForwardAuthorizationHandler>();
            services.TryAddScoped<IAdminRestrictionProvider>(sp => sp.GetRequiredService<DirectoryHttpAdminRestrictionProvider>());
        }

        return services;
    }

    /// <summary>À placer après UseAuthentication / UseAuthorization.</summary>
    public static IApplicationBuilder UseKyntusAdminRestrictions(this IApplicationBuilder app) =>
        app.UseMiddleware<AdminRestrictionMiddleware>();
}
