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
builder.Services.AddScoped<IRestaurantOrderNotifier, SignalRRestaurantOrderNotifier>();

if (builder.Configuration.GetValue("BackgroundJobs:SubscriptionsEnabled", true))
    builder.Services.AddSubscriptionBackgroundJobs();

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

var fixedTotpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Totp:FixedCode"]);
var allowFixedTotpOutsideDevelopment =
    builder.Configuration.GetValue("Totp:AllowFixedCodeOutsideDevelopment", false);

if (!builder.Environment.IsDevelopment()
    && fixedTotpConfigured
    && !allowFixedTotpOutsideDevelopment)
{
    throw new InvalidOperationException(
        "Totp:FixedCode outside Development requires Totp:AllowFixedCodeOutsideDevelopment=true.");
}

var corsOrigins = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
    ? new[]
    {
        "http://localhost:4200",
        "http://localhost:4201",
        "http://localhost:4202",
        "http://127.0.0.1:4200",
        "http://127.0.0.1:4201",
        "http://127.0.0.1:4202",
        "https://localhost:4200",
        "https://localhost:4201",
        "https://localhost:4202",
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
app.UseMiddleware<BilingualJsonMiddleware>();
app.UseMiddleware<BilingualJsonResponseMiddleware>();
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

if (args.Any(arg => string.Equals(arg, "--list-db-tables", StringComparison.OrdinalIgnoreCase)))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var tables = new[]
    {
        "Users", "Roles", "Countries", "Regions", "Tenants",
        "Programs", "Bundles", "RestaurantProfiles", "RestaurantBranches",
        "Meals", "Ingredients", "MenuPlans", "MenuPlanVersions",
        "Subscriptions", "SubscriptionDays", "CustomerProfiles", "DriverProfiles",
        "InfluencerProfiles", "InfluencerCommissionCalculations", "InfluencerWithdrawalRequests", "InfluencerPayoutHistories",
        "Campaigns", "CampaignDiscountSplits"
    };

    Console.WriteLine("================ DATABASE ROW COUNTS ================");
    foreach (var table in tables)
    {
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            await db.Database.OpenConnectionAsync();
            var count = await cmd.ExecuteScalarAsync();
            Console.WriteLine($"{table,-35}: {count} rows");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{table,-35}: Error ({ex.Message})");
        }
    }
    Console.WriteLine("=====================================================");
    return;
}

if (args.Any(arg => string.Equals(arg, "--seed-all-data", StringComparison.OrdinalIgnoreCase)))
{
    var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedAllData");
    seedLogger.LogInformation("=================================================================");
    seedLogger.LogInformation("STARTING FULL COMPREHENSIVE DATA SEEDING FOR MEALMATE PLATFORM");
    seedLogger.LogInformation("=================================================================");

    // 1. Identity & Base Roles/Permissions & SuperAdmin
    seedLogger.LogInformation("--> STEP 1: Seeding Identity, Roles, Permissions, and Default SuperAdmins...");
    await IdentityDataSeeder.SeedAsync(app.Services, "Admin!Password123");

    // 2. Kuwait Catalog & Delivery Coverage
    seedLogger.LogInformation("--> STEP 2: Seeding Kuwait Regions & Delivery Coverage...");
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await KuwaitRegionCatalogSeeder.SeedAsync(db, DateTime.UtcNow, CancellationToken.None);
        await RestaurantDeliveryCoverageBackfill.SeedAsync(db);
    }

    // 3. Demo Restaurants
    seedLogger.LogInformation("--> STEP 3: Seeding 20 Demo Restaurants with Kitchens & Zones...");
    await DemoRestaurantDataSeeder.SeedAsync(app.Services, 20);

    // 4. Meal Catalog & Nutrition
    seedLogger.LogInformation("--> STEP 4: Seeding Restaurant Meal Catalog & Nutrition...");
    await DemoRestaurantMealCatalogSeeder.SeedAsync(app.Services);

    // 5. 11-Day Menu Cycle
    seedLogger.LogInformation("--> STEP 5: Seeding 11-Day Menu Cycles...");
    await DemoRestaurantDataSeeder.SeedElevenDayCycleAsync(app.Services);

    // 6. Realistic Subscriptions & Orders & Deliveries
    seedLogger.LogInformation("--> STEP 6: Seeding Realistic Subscriptions, Daily Orders, & Deliveries...");
    await RealisticRestaurantOrdersSeeder.SeedAsync(app.Services);

    // 7. Influencers, Campaigns, Commissions, & Withdrawals Hub
    seedLogger.LogInformation("--> STEP 7: Seeding Influencer Ecosystem, Campaigns, & Withdrawals...");
    await InfluencersAndCampaignsSeeder.SeedAsync(app.Services);

    seedLogger.LogInformation("=================================================================");
    seedLogger.LogInformation("FULL DATA SEEDING COMPLETED SUCCESSFULLY! ALL MODULES POPULATED.");
    seedLogger.LogInformation("=================================================================");
    return;
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
    await RestaurantDeliveryCoverageBackfill.SeedAsync(db);
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

if (args.Any(arg => string.Equals(arg, "--seed-catalog-images", StringComparison.OrdinalIgnoreCase)))
{
    await CatalogImagesSeeder.SeedAsync(app.Services);
    return;
}

if (args.Any(arg => string.Equals(arg, "--seed-demo-meals", StringComparison.OrdinalIgnoreCase)))
{
    await DemoRestaurantMealCatalogSeeder.SeedAsync(app.Services);
    return;
}

if (args.Any(arg => string.Equals(arg, "--seed-eleven-day-cycle", StringComparison.OrdinalIgnoreCase)))
{
    await DemoRestaurantDataSeeder.SeedElevenDayCycleAsync(app.Services);
    return;
}

if (args.Any(arg => string.Equals(arg, "--seed-realistic-orders", StringComparison.OrdinalIgnoreCase)))
{
    await RealisticRestaurantOrdersSeeder.SeedAsync(app.Services);
    return;
}

var createAdminIndex = Array.FindIndex(
    args,
    arg => string.Equals(arg, "--create-admin", StringComparison.OrdinalIgnoreCase));
if (createAdminIndex >= 0)
{
    if (createAdminIndex + 2 >= args.Length)
    {
        Console.Error.WriteLine("Usage: --create-admin <email> <password> [fullName]");
        return;
    }

    var fullName = createAdminIndex + 3 < args.Length ? args[createAdminIndex + 3] : "MealMate Admin";
    await IdentityDataSeeder.CreateSuperAdminAsync(
        app.Services,
        args[createAdminIndex + 1],
        args[createAdminIndex + 2],
        fullName);
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

// Always apply pending EF migrations outside Testing (independent of identity seeding).
// Production previously skipped SeedAsync and therefore never ran MigrateAsync, which
// left FamilyGroups.SelectedFamilyPackageId missing and broke GET .../family-subscription-flow/current.
if (!app.Environment.IsEnvironment("Testing")
    && app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    using var migrateScope = app.Services.CreateScope();
    var db = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var migrateLogger = migrateScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    try
    {
        migrateLogger.LogInformation("Applying pending EF Core migrations on startup...");
        await db.Database.MigrateAsync();
        migrateLogger.LogInformation("EF Core migrations applied.");
        try
        {
            migrateLogger.LogInformation("Updating Kuwait region centroids and restaurant delivery coverage...");
            await KuwaitRegionCatalogSeeder.SeedAsync(db, DateTime.UtcNow, CancellationToken.None);
            await RestaurantDeliveryCoverageBackfill.SeedAsync(db, migrateLogger);
            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""FamilyMemberAccesses"" SET ""HasSeparateAccount"" = false WHERE ""InviteTokenHash"" IS NULL;");
            migrateLogger.LogInformation("Region centroids and restaurant coverage backfill applied.");
        }
        catch (Exception ex)
        {
            migrateLogger.LogWarning(ex, "Region/coverage backfill failed; API will continue without updated centroids.");
        }

        try
        {
            await MyFatoorahGatewayBootstrap.EnsureConfiguredAsync(
                migrateScope.ServiceProvider,
                app.Configuration,
                migrateLogger);
        }
        catch (Exception ex)
        {
            migrateLogger.LogWarning(ex, "MyFatoorah gateway bootstrap failed; payment methods may remain unavailable.");
        }
    }
    catch (Exception ex)
    {
        migrateLogger.LogCritical(
            ex,
            "Startup aborted: EF Core MigrateAsync failed. Check ConnectionStrings:MealMateDb / MEALMATE_DB_CONNECTION.");
        throw;
    }
}

if (app.Environment.IsProduction() && app.Configuration.GetValue("Seed:Identity", false))
{
    var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    seedLogger.LogWarning("Seed:Identity is true in Production but identity seed is blocked; migrations already applied above.");
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

    if (app.Environment.IsDevelopment())
    {
        var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        seedLogger.LogInformation("Scheduling identity/catalog seed in the background so the API can accept requests immediately.");
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await IdentityDataSeeder.SeedAsync(app.Services, adminPassword);
                    seedLogger.LogInformation("Background identity/catalog seed completed.");
                }
                catch (Exception ex)
                {
                    seedLogger.LogError(ex, "Background identity/catalog seed failed.");
                }
            });
        });
    }
    else
    {
        // SeedAsync also calls MigrateAsync; safe/idempotent if already applied above.
        await IdentityDataSeeder.SeedAsync(app.Services, adminPassword);
    }
}

app.Run();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
