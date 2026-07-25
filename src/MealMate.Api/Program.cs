using MealMate.Api;
using MealMate.Api.Localization;
using MealMate.Api.Middleware;
using MealMate.Application;
using MealMate.Infrastructure;
using MealMate.Persistence;
using MealMate.Persistence.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPersistence(builder.Configuration)
    .AddWebApi(builder.Configuration);

var corsOrigins = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
    ? new[]
    {
        "http://localhost:4200",
        "http://localhost:4201",
        "https://localhost:4200",
        "https://localhost:4201",
    }
    : builder.Configuration.GetSection("Cors:DashboardOrigins").Get<string[]>() ?? [];

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Dashboards", policy =>
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMealMateLocalization();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
if (corsOrigins.Length > 0)
{
    app.UseCors("Dashboards");
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => !registration.Tags.Contains("ready"),
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

if (app.Environment.IsProduction() && app.Configuration.GetValue("Seed:Identity", false))
{
    var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    seedLogger.LogWarning("Seed:Identity is disabled in Production; skipping identity seed.");
}
else if ((app.Environment.IsDevelopment() || app.Configuration.GetValue("Seed:Identity", false))
    && !app.Environment.IsEnvironment("Testing"))
{
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword must be configured when Seed:Identity is enabled outside Development.");
        }

        adminPassword = IdentityDataSeeder.DefaultAdminPassword;
    }

    await IdentityDataSeeder.SeedAsync(app.Services, adminPassword);
}

app.Run();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
