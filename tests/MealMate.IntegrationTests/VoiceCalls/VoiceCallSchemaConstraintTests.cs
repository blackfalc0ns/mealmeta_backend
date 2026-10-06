using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.VoiceCalls;

/// <summary>
/// T07 acceptance (AC26/AC27): database-level guards — one busy row per user across
/// caller/receiver roles, one attempt per call, unique open contact cycle, unique
/// reveal-grant idempotency, unique device receipts — enforced by PostgreSQL, not
/// application discipline.
/// </summary>
public sealed class VoiceCallSchemaConstraintTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

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
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker/Postgres container failed for voice-call schema constraint tests.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private async Task<(TripStop Stop, Trip Trip)> SeedStopAsync(AppDbContext db)
    {
        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRIP-" + Guid.NewGuid().ToString("N")[..10],
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "ORD-" + Guid.NewGuid().ToString("N")[..10],
            CustomerMaskedId = "MM-SC-0001",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "Lunch",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();
        return (stop, trip);
    }

    private static CallSession NewCallOn(AppDbContext db, Guid stopId, Guid caller, Guid receiver)
    {
        var call = new CallSession(stopId, caller, CallParticipantType.Driver, receiver, CallParticipantType.Customer, Guid.NewGuid(), DateTime.UtcNow);
        call.MarkRinging(DateTime.UtcNow);
        db.CallSessions.Add(call);
        return call;
    }

    [Fact]
    public async Task BusyParticipant_UniquePerUser_BlocksCrossRoleOverlap()
    {
        await using var db = CreateDb();
        var (stop, _) = await SeedStopAsync(db);
        var driver = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var call1 = NewCallOn(db, stop.Id, driver, customer);
        await db.SaveChangesAsync();

        db.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(customer, call1.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();

        // The same user (busy as a receiver in call1) attempts a second call slot from
        // another context — exactly what concurrent requests would do.
        await using var db2 = CreateDb();
        var call2 = NewCallOn(db2, stop.Id, customer, Guid.NewGuid());
        db2.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(customer, call2.Id, DateTime.UtcNow));
        var act = async () => await db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("the busy participant PK prevents one account holding two call slots");
    }

    [Fact]
    public async Task ContactAttempt_UniquePerCall()
    {
        await using var db = CreateDb();
        var (stop, trip) = await SeedStopAsync(db);
        var driver = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var call = NewCallOn(db, stop.Id, driver, customer);

        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = Guid.NewGuid(), AssignedAtUtc = DateTime.UtcNow, Status = "Active" };
        db.DriverAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var contactCase = new DeliveryContactCase(stop.Id, assignment.Id, 1, DateTime.UtcNow);
        db.DeliveryContactCases.Add(contactCase);
        await db.SaveChangesAsync();

        db.DeliveryContactAttempts.Add(new DeliveryContactAttempt(
            call.Id, contactCase.Id, driver, CallParticipantType.Driver,
            DeliveryContactAttempt.OutcomeQualifiedNoAnswer, qualifiesAsNoAnswer: true, ordinal: 1,
            DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow));
        await db.SaveChangesAsync();

        await using var db2 = CreateDb();
        db2.DeliveryContactAttempts.Add(new DeliveryContactAttempt(
            call.Id, contactCase.Id, driver, CallParticipantType.Driver,
            DeliveryContactAttempt.OutcomeQualifiedNoAnswer, qualifiesAsNoAnswer: true, ordinal: 2,
            DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow));
        var act = async () => await db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("one call produces exactly one persisted attempt");
    }

    [Fact]
    public async Task ContactCase_OneOpenCyclePerStopAndAssignment()
    {
        await using var db = CreateDb();
        var (stop, trip) = await SeedStopAsync(db);

        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = Guid.NewGuid(), AssignedAtUtc = DateTime.UtcNow, Status = "Active" };
        db.DriverAssignments.Add(assignment);
        await db.SaveChangesAsync();

        db.DeliveryContactCases.Add(new DeliveryContactCase(stop.Id, assignment.Id, 1, DateTime.UtcNow));
        await db.SaveChangesAsync();

        db.DeliveryContactCases.Add(new DeliveryContactCase(stop.Id, assignment.Id, 2, DateTime.UtcNow));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("only one open cycle per (stop, assignment) may exist");
    }

    [Fact]
    public async Task DeviceReceipt_UniquePerCallAndDevice()
    {
        await using var db = CreateDb();
        var (stop, _) = await SeedStopAsync(db);
        var call = NewCallOn(db, stop.Id, Guid.NewGuid(), Guid.NewGuid());
        await db.SaveChangesAsync();

        var device = Guid.NewGuid();
        db.VoiceCallDeviceReceipts.Add(new VoiceCallDeviceReceipt(call.Id, device, DateTime.UtcNow));
        await db.SaveChangesAsync();

        db.VoiceCallDeviceReceipts.Add(new VoiceCallDeviceReceipt(call.Id, device, DateTime.UtcNow));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("a device cannot double-acknowledge the same call");
    }

    [Fact]
    public async Task RevealGrant_UniquePerDriverAndClientRequestId()
    {
        await using var db = CreateDb();
        var (stop, trip) = await SeedStopAsync(db);

        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = Guid.NewGuid(), AssignedAtUtc = DateTime.UtcNow, Status = "Active" };
        db.DriverAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var contactCase = new DeliveryContactCase(stop.Id, assignment.Id, 1, DateTime.UtcNow);
        db.DeliveryContactCases.Add(contactCase);
        await db.SaveChangesAsync();

        var driver = Guid.NewGuid();
        var clientRequestId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        db.CustomerPhoneRevealGrants.Add(new CustomerPhoneRevealGrant(
            contactCase.Id, driver, first, second, clientRequestId, "تأكيد التسليم", DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow));
        await db.SaveChangesAsync();

        db.CustomerPhoneRevealGrants.Add(new CustomerPhoneRevealGrant(
            contactCase.Id, driver, first, second, clientRequestId, "تأكيد التسليم", DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("reveal replay resolves to the persisted grant, never a second one");
    }

    [Fact]
    public async Task Migration_PreservesHistoricalCallEnums_AndAddsNullableV2Columns()
    {
        // AC28: a database created by the full migration chain stores a pre-v2 call
        // and reads it back with identical enum semantics — persisted enum values are
        // append-only and never renumbered; v2 columns are nullable so legacy rows
        // carry nulls, not fabricated values.
        await using var db = CreateDb();
        var (stop, _) = await SeedStopAsync(db);

        var legacyCall = new CallSession(
            stop.Id,
            Guid.NewGuid(),
            CallParticipantType.Customer,
            Guid.NewGuid(),
            CallParticipantType.Driver,
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(-3));
        legacyCall.MarkRinging(DateTime.UtcNow.AddDays(-3));
        legacyCall.MarkMissed(DateTime.UtcNow.AddDays(-3).AddSeconds(40));
        db.CallSessions.Add(legacyCall);
        await db.SaveChangesAsync();
        var legacyId = legacyCall.Id;

        await using var fresh = CreateDb();
        var reloaded = await fresh.CallSessions.AsNoTracking().SingleAsync(c => c.Id == legacyId);

        reloaded.Status.Should().Be(CallStatus.Missed);
        reloaded.EndReason.Should().Be(CallEndReason.NoAnswer);
        reloaded.ProtocolVersion.Should().Be(1, "legacy rows keep protocol version 1");
        reloaded.Sequence.Should().Be(0);
        reloaded.ReceiverDeviceSessionId.Should().BeNull("v2 columns stay null for legacy rows");
        reloaded.RingExpiresAtUtc.Should().BeNull();

        // v2 enum additions exist without shifting legacy values.
        ((int)CallEndReason.EndedBeforeConnect).Should().Be(11);
        ((int)CallEndReason.Reassigned).Should().Be(13);
        ((int)CallEndReason.NoAnswer).Should().Be(5);
    }
}
