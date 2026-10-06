using FluentAssertions;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.VoiceCalls;

/// <summary>
/// T02 regression (F03): voice-call creation must succeed through the production
/// persistence registration (EnableRetryOnFailure) while the participant lock keeps
/// its own transaction — the whole lock+query unit runs inside the execution strategy.
/// Created failing in T00; flips green with T02.
/// </summary>
public sealed class VoiceCallProductionConfigurationTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;
    private Guid _driverUserId;
    private Guid _customerUserId;
    private Guid _tripStopId;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync();

            await using var db = CreateDb();
            await db.Database.MigrateAsync();
            await SeedDeliveryContextAsync(db);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MealMateDb"] = _container.GetConnectionString()
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication();
            services.AddPersistence(configuration);
            // The API registers the clock inside AddInfrastructure; this provider builds a
            // narrow slice of production DI without external providers, so register it directly.
            services.AddSingleton<IDateTimeProvider, MealMate.Infrastructure.Services.DateTimeProvider>();
            services.AddSingleton<ICurrentUser>(new TestCurrentUser(_driverUserId));

            _provider = services.BuildServiceProvider();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed to start for voice-call production configuration tests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_provider != null)
        {
            await _provider.DisposeAsync();
        }

        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private async Task SeedDeliveryContextAsync(AppDbContext db)
    {
        _driverUserId = Guid.NewGuid();
        _customerUserId = Guid.NewGuid();

        var program = new Program
        {
            Code = "PRG-VC-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج اختبار المكالمات",
            NameEn = "Voice Call Test Program"
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BND-VC-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "حزمة اختبار",
            NameEn = "Test Bundle",
            MealsPerDay = 1,
            BreakfastCount = 0,
            LunchCount = 1,
            DinnerCount = 0,
            SnackCount = 0
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        var customer = new ApplicationUser
        {
            Id = _customerUserId,
            UserName = "cust_" + Guid.NewGuid().ToString("N")[..8],
            NormalizedUserName = ("cust_" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant(),
            UserType = UserType.Customer,
            FullName = "عميل تكامل المكالمات",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var driverUser = new ApplicationUser
        {
            Id = _driverUserId,
            UserName = "drv_" + Guid.NewGuid().ToString("N")[..8],
            NormalizedUserName = ("drv_" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant(),
            UserType = UserType.Driver,
            FullName = "سائق تكامل المكالمات",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.AddRange(customer, driverUser);

        var driverProfile = new DriverProfile
        {
            UserId = driverUser.Id,
            FullNameAr = "سائق تكامل",
            FullNameEn = "Integration Driver",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driverProfile);

        var subscription = new Subscription
        {
            CustomerUserId = customer.Id,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 30,
            Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(29),
            PaidAmount = new Money(10m, "KWD")
        };
        db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant
        };
        db.SubscriptionDays.Add(day);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRIP-" + Guid.NewGuid().ToString("N")[..10],
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-30)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            OrderCode = "ORD-" + Guid.NewGuid().ToString("N")[..10],
            CustomerMaskedId = "MM-IT-0001",
            CustomerName = "عميل تكامل",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch Meal",
            SlotLabel = "Lunch",
            SequenceNumber = 1,
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);

        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driverProfile.Id,
            AssignedAtUtc = DateTime.UtcNow.AddMinutes(-20),
            Status = "Active"
        });

        // Contact-eligibility context: stop coordinates plus a fresh, accurate driver fix.
        stop.Latitude = 29.3983m;
        stop.Longitude = 47.9774m;
        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            DriverProfileId = driverProfile.Id,
            Latitude = 29.3974m,
            Longitude = 47.9774m,
            AccuracyMeters = 20,
            CapturedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        _tripStopId = stop.Id;
    }

    [Fact]
    public async Task Initiate_ThroughProductionRetryConfiguration_CreatesSingleCall()
    {
        var sender = _provider.GetRequiredService<MediatR.ISender>();

        var result = await sender.Send(new InitiateCallCommand(_tripStopId, Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue(
            "the participant lock transaction must run inside the retry execution strategy; got {0}/{1}",
            result.Error?.Code, result.Error?.Description);
        result.Value.CallId.Should().NotBeEmpty();

        await using var db = CreateDb();
        var callCount = await db.CallSessions.CountAsync(c => c.TripStopId == _tripStopId);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Initiate_WithTransientCommitFailure_RetriesAsOneUnit_AndCreatesExactlyOneCall()
    {
        // A transient serialization failure is raised the first time the participant-lock
        // transaction commits. The execution strategy must retry the whole unit (locks,
        // re-reads, insert, commit) and converge without duplicating the call or audit row.
        var interceptor = new FailFirstCommitInterceptor();
        var scopedServices = new ServiceCollection();
        scopedServices.AddLogging();
        scopedServices.AddApplication();
        scopedServices.AddPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MealMateDb"] = _container.GetConnectionString()
            })
            .Build());
        scopedServices.AddSingleton<IDateTimeProvider, MealMate.Infrastructure.Services.DateTimeProvider>();
        scopedServices.AddSingleton<ICurrentUser>(new TestCurrentUser(_driverUserId));
        // Re-register the AppDbContext with the fault-injecting commit interceptor.
        scopedServices.RemoveAll<DbContextOptions<AppDbContext>>();
        scopedServices.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        scopedServices.AddDbContext<AppDbContext>(dbOptions =>
        {
            dbOptions.UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null));
            dbOptions.AddInterceptors(interceptor);
        });

        await using var provider = scopedServices.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();

        var result = await sender.Send(new InitiateCallCommand(_tripStopId, Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue("the strategy must retry the transient commit failure as one retriable unit; got {0}", result.Error?.Description);
        interceptor.CommitFailuresObserved.Should().BeGreaterThanOrEqualTo(1);

        await using var db = CreateDb();
        var callCount = await db.CallSessions.CountAsync(c => c.TripStopId == _tripStopId);
        var auditCount = await db.AuditLogs.CountAsync(a => a.Action == "VoiceCallInitiated" && a.EntityId == result.Value.CallId);
        callCount.Should().Be(1, "a retried transaction must not duplicate the call");
        auditCount.Should().Be(1, "a retried transaction must not duplicate audit evidence");
    }

    [Fact]
    public async Task Initiate_ConcurrentSameClientRequestId_ConvergesToSingleCall()
    {
        var clientRequestId = Guid.NewGuid();

        async Task<MealMate.Application.Common.Models.Result<InitiateCallResponseDto>> SendFromNewScopeAsync()
        {
            using var scope = _provider.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
            return await sender.Send(new InitiateCallCommand(_tripStopId, clientRequestId));
        }

        var results = await Task.WhenAll(SendFromNewScopeAsync(), SendFromNewScopeAsync());

        results.Should().OnlyContain(r => r.IsSuccess, "both identical requests resolve to the persisted winner: {0}",
            string.Join(" | ", results.Select(r => r.Error?.Description)));

        results[0].Value.CallId.Should().Be(results[1].Value.CallId);

        await using var db = CreateDb();
        var calls = await db.CallSessions.Where(c => c.TripStopId == _tripStopId).ToListAsync();
        calls.Should().HaveCount(1);
        var audits = await db.AuditLogs.CountAsync(a => a.Action == "VoiceCallInitiated" && a.EntityId == calls[0].Id);
        audits.Should().Be(1);
    }

    /// <summary>Raises a transient PostgreSQL error (SQLSTATE 40001) on the first commit attempt.</summary>
    private sealed class FailFirstCommitInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private bool _failedOnce;

        public int CommitFailuresObserved { get; private set; }

        public override System.Threading.Tasks.ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!_failedOnce)
            {
                _failedOnce = true;
                CommitFailuresObserved++;
                throw new Npgsql.PostgresException(
                    "simulated serialization failure before commit",
                    "ERROR",
                    "XX000",
                    "40001");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid userId)
        {
            UserId = userId;
        }

        public bool IsAuthenticated => true;
        public Guid? UserId { get; }
        public UserType? UserType => Domain.Enums.UserType.Driver;
        public Guid? RestaurantId => null;
        public IReadOnlyCollection<string> Roles => ["Driver"];
        public IReadOnlyCollection<string> Permissions => [];
        public IReadOnlyCollection<Guid> AccessScopeIds => [];
        public IReadOnlyCollection<Guid> TenantIds => [];
        public bool IsInRole(string role) => role == "Driver";
        public bool HasPermission(string permission) => false;
    }
}
