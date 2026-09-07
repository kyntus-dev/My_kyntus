using Conge.Application.Abstractions;
using Conge.Application.Behaviors;
using Conge.Application.Contracts;
using Conge.Application.Services;
using Conge.Domain.Interfaces;
using Conge.Infrastructure.Data;
using Conge.Infrastructure.Messaging;
using Conge.Infrastructure.Messaging.Consumers;
using Conge.Infrastructure.Messaging.Publishers;
using Conge.Infrastructure.Persistence;
using Conge.Infrastructure.Persistence.Repositories;
using Conge.Infrastructure.Services;
using FluentValidation;
using Kyntus.Caching;
using Kyntus.Identity.Jwt;
using Kyntus.Iam;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var isTesting = builder.Environment.IsEnvironment("Testing");

// ?? Base de donn�es PostgreSQL ??????????????????????????????????????????????
builder.Services.AddDbContext<CongeDbContext>(options =>
{
    if (isTesting)
        options.UseSqlite(builder.Configuration.GetConnectionString("CongeDb")
            ?? "DataSource=conge_characterization_test.db");
    else
        options.UseNpgsql(builder.Configuration.GetConnectionString("CongeDb"));
});

// ?? Repositories ????????????????????????????????????????????????????????????
builder.Services.AddScoped<IDemandeCongeRepository, DemandeCongeRepository>();
builder.Services.AddScoped<ISoldeCongeRepository, SoldeCongeRepository>();
builder.Services.AddScoped<IEmployeSnapshotRepository, EmployeSnapshotRepository>();
builder.Services.AddScoped<IPeriodeInterditeRepository, PeriodeInterditeRepository>();
builder.Services.AddScoped<IQuotaCongeServiceRepository, QuotaCongeServiceRepository>();
builder.Services.AddScoped<IOrgNodeCongeRepository, OrgNodeCongeRepository>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CongeDbContext>());
builder.Services.AddScoped<Conge.Application.Services.CongeReglesService>();
builder.Services.AddScoped<Conge.Application.Services.CongeValidationFinalizer>();
builder.Services.AddScoped<ILeaveWorkflowPolicy, DirectoryLeaveWorkflowPolicy>();

var directoryBaseUrl = builder.Configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080";
builder.Services.AddKyntusJwtAuthentication(builder.Configuration);
builder.Services.AddKyntusIamViaDirectoryHttp(directoryBaseUrl);

builder.Services.AddKyntusCaching(builder.Configuration);
builder.Services.AddHttpClient("directory-org", c =>
{
    c.BaseAddress = new Uri(directoryBaseUrl.TrimEnd('/') + "/");
}).AddHttpMessageHandler<DirectoryForwardAuthorizationHandler>();
builder.Services.AddScoped<DirectoryOrgCatalog>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("directory-org");
    return new DirectoryOrgCatalog(
        http,
        sp.GetRequiredService<IOrgNodeCongeRepository>(),
        sp.GetRequiredService<ICacheService>(),
        sp.GetRequiredService<ILogger<DirectoryOrgCatalog>>());
});
builder.Services.AddScoped<IDirectoryOrgCatalog>(sp => sp.GetRequiredService<DirectoryOrgCatalog>());
builder.Services.AddScoped<IEmployeSnapshotEnsure, EmployeSnapshotEnsure>();
if (isTesting)
{
    builder.Services.AddSingleton<IDirectoryEmployeeLookup, NullDirectoryEmployeeLookup>();
}
else
{
    builder.Services.AddScoped<IDirectoryEmployeeLookup>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("directory-org");
        return new DirectoryEmployeeLookup(
            http,
            sp.GetRequiredService<ILogger<DirectoryEmployeeLookup>>());
    });
}
if (!isTesting)
    builder.Services.AddHostedService<CongeDirectoryOrgBootstrap>();

// ?? MediatR + FluentValidation ???????????????????????????????????????????????
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(ValidationBehavior<,>).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(DemanderCongeValidator).Assembly);

// ?? MassTransit + RabbitMQ ???????????????????????????????????????????????????
if (isTesting)
{
    builder.Services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
}
else
{
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SoldeAnnuelInitialiseConsumer>();
    x.AddConsumer<EmployeCreatedConsumer>();
    x.AddConsumer<EmployeUpdatedConsumer>();
    x.AddConsumer<OrgAssignmentCongeSyncConsumer>();
    x.AddConsumer<DirectoryEmployeeCongeProjectionConsumer>();
    x.AddConsumer<DirectoryEmployeeHrProfileCongeProjectionConsumer>();
    x.AddConsumer<DirectoryOrgNodeCongeProjectionConsumer>();
    x.AddConsumer<DirectoryAssignmentCongeSyncConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("conge-solde-annuel", e =>
        {
            e.Bind("Kyntus.Messaging.Contracts:SoldeAnnuelInitialiseMessage");
            e.ConfigureConsumer<SoldeAnnuelInitialiseConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-employe-created", e =>
        {
            e.ConfigureConsumer<EmployeCreatedConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-employe-updated", e =>
        {
            e.ConfigureConsumer<EmployeUpdatedConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-org-assignment", e =>
        {
            e.Bind("Kyntus.Messaging.Contracts:OrgAssignmentChangedMessage");
            e.ConfigureConsumer<OrgAssignmentCongeSyncConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-directory-projection", e =>
        {
            e.ConfigureConsumer<DirectoryEmployeeCongeProjectionConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-directory-hr-profile", e =>
        {
            e.ConfigureConsumer<DirectoryEmployeeHrProfileCongeProjectionConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-directory-org-nodes", e =>
        {
            e.ConfigureConsumer<DirectoryOrgNodeCongeProjectionConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("conge-directory-assignments", e =>
        {
            e.ConfigureConsumer<DirectoryAssignmentCongeSyncConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});
}

// ?? Publisher ????????????????????????????????????????????????????????????????
builder.Services.AddScoped<ICongeEventPublisher, CongeEventPublisher>();

// ?? CORS ?????????????????????????????????????????????????????????????????????
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .WithOrigins("http://localhost:8200", "http://localhost:8201")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// ?? API ???????????????????????????????????????????????????????????????????????
builder.Services.AddControllers(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Conge API", Version = "v1" });
});

var app = builder.Build();

// ?? Migrations automatiques au d�marrage ?????????????????????????????????????
if (isTesting)
{
    using var testScope = app.Services.CreateScope();
    var testDb = testScope.ServiceProvider.GetRequiredService<CongeDbContext>();
    await testDb.Database.EnsureCreatedAsync();
}
else
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CongeDbContext>();
    await db.Database.MigrateAsync();
    var seedLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Conge.EnrichmentSeed");
    try
    {
        await CongeSchemaPatches.ApplyAsync(db, seedLog);
        await DockerComposeCongeEnrichmentSeed.ApplyIfEnabledAsync(app.Configuration, db, seedLog);
    }
    catch (Exception ex)
    {
        seedLog.LogWarning(ex, "Conge enrichment seed ignoré.");
    }
}

// ?? Middleware pipeline ???????????????????????????????????????????????????????
app.UseMiddleware<Conge.API.Middlewares.ExceptionMiddleware>();

// ? CORS � doit �tre avant UseRouting et MapControllers
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ? HttpsRedirection d�sactiv� � cause des conflits derri�re Docker/Ocelot
// app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "conge" }));

app.Run();

public partial class Program;
