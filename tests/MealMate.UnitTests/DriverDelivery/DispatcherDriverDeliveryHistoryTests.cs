using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherDriverDeliveryHistoryTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static GetDispatcherDriverDeliveryHistoryQueryHandler CreateHandler(
        AppDbContext db,
        ICurrentUser? currentUser = null)
        => new(db, currentUser, NullLocalizedMessageProvider.Instance);

    private static (DriverProfile driver, Trip trip1, Trip trip2) SeedDriverWithTrips(AppDbContext db, Guid restaurantId)
    {
        var driver = new DriverProfile
        {
            RestaurantId = restaurantId,
            FullNameAr = "سائق السجل",
            FullNameEn = "History Driver",
            IsAvailable = true
        };

        var trip1 = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-H1",
            PlannedDate = new DateOnly(2026, 10, 1),
            ShiftName = "صباحي",
            Status = TripStatus.Completed,
            CompletedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        var trip2 = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-H2",
            PlannedDate = new DateOnly(2026, 10, 2),
            Status = TripStatus.Completed,
            CompletedAtUtc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)
        };

        var stop1 = new TripStop
        {
            TripId = trip1.Id,
            OrderCode = "ORD-1",
            CustomerMaskedId = "CUST-1",
            CustomerName = "عميل أول",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawalli",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch meal",
            SlotLabel = "09:00-10:00",
            SequenceNumber = 1,
            Status = TripStopStatus.Delivered,
            DeliveredAtUtc = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc)
        };
        var stop2 = new TripStop
        {
            TripId = trip2.Id,
            OrderCode = "ORD-2",
            CustomerMaskedId = "CUST-2",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة عشاء",
            MealSummaryEn = "Dinner meal",
            SlotLabel = "11:00-12:00",
            SequenceNumber = 1,
            Status = TripStopStatus.Failed,
            FailureReason = "العميل غير متواجد"
        };

        trip1.Stops.Add(stop1);
        trip2.Stops.Add(stop2);

        db.DriverProfiles.Add(driver);
        db.Trips.AddRange(trip1, trip2);
        db.TripStops.AddRange(stop1, stop2);
        db.DriverAssignments.AddRange(
            new DriverAssignment { TripId = trip1.Id, DriverProfileId = driver.Id, AssignedAtUtc = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc), Status = "Completed" },
            new DriverAssignment { TripId = trip2.Id, DriverProfileId = driver.Id, AssignedAtUtc = new DateTime(2026, 10, 2, 6, 0, 0, DateTimeKind.Utc), Status = "Completed" });
        db.SaveChanges();

        return (driver, trip1, trip2);
    }

    [Fact]
    public async Task DeliveryHistory_ReturnsTripsGroupedWithStops_NewestFirst()
    {
        await using var db = CreateDb();
        var (driver, trip1, trip2) = SeedDriverWithTrips(db, Guid.NewGuid());
        var handler = CreateHandler(db);

        var result = await handler.Handle(new GetDispatcherDriverDeliveryHistoryQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.TripCount.Should().Be(2);
        result.Value.Trips[0].Code.Should().Be(trip2.Code);
        result.Value.Trips[1].Code.Should().Be(trip1.Code);

        var firstTripStops = result.Value.Trips[0].Stops;
        firstTripStops.Should().HaveCount(1);
        firstTripStops[0].StopStatus.Should().Be(nameof(TripStopStatus.Failed));
        firstTripStops[0].FailureReason.Should().Be("العميل غير متواجد");
        firstTripStops[0].CustomerName.Should().Be("CUST-2");

        var secondTripStops = result.Value.Trips[1].Stops;
        secondTripStops[0].StopStatus.Should().Be(nameof(TripStopStatus.Delivered));
        secondTripStops[0].DeliveredAtUtc.Should().NotBeNull();
        secondTripStops[0].CustomerName.Should().Be("عميل أول");
        secondTripStops[0].Zone.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeliveryHistory_ResolvesBoxBarcodeWhenLinked()
    {
        await using var db = CreateDb();
        var (driver, trip1, _) = SeedDriverWithTrips(db, Guid.NewGuid());
        var stop = trip1.Stops.Single();
        var barcode = new MealBarcode
        {
            BarcodeValue = "#BX-9001",
            GeneratedAtUtc = DateTime.UtcNow,
            TripStopId = stop.Id
        };
        db.MealBarcodes.Add(barcode);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var result = await handler.Handle(new GetDispatcherDriverDeliveryHistoryQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Trips.Single(t => t.TripId == trip1.Id).Stops[0].BoxCode.Should().Be("#BX-9001");
    }

    [Fact]
    public async Task DeliveryHistory_ExcludesOtherDriversTrips()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var (driver, _, _) = SeedDriverWithTrips(db, restaurantId);

        var otherDriver = new DriverProfile
        {
            RestaurantId = restaurantId,
            FullNameAr = "سائق آخر",
            FullNameEn = "Other Driver",
            IsAvailable = true
        };
        var otherTrip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-OTHER",
            PlannedDate = new DateOnly(2026, 10, 3),
            Status = TripStatus.Completed
        };
        db.DriverProfiles.Add(otherDriver);
        db.Trips.Add(otherTrip);
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = otherTrip.Id,
            DriverProfileId = otherDriver.Id,
            AssignedAtUtc = DateTime.UtcNow,
            Status = "Completed"
        });
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        var result = await handler.Handle(new GetDispatcherDriverDeliveryHistoryQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Trips.Select(t => t.Code).Should().NotContain("TRP-OTHER");
        result.Value.TripCount.Should().Be(2);
    }

    [Fact]
    public async Task DeliveryHistory_UnknownDriver_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var handler = CreateHandler(db);

        var result = await handler.Handle(new GetDispatcherDriverDeliveryHistoryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeliveryHistory_CrossRestaurantCaller_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var (driver, _, _) = SeedDriverWithTrips(db, Guid.NewGuid());

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(Guid.NewGuid()); // different restaurant

        var handler = CreateHandler(db, currentUser);
        var result = await handler.Handle(new GetDispatcherDriverDeliveryHistoryQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
