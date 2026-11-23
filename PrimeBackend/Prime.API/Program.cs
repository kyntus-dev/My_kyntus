using System.Text.Json.Serialization;
using Kyntus.Identity.Jwt;
using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;
using Prime.API.Middlewares;
using Prime.Application;
using Prime.Infrastructure;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;

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
        policy
            .WithOrigins("http://localhost:8200", "http://localhost:8201", "http://localhost:4207")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
builder.Services.AddPrimeApplication();
builder.Services.AddPrimeInfrastructure(builder.Configuration, isTesting);

// v60 — Restrictions des administrateurs par le Super Admin (contrôle à l'entrée de chaque API).
builder.Services.AddKyntusAdminRestrictions(builder.Configuration);

var app = builder.Build();

if (isTesting)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetService<PrimeDbContext>();
    if (db is not null)
        await db.Database.EnsureCreatedAsync();
}

app.UseCors("devCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseKyntusAdminRestrictions();
app.UseMiddleware<ApiExceptionMiddleware>();
app.MapControllers();
app.Run();

public partial class Program;
