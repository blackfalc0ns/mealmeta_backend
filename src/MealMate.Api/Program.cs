using MealMate.Api;
using MealMate.Api.Configuration;
using MealMate.Api.Localization;
using MealMate.Api.Middleware;
using MealMate.Api.Realtime;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Infrastructure;
using MealMate.Persistence;
using MealMate.Persistence.Seeding;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Command-line maintenance tasks (migrations/seeders) can target an explicit
// database without changing appsettings files. Hosting configuration still wins
// unless this variable is deliberately supplied.
var databaseConnectionOverride = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION");
if (!string.IsNullOrWhiteSpace(databaseConnectionOverride))
{
    builder.Configuration["ConnectionStrings:MealMateDb"] = databaseConnectionOverride;
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPersistence(builder.Configuration)
    .AddWebApi(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddScoped<IRegistrationReviewNotifier, SignalRRegistrationReviewNotifier>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ForwardedHeadersConfiguration.Configure(builder.Configuration, options));

var fixedOtpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Otp:FixedCode"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["Otp:WhatsAppFixedCode"]);
var allowFixedOtpOutsideDevelopment =
    builder.Configuration.GetValue("Otp:AllowFixedCodeOutsideDevelopment", false);

if (!builder.Environment.IsDevelopment()
    && fixedOtpConfigured
    && !allowFixedOtpOutsideDevelopment)
{
    throw new InvalidOperationException(
        "Otp:FixedCode and Otp:WhatsAppFixedCode outside Development require Otp:AllowFixedCodeOutsideDevelopment=true.");
}

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

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMealMateLocalization();
app.UseExceptionHandler();
app.UseStatusCodePages();

var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Swagger:Enabled", false);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (args.Any(arg => string.Equals(arg, "--seed-kuwait-catalog", StringComparison.OrdinalIgnoreCase)))
{
    await using var catalogScope = app.Services.CreateAsyncScope();
    var db = catalogScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var now = DateTime.UtcNow;
    var country = await db.Countries.FirstOrDefaultAsync(x => x.Code == "KW");
    if (country is null)
    {
        country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", CurrencyCode = "KWD", DialCode = "+965", FlagEmoji = "🇰🇼", IsActive = true, CreatedAtUtc = now };
        db.Countries.Add(country);
    }
    else
    {
        country.IsActive = true;
        country.DialCode = "+965";
        country.FlagEmoji = "🇰🇼";
    }

    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Code == "TEN-KW");
    if (tenant is null)
    {
        tenant = new Tenant { Code = "TEN-KW", NameAr = "ميل ميت الكويت", NameEn = "MealMate Kuwait", CountryId = country.Id, Domain = "kw.mealmate.app", CurrencyCode = "KWD", TimeZoneId = "Asia/Kuwait", DefaultLanguage = "ar", Status = TenantStatus.Active, IsActive = true, CreatedAtUtc = now };
        db.Tenants.Add(tenant);
    }
    else
    {
        tenant.CountryId = country.Id;
        tenant.Status = TenantStatus.Active;
        tenant.IsActive = true;
    }

    await db.SaveChangesAsync();
    await KuwaitRegionCatalogSeeder.SeedAsync(db, now, CancellationToken.None);
    return;
}

var demoSeedIndex = Array.FindIndex(args, arg => string.Equals(arg, "--seed-demo-restaurants", StringComparison.OrdinalIgnoreCase));
if (demoSeedIndex >= 0)
{
    var count = demoSeedIndex + 1 < args.Length && int.TryParse(args[demoSeedIndex + 1], out var parsedCount)
        ? parsedCount
        : 50;
    await DemoRestaurantDataSeeder.SeedAsync(app.Services, count);
    return;
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
app.MapHub<RestaurantReviewHub>(RestaurantReviewHub.HubPath);
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
