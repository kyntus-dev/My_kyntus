using Formation.Application.Commands.CreateFormation;
using Formation.Infrastructure.Messaging;
using Formation.Infrastructure.Persistence;
using Formation.Infrastructure.Repositories;
using Formation.Domain.Interfaces;
using Formation.API.Middlewares;
using Kyntus.Caching;
using Kyntus.Identity.Jwt;
using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// Host Formation.API (projetformationconges) — service formation actif dans docker-compose (formation-backend).
// Ne pas supprimer : c’est le point d’entrée HTTP Formation, pas un stub legacy.

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");

if (string.IsNullOrWhiteSpace(builder.Configuration["JwtSettings:Secret"]))
{
    if (isTesting || builder.Environment.IsDevelopment())
    {
        // Dev/test sans secret injecté — ne pas planter le démarrage.
        builder.Configuration["JwtSettings:Secret"] = "local-dev-formation-jwt-secret-key-32c";
    }
    else
    {
        throw new InvalidOperationException(
            "JwtSettings:Secret manquant pour Formation API — injecter KYNTUS_JWT_SECRET (même secret que Auth).");
    }
}

// DbContext
builder.Services.AddDbContext<FormationDbContext>(opt =>
{
    if (isTesting)
        opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "DataSource=formation_characterization_test.db");
    else
        opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateFormationCommand).Assembly));

// Repositories
builder.Services.AddScoped<IFormationRepository, FormationRepository>();
builder.Services.AddScoped<Formation.Infrastructure.Services.FormationDocumentChecklistService>();
builder.Services.AddScoped<Formation.Infrastructure.Services.LearningCatalogService>();
builder.Services.AddScoped<Formation.Infrastructure.Services.TrainingQuizLibraryService>();
builder.Services.AddScoped<Formation.Infrastructure.Services.TrainingWorkflowService>();
builder.Services.AddKyntusCaching(builder.Configuration);
builder.Services.AddHostedService<Formation.Infrastructure.Services.InitialTrainingMissingDocumentsAlertHostedService>();
builder.Services.AddHostedService<Formation.Infrastructure.Services.CatalogDeadlineReminderHostedService>();

builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanPlanContinue", policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = ctx.User.GetAuthRole();
            if (string.IsNullOrWhiteSpace(role)) return false;
            return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("RH", StringComparison.OrdinalIgnoreCase)
                || KyntusRoleNames.IsChefDeProjet(role)
                || KyntusRoleNames.IsSuperviseur(role)
                || KyntusRoleNames.IsQualiticien(role);
        }));
    options.AddPolicy("CanRecordInitialQuiz", policy =>
        policy.RequireAssertion(ctx =>
        {
            var role = ctx.User.GetAuthRole();
            if (string.IsNullOrWhiteSpace(role)) return false;
            return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Formateur", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Equipe_Formation", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Equipe formation", StringComparison.OrdinalIgnoreCase);
        }));
});

if (isTesting)
{
    builder.Services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
}
else
{
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<FormationOrgAssignmentSyncConsumer>();
    x.AddConsumer<FormationDirectoryEmployeeProjectionConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("formation-org-assignment", e =>
        {
            e.Bind("Kyntus.Messaging.Contracts:OrgAssignmentChangedMessage");
            e.ConfigureConsumer<FormationOrgAssignmentSyncConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("formation-directory-projection", e =>
        {
            e.ConfigureConsumer<FormationDirectoryEmployeeProjectionConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});
}

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 500_000_000;
    o.ValueLengthLimit = int.MaxValue;
});

builder.Services.AddControllers(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Migration automatique au démarrage
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FormationDbContext>();
    var startupLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Formation.Startup");
    if (isTesting)
        await db.Database.EnsureCreatedAsync();
    else
    {
        try
        {
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            startupLog.LogWarning(ex, "EF Migrate partiel — application des patches SQL training.");
        }

        try
        {
            await FormationSchemaPatches.EnsureTrainingWorkflowTablesAsync(db, startupLog);
            await FormationSchemaPatches.EnsureQuizMultiChoiceColumnsAsync(db);
            await FormationSchemaPatches.EnsureEmployeAnnuaireOrgPathAsync(db);
            await FormationSchemaPatches.EnsureLearningCatalogTablesAsync(db, startupLog);
            await FormationSchemaPatches.EnsureQuizLibraryTablesAsync(db, startupLog);
            await FormationSchemaPatches.EnsureCatalogEnrollmentAsync(db, startupLog);
        }
        catch (Exception ex)
        {
            startupLog.LogError(ex, "Impossible de créer les tables training workflow.");
            throw;
        }

        try
        {
            await FormationSchemaPatches.EnsureDocumentChecklistTablesAsync(db, startupLog);
            var checklist = scope.ServiceProvider.GetRequiredService<Formation.Infrastructure.Services.FormationDocumentChecklistService>();
            await checklist.EnsureDefaultDefinitionsAsync();
            var activePaths = await db.InitialTrainingPaths
                .Where(p => p.Status != Formation.Domain.Enums.InitialTrainingStatus.EnProduction
                            && p.Status != Formation.Domain.Enums.InitialTrainingStatus.Rejete)
                .ToListAsync();
            foreach (var path in activePaths)
                await checklist.MaterializeForPathAsync(path);
        }
        catch (Exception ex)
        {
            startupLog.LogWarning(ex, "Checklist documents formation — seed / matérialisation ignorée.");
        }

        try
        {
            await DockerComposeFormationEnrichmentSeed.ApplyIfEnabledAsync(app.Configuration, db, startupLog);
        }
        catch (Exception ex)
        {
            startupLog.LogWarning(ex, "Formation enrichment seed ignoré.");
        }
    }

}

app.Run();

public partial class Program;
