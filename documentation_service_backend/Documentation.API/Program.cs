using System.Data;
using System.Text.Json.Serialization;
using Documentation.Application;
using Documentation.Infrastructure;
using Documentation.Infrastructure.Context;
using Documentation.Infrastructure.Persistence;
using Documentation.Infrastructure.Services;
using Documentation.API.Middleware;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");

builder.Services.AddControllers(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
}).AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("devCors", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            policy.WithOrigins("http://localhost:8200");
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(
                "X-Document-Required-Total",
                "X-Document-Missing-Count",
                "X-Document-Filled-Count",
                "X-Document-Filled-Percent",
                "X-Document-Missing-Variables",
                "X-Document-Invalid-Count");
    });
});

builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
builder.Services.AddDocumentationApplication();
builder.Services.AddDocumentationInfrastructure(builder.Configuration, isTesting);

var app = builder.Build();

if (!isTesting && app.Configuration.GetValue("Documentation:ApplyBootstrapSchema", false))
{
    await using var bootstrapScope = app.Services.CreateAsyncScope();
    var bootstrapLog = bootstrapScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Documentation.SchemaBootstrap");
    var db = bootstrapScope.ServiceProvider.GetRequiredService<DocumentationDbContext>();
    try
    {
        var created = await db.Database.EnsureCreatedAsync();
        bootstrapLog.LogInformation("Documentation:ApplyBootstrapSchema=true — EnsureCreated returned {Created}", created);
    }
    catch (Exception ex)
    {
        bootstrapLog.LogCritical(ex, "Documentation:ApplyBootstrapSchema — EnsureCreated failed");
        throw;
    }

    try
    {
        await DocumentationSchemaBootstrap.ApplyPostSchemaObjectsAsync(db, bootstrapLog);
    }
    catch (Exception ex)
    {
        bootstrapLog.LogWarning(ex, "Documentation:ApplyBootstrapSchema — fonction REQ non appliquée (non bloquant).");
    }
}

if (!isTesting && app.Configuration.GetValue("Documentation:ApplyPostSchemaObjects", true))
{
    await using var postSchemaScope = app.Services.CreateAsyncScope();
    var postSchemaLog = postSchemaScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Documentation.SchemaBootstrap");
    var postSchemaDb = postSchemaScope.ServiceProvider.GetRequiredService<DocumentationDbContext>();
    try
    {
        await DocumentationSchemaBootstrap.ApplyPostSchemaObjectsAsync(postSchemaDb, postSchemaLog);
    }
    catch (Exception ex)
    {
        postSchemaLog.LogWarning(ex, "Documentation:ApplyPostSchemaObjects — fonction REQ non appliquée (schéma absent ?).");
    }
}

if (!isTesting)
{
    await using var enrichScope = app.Services.CreateAsyncScope();
    var enrichLog = enrichScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Documentation.EnrichmentSeed");
    var enrichDb = enrichScope.ServiceProvider.GetRequiredService<DocumentationDbContext>();
    try
    {
        await DockerDocumentationEnrichmentSeed.ApplyIfEnabledAsync(app.Configuration, enrichDb, enrichLog);
    }
    catch (Exception ex)
    {
        enrichLog.LogWarning(ex, "Documentation enrichment seed ignoré.");
    }
}

app.UseMiddleware<UnhandledExceptionMiddleware>();
app.UseCors("devCors");
app.UseAuthentication();
app.UseAuthorization();
if (!isTesting)
{
    app.UseMiddleware<DocumentationCorrelationMiddleware>();
    app.UseMiddleware<DocumentationUserContextMiddleware>();
}

app.MapGet("/health", () => Results.Json(new { status = "Healthy", service = "documentation" }));
app.MapGet("/healthz", () => Results.Json(new { status = "Healthy", service = "documentation" }));
app.MapGet("/ready", () => Results.Json(new { status = "Ready", service = "documentation" }));
app.MapGet("/api/documentation/health", () => Results.Json(new { status = "Healthy", service = "documentation" }));

app.MapGet("/", () => Results.Json(new
{
    service = "DocumentationBackend",
    message = "API opérationnelle. Il n’y a pas de page HTML ici.",
    tryThese = new[] { "/health", "/api/documentation/db/status", "/api/documentation/health" },
}));

app.MapControllers();

if (!isTesting)
{
app.MapGet("/api/documentation/db/status", async (
    DocumentationDbContext db,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    CancellationToken ct) =>
{
    var cs = configuration.GetConnectionString("Documentation") ?? "";
    var csb = new NpgsqlConnectionStringBuilder(cs);
    var statusPwd = configuration["DocumentationDb:Password"];
    if (!string.IsNullOrEmpty(statusPwd))
        csb.Password = statusPwd;

    try
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        try
        {
            var documentTypeCount = await db.DocumentTypes.CountAsync(ct);
            var documentRequestCount = await db.DocumentRequests.CountAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT current_database()";
            var serverDbName = (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "";
            cmd.CommandText = "SELECT count(*)::bigint FROM documentation.document_requests";
            var documentRequestTotalAllTenants = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            return Results.Ok(new
            {
                connected = true,
                schema = "documentation",
                serverDatabase = serverDbName,
                configuredHost = csb.Host,
                configuredPort = csb.Port,
                configuredDatabase = csb.Database,
                documentTypeCount,
                documentRequestCount,
                documentRequestTotalAllTenants,
                hint =
                    "Comparer serverDatabase / host / port avec la connexion pgAdmin. " +
                    "documentRequestCount suit le filtre tenant courant ; documentRequestTotalAllTenants compte toutes les lignes.",
            });
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
    catch (Exception ex)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return Results.Ok(new
            {
                connected = false,
                schema = "documentation",
                message = "Impossible de joindre PostgreSQL.",
            });
        }

        return Results.Ok(new
        {
            connected = false,
            schema = "documentation",
            message = "Impossible de joindre PostgreSQL.",
            errorType = ex.GetType().Name,
            errorMessage = ex.Message,
            host = csb.Host,
            port = csb.Port,
            database = csb.Database,
            username = csb.Username,
            passwordConfigured = !string.IsNullOrEmpty(csb.Password),
            passwordFromDocumentationDbKey = !string.IsNullOrEmpty(configuration["DocumentationDb:Password"]),
            hint = "28P01 = mot de passe refusé par PostgreSQL. Si le mot de passe contient des caractères spéciaux, placez-le dans DocumentationDb:Password (JSON). Sinon alignez le mot de passe : ALTER USER postgres WITH PASSWORD 'votre_mot_de_passe'; (en superutilisateur).",
        });
    }
});
}

app.Run();

public partial class Program;
