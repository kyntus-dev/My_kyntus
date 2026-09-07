using System.Text.Json.Serialization;
using Kyntus.Identity.Jwt;
using Microsoft.EntityFrameworkCore;
using Parrainage.Application;
using Parrainage.Infrastructure;
using Parrainage.Infrastructure.Persistence;

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddCors(options =>
{
    options.AddPolicy("devCors", policy =>
        policy
            .WithOrigins("http://localhost:8200", "http://localhost:8201")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
builder.Services.AddParrainageApplication();
builder.Services.AddParrainageInfrastructure(builder.Configuration, isTesting);

var app = builder.Build();

if (isTesting)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetService<ParrainageDbContext>();
    if (db is not null)
        await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("devCors");
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (UnauthorizedAccessException ex)
    {
        if (ctx.Response.HasStarted) throw;
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "parrainage-service",
    status = "running",
}));

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
