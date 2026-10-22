using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherAssignmentTerminalTripTests
{
    [Theory]
    [InlineData(false, TripStatus.Completed, "Active")]
    [InlineData(false, TripStatus.Completed, "Assigned")]
    [InlineData(false, TripStatus.Cancelled, "Active")]
    [InlineData(false, TripStatus.Cancelled, "Assigned")]
    [InlineData(true, TripStatus.Completed, "Active")]
    [InlineData(true, TripStatus.Completed, "Assigned")]
    [InlineData(true, TripStatus.Cancelled, "Active")]
    [InlineData(true, TripStatus.Cancelled, "Assigned")]
    public async Task Assignment_IgnoresUnrelatedTerminalDriverHistory(
        bool bulk, TripStatus historicalStatus, string assignmentStatus)
    {
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق", FullNameEn = "Driver", VehiclePlate = "123",
            VerificationStatus = VerificationStatus.Approved
        };
        var historical = NewTrip(historicalStatus);
        var assignment = new DriverAssignment
        {
            TripId = historical.Id, DriverProfileId = driver.Id,
            Status = assignmentStatus, AssignedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var source = NewTrip(TripStatus.Planned);
        var stop = NewStop(source);
        db.AddRange(driver, historical, assignment, source, stop);
        await db.SaveChangesAsync();

        if (bulk)
        {
            var handler = new BulkAssignBoxesCommandHandler(db, NullLocalizedMessageProvider.Instance,
                txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());
            var result = await handler.Handle(new BulkAssignBoxesCommand([stop.Id], driver.Id), default);
            result.IsSuccess.Should().BeTrue("unrelated terminal trips must not block new work: {0}", result.Error);
            result.Value.TripId.Should().NotBe(historical.Id);
        }
        else
        {
            var handler = new AssignBoxToDriverCommandHandler(db, NullLocalizedMessageProvider.Instance,
                txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());
            var result = await handler.Handle(new AssignBoxToDriverCommand(stop.Id, driver.Id), default);
            result.IsSuccess.Should().BeTrue("unrelated terminal trips must not block new work: {0}", result.Error);
        }

        db.ChangeTracker.Clear();
        (await db.Trips.SingleAsync(t => t.Id == historical.Id)).Status.Should().Be(historicalStatus);
        (await db.DriverAssignments.SingleAsync(a => a.Id == assignment.Id)).Status.Should().Be(assignmentStatus);
        var assignedStop = await db.TripStops.Include(s => s.Trip).SingleAsync(s => s.Id == stop.Id);
        assignedStop.Trip.Status.Should().Be(TripStatus.Assigned);
        assignedStop.TripId.Should().NotBe(historical.Id);
        (await db.DriverAssignments.AnyAsync(a => a.TripId == assignedStop.TripId
            && a.DriverProfileId == driver.Id && a.Status == "Active")).Should().BeTrue();
        (await db.OutboxMessages.CountAsync(m => m.EventType == "DriverBoxAssigned")).Should().Be(1);
    }

    [Theory]
    [InlineData(false, TripStatus.Completed)]
    [InlineData(false, TripStatus.Cancelled)]
    [InlineData(true, TripStatus.Completed)]
    [InlineData(true, TripStatus.Cancelled)]
    public async Task Assignment_StillRejectsBoxFromTerminalSourceTrip(bool bulk, TripStatus status)
    {
        await using var db = CreateDb();
        var source = NewTrip(status);
        var stop = NewStop(source);
        db.AddRange(source, stop);
        await db.SaveChangesAsync();

        if (bulk)
        {
            var handler = new BulkAssignBoxesCommandHandler(db, NullLocalizedMessageProvider.Instance,
                txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());
            var result = await handler.Handle(new BulkAssignBoxesCommand([stop.Id], Guid.NewGuid()), default);
            result.Error.Code.Should().Be("TRIP_TERMINAL");
        }
        else
        {
            var handler = new AssignBoxToDriverCommandHandler(db, NullLocalizedMessageProvider.Instance,
                txExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());
            var result = await handler.Handle(new AssignBoxToDriverCommand(stop.Id, Guid.NewGuid()), default);
            result.Error.Code.Should().Be("TRIP_TERMINAL");
        }

        (await db.Trips.SingleAsync()).Status.Should().Be(status);
        (await db.DriverAssignments.CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync()).Should().Be(0);
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static Trip NewTrip(TripStatus status) => new()
    {
        Code = "TRIP-TEST", Status = status,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private static TripStop NewStop(Trip trip) => new()
    {
        TripId = trip.Id, OrderCode = "BOX-TEST", CustomerMaskedId = "CUSTOMER-TEST",
        ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal",
        SlotLabel = "AM", BoxCount = 1, Status = TripStopStatus.Pending
    };
}
