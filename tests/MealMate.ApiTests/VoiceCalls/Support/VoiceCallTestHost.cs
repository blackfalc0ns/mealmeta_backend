using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;

using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;
using DomainProgram = MealMate.Domain.ProgramsSubscriptions.Program;

namespace MealMate.ApiTests.VoiceCalls.Support;

/// <summary>
/// Isolated HTTP test host for the voice-calling system: disposable PostgreSQL 16,
/// production DI (AddPersistence with retry, MediatR pipeline, real JWT identity),
/// and a seeded delivery context (customer → subscription day → trip → stop → active
/// driver assignment). Test-only fault adapters live here; they never leak into
/// production registrations.
/// </summary>
public sealed class VoiceCallTestHost : IAsyncDisposable
{
    private PostgreSqlContainer _container = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private VoiceCallTestSeed _seed = new();
    private IReadOnlyDictionary<string, string?>? _extraSettings;

    public VoiceCallTestDateTimeProvider Clock { get; } = new();
    public TestVoiceCallPushNotifier PushNotifier { get; } = new();

    public string ConnectionString => _container.GetConnectionString();
    public Guid TripStopId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid SubscriptionDayId { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid DriverUserId { get; private set; }
    public Guid DriverProfileId { get; private set; }
    public Guid CustomerUserId { get; private set; }
    public Guid OutsiderUserId { get; private set; }
    public Guid DispatcherUserId { get; private set; }

    /// <summary>Device-session ids captured by RegisterDeviceAsync, keyed by installation.</summary>
    public System.Collections.Generic.Dictionary<string, Guid> RegisteredDeviceSessions { get; } = new();

    /// <summary>Root service provider of the hosted API for scope-based pipeline tests.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>Phone number seeded on the TripStop row for privacy regression checks (test data only).</summary>
    public string SeededCustomerPhone { get; } = "+96551110001";

    public HttpClient DriverClient { get; private set; } = null!;
    public HttpClient CustomerClient { get; private set; } = null!;
    public HttpClient OutsiderClient { get; private set; } = null!;

    /// <summary>JWT-bearing clients intended for device-scoped calls. Device session
    /// registration/proof attachment lands with T06; until then they behave like
    /// plain account clients.</summary>
    public HttpClient DriverDeviceA { get; private set; } = null!;
    public HttpClient CustomerDeviceA { get; private set; } = null!;
    public HttpClient CustomerDeviceB { get; private set; } = null!;

    public static async Task<VoiceCallTestHost> CreateAsync(VoiceCallTestSeed? seed = null, IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var host = new VoiceCallTestHost();
        if (seed is not null)
        {
            host._seed = seed;
        }

        if (extraSettings is not null)
        {
            host._extraSettings = extraSettings;
        }

        await host.InitializeAsync();
        return host;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using (var db = CreateDbContext())
            {
                await db.Database.MigrateAsync();
            }

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureLogging(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Information));
                builder.UseSetting("ConnectionStrings:MealMateDb", _container.GetConnectionString());
                builder.UseSetting("Seed:Identity", "false");
                builder.UseSetting("Email:SecretsEncryptionKey", Convert.ToBase64String(new byte[32]));
                if (_extraSettings is not null)
                {
                    foreach (var (key, value) in _extraSettings)
                    {
                        builder.UseSetting(key, value);
                    }
                }
                builder.ConfigureTestServices(services =>
                {
                    // Keep the container connection local to this host and mirror the
                    // production retry configuration toggle.
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                    services.AddDbContext<AppDbContext>((provider, dbOptions) =>
                    {
                        dbOptions.UseNpgsql(_container.GetConnectionString(), npgsql =>
                        {
                            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                            if (_seed.EnableDatabaseRetries)
                            {
                                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                            }
                        });
                        dbOptions.AddInterceptors(provider.GetRequiredService<MealMate.Persistence.Interceptors.AuditableEntityInterceptor>());
                    });

                    // Deterministic outbox/recovery ticking driven by test steps.
                    var hostedServiceDescriptors = services
                        .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                        .ToList();
                    foreach (var descriptor in hostedServiceDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    var pushDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallPushNotifier));
                    if (pushDescriptor != null)
                    {
                        services.Remove(pushDescriptor);
                    }

                    services.AddSingleton<MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallPushNotifier>(PushNotifier);
                });
            });

            await SeedDeliveryContextAsync();

            DriverClient = CreateClientForUser(DriverUserId, UserType.Driver, [AppRoles.Driver]);
            CustomerClient = CreateClientForUser(CustomerUserId, UserType.Customer, [AppRoles.Customer]);
            OutsiderClient = CreateClientForUser(OutsiderUserId, UserType.Customer, [AppRoles.Customer]);
            DriverDeviceA = CreateClientForUser(DriverUserId, UserType.Driver, [AppRoles.Driver]);
            CustomerDeviceA = CreateClientForUser(CustomerUserId, UserType.Customer, [AppRoles.Customer]);
            CustomerDeviceB = CreateClientForUser(CustomerUserId, UserType.Customer, [AppRoles.Customer]);
        }
        catch (Exception ex)
        {
            throw new XunitException("Failed to initialize the voice-call PostgreSQL container or test host.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    public async Task<AppDbContext> OpenDbAsync()
    {
        var db = CreateDbContext();
        await Task.CompletedTask;
        return db;
    }

    public HttpClient CreateClientForUser(Guid userId, UserType userType, IReadOnlyList<string> roles)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokenService.CreateAccessToken(new TokenUser(userId, userType, roles, []));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    /// <summary>HTTP client without an Authorization header (anonymous probes).</summary>
    public HttpClient CreateAnonymousClient() => _factory.CreateClient();

    /// <summary>Seeded delivery manager bound to the operational restaurant.</summary>
    public HttpClient CreateDispatcherClient()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokenService.CreateAccessToken(new TokenUser(
            DispatcherUserId,
            Domain.Enums.UserType.DeliveryManager,
            [AppRoles.DeliveryManager],
            [],
            RestaurantId));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    /// <summary>
    /// Registers a device-control session through the real v2 route and attaches the
    /// session/proof headers to the client — the same way a real device would.
    /// </summary>
    public async Task<(Guid DeviceSessionId, string ControlProof)> RegisterDeviceAsync(
        HttpClient client,
        string installationId,
        Guid? fcmDeviceTokenId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/voice-calls/devices/register",
            new { installationId, platform = "android", capabilityVersion = 2, fcmDeviceTokenId });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var sessionId = body.GetProperty("deviceSessionId").GetGuid();
        var proof = body.GetProperty("controlProof").GetString()!;

        client.DefaultRequestHeaders.Remove("X-Voice-Device-Session");
        client.DefaultRequestHeaders.Remove("X-Voice-Device-Proof");
        client.DefaultRequestHeaders.Add("X-Voice-Device-Session", sessionId.ToString());
        client.DefaultRequestHeaders.Add("X-Voice-Device-Proof", proof);

        RegisteredDeviceSessions[installationId] = sessionId;
        return (sessionId, proof);
    }

    public Task<Guid> CreateRingingCallAsync()
        => throw new NotSupportedException("CreateRingingCallAsync is implemented with the v2 initiation route in T08; do not use it in earlier baselines.");

    public Task<Guid> CreateConnectingCallAsync()
        => throw new NotSupportedException("CreateConnectingCallAsync is implemented with the v2 answer/report routes in T09; do not use it in earlier baselines.");

    public Task<Guid> CreateActiveCallAsync()
        => throw new NotSupportedException("CreateActiveCallAsync is implemented with the v2 answer/report routes in T09; do not use it in earlier baselines.");

    public Task RunRecoveryAsync()
        => throw new NotSupportedException("RunRecoveryAsync is implemented with the conditional recovery coordinator in T11; do not use it in earlier baselines.");

    /// <summary>
    /// Runs the REAL outbox dispatcher (lease → publish → complete/retry) over staged
    /// messages — the same code path the background worker uses.
    /// </summary>
    public async Task RunOutboxAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<MealMate.Application.Common.Outbox.IOutboxService>();
        await outbox.ProcessPendingMessagesAsync();
    }

    public Task<Guid> CompleteQualifiedMissedAttemptAsync()
        => throw new NotSupportedException("CompleteQualifiedMissedAttemptAsync is implemented with the contact-attempt workflow in T14; do not use it in earlier baselines.");

    private async Task SeedDeliveryContextAsync()
    {
        var startDate = DateOnly.FromDateTime(Clock.UtcNow);
        var entities = await OrderLifecycleTestDataSeeder.SeedAsync(CreateDbContext(), startDate);

        RestaurantId = entities.RestaurantA.Id;
        DriverUserId = entities.DriverUserA.Id;
        DispatcherUserId = entities.DispatcherA.Id;
        DriverProfileId = entities.DriverProfileA.Id;
        CustomerUserId = entities.CustomerUser.Id;

        await using var db = CreateDbContext();

        var subscription = new Subscription
        {
            CustomerUserId = CustomerUserId,
            ProgramId = entities.Program.Id,
            BundleId = entities.Bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 30,
            IncludeFriday = true,
            Status = SubscriptionStatus.Active,
            StartDate = startDate,
            EndDate = startDate.AddDays(29),
            RegionId = entities.Region.Id,
            CountryId = entities.Country.Id,
            DeliveryAddress = entities.CustomerAddress.AddressLine,
            PaidAmount = new Money(10m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            RestaurantId = RestaurantId,
            ConfirmedAtUtc = Clock.UtcNow.AddHours(-2)
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var trip = new Trip
        {
            RestaurantId = RestaurantId,
            DispatcherUserId = entities.DispatcherA.Id,
            Code = "TRIP-VC-" + suffix,
            Status = _seed.TripStatus,
            PlannedDate = startDate,
            StartedAtUtc = _seed.TripStatus == TripStatus.InProgress ? Clock.UtcNow.AddMinutes(-45) : null
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        // Stop coordinates sit ~2.5km north of the seeded restaurant/customer address
        // so the seeded distance matches the default contact-radius knob.
        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            OrderCode = "ORD-VC-" + suffix,
            CustomerMaskedId = "MM-VC-" + suffix[..4],
            CustomerName = "عميل مكالمة " + suffix,
            CustomerPhone = SeededCustomerPhone,
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch Meal",
            FullAddress = "العاصمة، القبلة، شارع الخليج، برج الساحل",
            SlotLabel = "Lunch",
            SequenceNumber = 1,
            Status = _seed.StopStatus,
            Latitude = 29.3983m,
            Longitude = 47.9774m
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = DriverProfileId,
            AssignedAtUtc = Clock.UtcNow.AddMinutes(-30),
            Status = _seed.ActiveDriverAssignment ? "Active" : "Reassigned"
        };
        db.DriverAssignments.Add(assignment);

        // Contact-eligibility context: the driver's fresh, accurate fix, placed at the
        // seeded distance south of the stop. The degrees-per-meter constant matches the
        // Haversine earth radius (2πR/360) so boundary seeds land exactly on the limit.
        // Captured on the REAL clock: the eligibility service uses the production
        // IDateTimeProvider, not this host's test clock.
        var locationDeltaDegrees = (decimal)(_seed.DistanceMeters / (Math.PI * 6_371_000d / 180d));
        var capturedAt = DateTime.UtcNow.AddSeconds(-_seed.LocationAgeSeconds);
        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            DriverProfileId = DriverProfileId,
            Latitude = stop.Latitude!.Value - locationDeltaDegrees,
            Longitude = stop.Longitude!.Value,
            AccuracyMeters = _seed.AccuracyMeters,
            CapturedAtUtc = capturedAt,
            ReceivedAtUtc = capturedAt,
            RecordedAtUtc = capturedAt
        });

        // Outsider: an authenticated account with no relationship to this delivery.
        var outsiderUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "outs_" + suffix,
            NormalizedUserName = ("outs_" + suffix).ToUpperInvariant(),
            Email = $"outs_{suffix}@mealmate.local",
            NormalizedEmail = $"outs_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Customer,
            FullName = "مستخدم خارج الطلب " + suffix,
            PhoneNumber = "965599" + suffix[..4],
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.Add(outsiderUser);
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = outsiderUser.Id,
            AllergiesCompletedAtUtc = Clock.UtcNow,
            DietaryPreferencesCompletedAtUtc = Clock.UtcNow
        });
        await db.SaveChangesAsync();
        OutsiderUserId = outsiderUser.Id;

        TripStopId = stop.Id;
        TripId = trip.Id;
        SubscriptionDayId = day.Id;
    }
}
