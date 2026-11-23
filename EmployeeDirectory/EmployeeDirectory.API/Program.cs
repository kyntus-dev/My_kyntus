using EmployeeDirectory.Application;
using EmployeeDirectory.API.Middlewares;
using EmployeeDirectory.Infrastructure;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Iam;
using Kyntus.Identity.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddCors(o => o.AddPolicy("lanCors", p => p
    .WithOrigins(
        builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
        ?? ["http://localhost:8200", "http://localhost:8201"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddEmployeeDirectoryApplication();
builder.Services.AddDirectoryInfrastructure(builder.Configuration);
builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
// v60 — Restrictions des administrateurs par le Super Admin (fournisseur local : base de l'annuaire).
builder.Services.AddKyntusAdminRestrictions(builder.Configuration, useDirectoryHttp: false);

var app = builder.Build();

if (app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DirectoryDbContext>().Database.EnsureCreatedAsync();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    try
    {
        await DirectoryDatabaseInitializer.InitializeAsync(app.Services);
    }
    catch (Exception ex)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DirectoryInit");
        log.LogCritical(ex, "Directory init a levé une exception non gérée — démarrage HTTP quand même.");
    }
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        var config = app.Services.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Directory:EnablePrimeBootstrap", false))
            return;

        await Task.Delay(TimeSpan.FromSeconds(5));
        await DirectoryPrimeBootstrap.BootstrapFromPrimeIfNeededAsync(app.Services);
    });
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseCors("lanCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseKyntusAdminRestrictions();
app.MapControllers();

app.Run();

public partial class Program;
