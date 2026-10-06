using System.Text.Json;
using FluentAssertions;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using MealMate.Persistence.VoiceCalls;
using Microsoft.EntityFrameworkCore;
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
/// Red-first regressions for the 2026-10-06 follow-up review through the REAL MediatR
/// pipeline on PostgreSQL: V01 (rolled-back commit retry must not invent a call),
/// V02 (reassignment between fast lookup and lock must not bind the old driver),
/// V07 (self-persisting ownership + clean reconciliation in one scope).
/// </summary>
public sealed class VoiceCallV2RetryOwnershipTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;
    private Guid _driverUserId;
    private Guid _driverProfileAId;
    private Guid _customerUserId;
    private Guid _tripStopId;
    private Guid _tripId;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
            await using (var migrateDb = CreateDb())
            {
                await migrateDb.Database.MigrateAsync();
            }
            await SeedAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed for v2 retry/ownership tests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_provider != null) await _provider.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    private AppDbContext CreateDb() => CreateDb(failFirstCommit: false);

    private AppDbContext CreateDb(bool failFirstCommit)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        if (failFirstCommit)
        {
            builder.AddInterceptors(new FailFirstCommitInterceptor());
        }
        return new AppDbContext(builder.Options, Substitute.For<IDomainEventDispatcher>());
    }

    private async Task SeedAsync()
    {
        _driverUserId = Guid.NewGuid();
        _customerUserId = Guid.NewGuid();

        await using var db = CreateDb();

        var program = new Program { Code = "PRG-" + Guid.NewGuid().ToString("N")[..8], NameAr = "برنامج", NameEn = "Program" };
        var bundle = new Bundle { ProgramId = program.Id, Code = "BND-" + Guid.NewGuid().ToString("N")[..8], NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, BreakfastCount = 0, DinnerCount = 0, SnackCount = 0 };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        db.Users.Add(new ApplicationUser
        {
            Id = _driverUserId, UserName = "drv_" + _driverUserId.ToString("N")[..8],
            NormalizedUserName = ("DRV_" + _driverUserId.ToString("N")[..8]).ToUpperInvariant(),
            UserType = UserType.Driver, FullName = "سائق أ", SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true, AccountStatus = AccountStatus.Active
        });
        db.Users.Add(new ApplicationUser
        {
            Id = _customerUserId, UserName = "cust_" + _customerUserId.ToString("N")[..8],
            NormalizedUserName = ("CUST_" + _customerUserId.ToString("N")[..8]).ToUpperInvariant(),
            UserType = UserType.Customer, FullName = "عميل", SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true, AccountStatus = AccountStatus.Active
        });

        var profileA = new DriverProfile
        {
            UserId = _driverUserId, FullNameAr = "سائق أ", FullNameEn = "Driver A",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(profileA);

        var subscription = new Subscription
        {
            CustomerUserId = _customerUserId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 30, Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(29),
            PaidAmount = new Money(10m, "KWD")
        };
        db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = SubscriptionDayStatus.ConfirmedByRestaurant };
        db.SubscriptionDays.Add(day);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRIP-" + Guid.NewGuid().ToString("N")[..10], Status = TripStatus.InProgress, PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), StartedAtUtc = DateTime.UtcNow.AddMinutes(-30) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id, SubscriptionDayId = day.Id, OrderCode = "ORD-" + Guid.NewGuid().ToString("N")[..10],
            CustomerMaskedId = "MM-RT-0001", CustomerName = "عميل", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "Lunch", SequenceNumber = 1,
            Status = TripStopStatus.PickedUp, Latitude = 29.3983m, Longitude = 47.9774m
        };
        db.TripStops.Add(stop);

        var assignmentA = new DriverAssignment { TripId = trip.Id, DriverProfileId = profileA.Id, AssignedAtUtc = DateTime.UtcNow.AddMinutes(-30), Status = "Active" };
        db.DriverAssignments.Add(assignmentA);

        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = profileA.Id, Latitude = 29.3974m, Longitude = 47.9774m,
            AccuracyMeters = 20, CapturedAtUtc = DateTime.UtcNow, ReceivedAtUtc = DateTime.UtcNow, RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        _driverProfileAId = profileA.Id;
        _tripStopId = stop.Id;
        _tripId = trip.Id;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MealMateDb"] = _container.GetConnectionString()
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistence(configuration);
        services.AddSingleton<IDateTimeProvider, MealMate.Infrastructure.Services.DateTimeProvider>();

        _provider = services.BuildServiceProvider();
    }

    private readonly List<ServiceProvider> _disposables = new();

    private async Task<MediatR.ISender> BuildSenderAsync(AppDbContext? faultedDb = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MealMateDb"] = _container.GetConnectionString()
        }).Build());
        services.AddSingleton<IDateTimeProvider, MealMate.Infrastructure.Services.DateTimeProvider>();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(_driverUserId));
        services.AddSingleton<MealMate.Application.Common.Localization.ILocalizedMessageProvider>(
            new MealMate.Application.Common.Localization.NullLocalizedMessageProvider());
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null));
            o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        if (faultedDb != null)
        {
            services.RemoveAll<Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddSingleton(faultedDb);
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        }

        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return provider.GetRequiredService<MediatR.ISender>();
    }

    [Fact]
    public async Task V01_TransientCommitFailure_RestagesAllRows_AndPersistsTheCall()
    {
        var faultedDb = CreateDb(failFirstCommit: true);
        var sender = await BuildSenderAsync(faultedDb: faultedDb);

        var result = await sender.Send(new InitiateVoiceCallV2Command(
            _tripStopId, Guid.NewGuid(), new VoiceCallDeviceContext(_driverUserId, Guid.NewGuid())));

        result.IsSuccess.Should().BeTrue(
            "the strategy must retry the transient commit failure as one unit; got {0}", result.Error?.Description);

        // Every staged row must be REAL — not just IsSuccess.
        await using var db = CreateDb();
        var call = await db.CallSessions.SingleOrDefaultAsync(c => c.TripStopId == _tripStopId);
        call.Should().NotBeNull("the returned call must exist in PostgreSQL");
        var busyCount = await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == call!.Id);
        busyCount.Should().Be(2);
        var receipt = await db.IdempotencyRecords.SingleOrDefaultAsync(r => r.ResourceUri == call!.Id.ToString());
        receipt.Should().NotBeNull();
        var audit = await db.AuditLogs.AnyAsync(a => a.EntityId == call!.Id && a.Action == "VoiceCallInitiatedV2");
        audit.Should().BeTrue();
        var outbox = await db.OutboxMessages.AnyAsync(o => o.AggregateId == call!.Id.ToString());
        outbox.Should().BeTrue();
    }

    [Fact]
    public async Task V02_ReassignmentBetweenFastAndLockedResolution_DoesNotBindOldDriver()
    {
        // The decorator runs the real service; after the FAST participants resolution it
        // performs the actual reassignment (A → Reassigned, B → new Active), so the
        // LOCKED resolution inside the handler sees driver B — exactly the race window.
        var driverBUserId = Guid.NewGuid();
        Guid driverBProfileId;
        await using (var db = CreateDb())
        {
            db.Users.Add(new ApplicationUser
            {
                Id = driverBUserId, UserName = "drvb_" + driverBUserId.ToString("N")[..8],
                NormalizedUserName = ("DRVB_" + driverBUserId.ToString("N")[..8]).ToUpperInvariant(),
                UserType = UserType.Driver, FullName = "سائق ب", SecurityStamp = Guid.NewGuid().ToString("D"),
                EmailConfirmed = true, AccountStatus = AccountStatus.Active
            });
            var profileB = new DriverProfile
            {
                UserId = driverBUserId, FullNameAr = "سائق ب", FullNameEn = "Driver B",
                VerificationStatus = VerificationStatus.Approved
            };
            db.DriverProfiles.Add(profileB);
            await db.SaveChangesAsync();
            driverBProfileId = profileB.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MealMateDb"] = _container.GetConnectionString()
        }).Build());
        services.AddSingleton<IDateTimeProvider, MealMate.Infrastructure.Services.DateTimeProvider>();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(_driverUserId));
        services.AddSingleton<MealMate.Application.Common.Localization.ILocalizedMessageProvider>(
            new MealMate.Application.Common.Localization.NullLocalizedMessageProvider());
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null));
            o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });
        // Decorate AFTER AddApplication so this replaces the scanned registration.
        services.RemoveAll<IDeliveryContactEligibilityService>();
        services.AddScoped<IDeliveryContactEligibilityService>(sp =>
            new ReassignmentFaultEligibilityService(
                new DeliveryContactEligibilityService(sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<IDateTimeProvider>()),
                sp, _tripId, driverBProfileId));

        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);

        var sender = provider.GetRequiredService<MediatR.ISender>();
        var result = await sender.Send(new InitiateVoiceCallV2Command(
            _tripStopId, Guid.NewGuid(), new VoiceCallDeviceContext(_customerUserId, Guid.NewGuid())));

        // The participant pair changed between fast lookup and the locked callback: the
        // locks guard the OLD pair, so the only safe outcome is a retriable conflict.
        result.IsSuccess.Should().BeFalse("stale locks must never bind the old driver; got {0}", result.Error?.Code);
        result.Error?.Code.Should().Be("calls.participants_changed");

        await using var verifyDb = CreateDb();
        var calls = await verifyDb.CallSessions.Where(c => c.TripStopId == _tripStopId).ToListAsync();
        calls.Should().BeEmpty("no call may be created under a stale participant pair");
    }

    [Fact]
    public async Task V07_ReconciledConflict_LeavesNoOrphanStagedRows_AndScopeStaysUsable()
    {
        // Seed a TERMINAL call that already occupies (CallerUserId, ClientRequestId) —
        // the unique index the creation insert will violate after the fast receipt
        // lookup misses and the busy check passes (terminal calls are not busy).
        var reusedKey = Guid.NewGuid();
        Guid endedCallId;
        await using (var db = CreateDb())
        {
            var ended = new CallSession(_tripStopId, _driverUserId, CallParticipantType.Driver, _customerUserId, CallParticipantType.Customer, reusedKey, DateTime.UtcNow.AddMinutes(-10));
            ended.MarkRinging(DateTime.UtcNow.AddMinutes(-10));
            ended.MarkMissed(DateTime.UtcNow.AddMinutes(-9));
            db.CallSessions.Add(ended);
            await db.SaveChangesAsync();
            endedCallId = ended.Id;
        }

        var sender = await BuildSenderAsync();
        var result = await sender.Send(new InitiateVoiceCallV2Command(
            _tripStopId, reusedKey, new VoiceCallDeviceContext(_driverUserId, Guid.NewGuid())));

        result.IsSuccess.Should().BeTrue("the reconciled request resolves to the persisted call; got {0}", result.Error?.Description);
        result.Value.CallId.Should().Be(endedCallId);

        // The poisoned-batch check: no orphan Busy/receipt/audit/outbox rows staged by
        // the failed attempt may leak through the pipeline's second save.
        await using var verifyDb = CreateDb();
        var busyForEnded = await verifyDb.VoiceCallBusyParticipants.CountAsync(b => b.CallId == endedCallId);
        busyForEnded.Should().Be(0, "a terminal call must not gain Busy rows from a failed attempt");
        var receiptForEnded = await verifyDb.IdempotencyRecords.CountAsync(r => r.ResourceUri == endedCallId.ToString());
        receiptForEnded.Should().Be(0, "the pre-existing call had no receipt and none may be invented");
        var orphanAudits = await verifyDb.AuditLogs.CountAsync(a => a.Action == "VoiceCallInitiatedV2" && a.EntityId == endedCallId);
        orphanAudits.Should().Be(0);
        var orphanOutbox = await verifyDb.OutboxMessages.CountAsync(o => o.AggregateId == endedCallId.ToString());
        orphanOutbox.Should().Be(0);

        // Next command through the same pipeline still works (no poisoned context).
        var next = await sender.Send(new UpdateDriverLocationCommand(
            DriverId: _driverProfileAId,
            TripId: _tripId,
            Latitude: 29.3974m,
            Longitude: 47.9774m,
            AccuracyMeters: 20));
        next.IsSuccess.Should().BeTrue("a reconciled conflict must not poison subsequent commands");
    }

    private sealed class ReassignmentFaultEligibilityService : IDeliveryContactEligibilityService
    {
        private readonly IDeliveryContactEligibilityService _inner;
        private readonly IServiceProvider _provider;
        private readonly Guid _tripId;
        private readonly Guid _driverBProfileId;
        private int _evalCount;

        public ReassignmentFaultEligibilityService(
            IDeliveryContactEligibilityService inner,
            IServiceProvider provider,
            Guid tripId,
            Guid driverBProfileId)
        {
            _inner = inner;
            _provider = provider;
            _tripId = tripId;
            _driverBProfileId = driverBProfileId;
        }

        public async Task<Result<VoiceCallContactParticipants>> ResolveParticipantsAsync(Guid tripStopId, CancellationToken cancellationToken) =>
            await _inner.ResolveParticipantsAsync(tripStopId, cancellationToken);

        public async Task<Result<DeliveryContactEligibilityDto>> EvaluateAsync(Guid tripStopId, Guid actorUserId, CancellationToken cancellationToken)
        {
            var result = await _inner.EvaluateAsync(tripStopId, actorUserId, cancellationToken);

            // The FAST eligibility has now passed against the OLD assignment. Reassign
            // exactly here — between the fast path and the locked callback — so the
            // lock callback resolves a DIFFERENT participant pair (the race window).
            if (Interlocked.Increment(ref _evalCount) == 1)
            {
                var scope = _provider.CreateScope();
                await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var assignments = await db.DriverAssignments
                    .Where(a => a.TripId == _tripId && a.Status == "Active")
                    .ToListAsync(cancellationToken);
                var oldProfileIds = assignments.Select(a => a.DriverProfileId).ToList();
                foreach (var a in assignments)
                {
                    a.Status = "Reassigned";
                }

                db.DriverAssignments.Add(new DriverAssignment
                {
                    TripId = _tripId, DriverProfileId = _driverBProfileId,
                    AssignedAtUtc = DateTime.UtcNow, Status = "Active"
                });
                await db.SaveChangesAsync(cancellationToken);

                // Driver B already streams a fresh accurate fix, so only the participant
                // pair differs — not the location policy.
                await using var readDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var oldFix = await readDb.DeliveryTrackingPoints
                    .Where(tp => oldProfileIds.Contains(tp.DriverProfileId))
                    .OrderByDescending(tp => tp.CapturedAtUtc ?? tp.RecordedAtUtc)
                    .FirstAsync(cancellationToken);
                readDb.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
                {
                    TripId = _tripId, DriverProfileId = _driverBProfileId,
                    Latitude = oldFix.Latitude, Longitude = oldFix.Longitude,
                    AccuracyMeters = 20, CapturedAtUtc = DateTime.UtcNow,
                    ReceivedAtUtc = DateTime.UtcNow, RecordedAtUtc = DateTime.UtcNow
                });
                await readDb.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Raises a transient PostgreSQL error (SQLSTATE 40001) on the first commit attempt.</summary>
    private sealed class FailFirstCommitInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private bool _failedOnce;

        public override System.Threading.Tasks.ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!_failedOnce)
            {
                _failedOnce = true;
                throw new Npgsql.PostgresException("simulated serialization failure before commit", "ERROR", "XX000", "40001");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }


    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid userId) => UserId = userId;

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
