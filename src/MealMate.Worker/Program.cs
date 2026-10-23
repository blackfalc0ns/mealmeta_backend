using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Infrastructure;
using MealMate.Infrastructure.Identity;
using MealMate.Persistence;
using MealMate.Worker.Jobs;
using Serilog;

var supportOnly = args.Contains("--support-only", StringComparer.OrdinalIgnoreCase);
var builderArgs = args.Where(argument => !string.Equals(argument, "--support-only", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = Host.CreateApplicationBuilder(builderArgs);
if (supportOnly) builder.Configuration["BackgroundJobs:OutboxEnabled"] = "false";

var databaseConnectionOverride = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION");
if (!string.IsNullOrWhiteSpace(databaseConnectionOverride))
    builder.Configuration["ConnectionStrings:MealMateDb"] = databaseConnectionOverride;

builder.Services.AddSerilog((services, configuration) =>
    configuration.ReadFrom.Configuration(builder.Configuration));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPersistence(builder.Configuration);

// No HTTP request in the worker: attribute actions to the system principal.
builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);

if (!supportOnly)
{
    builder.Services.AddHostedService<AuthTokenCleanupJob>();
    builder.Services.AddHostedService<CampaignReservationExpirationJob>();
    builder.Services.AddHostedService<AdAuctionSweepJob>();
    builder.Services.AddHostedService<AdPaymentReconciliationJob>();
    builder.Services.AddHostedService<CampaignCheckoutReconciliationJob>();
    builder.Services.AddHostedService<CampaignLifecycleSchedulerJob>();
    builder.Services.AddHostedService<OfferLifecycleSchedulerJob>();
    if (builder.Configuration.GetValue("BackgroundJobs:SubscriptionsEnabled", false))
        builder.Services.AddSubscriptionBackgroundJobs();
}
builder.Services.AddHostedService<SupportTimingWorkerJob>();
builder.Services.AddHostedService<SupportEvidenceCleanupJob>();

var host = builder.Build();
host.Run();
