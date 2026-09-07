using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAngular", policy => {
        policy.WithOrigins(
            "http://localhost:8200",
            "http://localhost:8201",
            "http://10.10.10.25:8200",
            "http://10.10.10.25:8201"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

builder.Services.AddOcelot(builder.Configuration).AddPolly();

var app = builder.Build();
app.UseCors("AllowAngular");
app.UseWebSockets();
await app.UseOcelot();
app.Run();