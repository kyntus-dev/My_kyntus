using System.Net;
using System.Text.Json;
using Documentation.Infrastructure.Persistence;
using Documentation.Infrastructure.Services;
using Kyntus.Identity.Jwt;

namespace Documentation.Infrastructure.Context;

/// <summary>
/// Remplit <see cref="DocumentationUserContext"/> depuis le JWT (email → annuaire) ou, en développement, depuis les en-têtes legacy.
/// </summary>
public sealed class DocumentationUserContextMiddleware(
    RequestDelegate next,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly bool _allowHeaderContextFallback =
        configuration.GetValue("Documentation:AllowHeaderContextFallback", false);

    public async Task InvokeAsync(
        HttpContext httpContext,
        DocumentationUserContext userContext,
        DocumentationDirectoryUserLookup directoryLookup,
        IHostEnvironment hostEnvironment,
        ILogger<DocumentationUserContextMiddleware> logger)
    {
        if (HttpMethods.IsOptions(httpContext.Request.Method))
        {
            await next(httpContext);
            return;
        }

        var path = httpContext.Request.Path.Value ?? "";
        if (DocumentationTechnicalPaths.BypassesUserContext(path))
        {
            await next(httpContext);
            return;
        }

        if (!RequiresDocumentationIdentity(path))
        {
            await next(httpContext);
            return;
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var email = httpContext.User.GetEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                await WriteJsonErrorAsync(httpContext, HttpStatusCode.Unauthorized, "Email absent du jeton JWT.");
                return;
            }

            var tenantId = DocumentationTenantAccessor.ResolveConfiguredDefault(configuration);
            var directoryUser = await directoryLookup.ResolveAsync(
                httpContext.User,
                tenantId,
                httpContext.RequestAborted);

            if (directoryUser is null)
            {
                logger.LogWarning("Annuaire documentation : aucun utilisateur pour {Email} (tenant {Tenant})", email, tenantId);
                await WriteJsonErrorAsync(
                    httpContext,
                    HttpStatusCode.Forbidden,
                    $"Aucun profil documentation pour « {email} ». Vérifiez l’annuaire (tenant {tenantId}).");
                return;
            }

            userContext.ApplyFromDirectoryUser(directoryUser, tenantId);
            if (!await TryContinueForDataPathAsync(httpContext, userContext, path))
                return;
            await next(httpContext);
            return;
        }

        if (hostEnvironment.IsDevelopment() || _allowHeaderContextFallback)
        {
            userContext.LoadFromHeaders(httpContext.Request.Headers, hostEnvironment);
            if (!await TryContinueForDataPathAsync(httpContext, userContext, path))
                return;
            await next(httpContext);
            return;
        }

        if (path.StartsWith("/api/documentation/data", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonErrorAsync(
                httpContext,
                HttpStatusCode.Unauthorized,
                "Authentification JWT requise pour l’API documentation.");
            return;
        }

        await next(httpContext);
    }

    private static async Task<bool> TryContinueForDataPathAsync(
        HttpContext httpContext,
        DocumentationUserContext userContext,
        string path)
    {
        if (!path.StartsWith("/api/documentation/data", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(userContext.ValidationError))
        {
            await WriteJsonErrorAsync(httpContext, HttpStatusCode.BadRequest, userContext.ValidationError);
            return false;
        }

        if (!userContext.IsComplete)
        {
            await WriteJsonErrorAsync(
                httpContext,
                HttpStatusCode.Unauthorized,
                "Contexte utilisateur documentation incomplet.");
            return false;
        }

        return true;
    }

    private static bool RequiresDocumentationIdentity(string path) =>
        path.StartsWith("/api/documentation", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/generate-document-ai", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteJsonErrorAsync(HttpContext httpContext, HttpStatusCode status, string message)
    {
        httpContext.Response.StatusCode = (int)status;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new { message }, Json));
    }
}
