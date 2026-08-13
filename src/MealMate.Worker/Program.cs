using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Infrastructure;
using MealMate.Infrastructure.Identity;
using MealMate.Persistence;
using MealMate.Worker.Jobs;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHostedService<AuthTokenCleanupJob>();
if (builder.Configuration.GetValue("BackgroundJobs:SubscriptionsEnabled", false))
    builder.Services.AddSubscriptionBackgroundJobs();

var host = builder.Build();
host.Run();
