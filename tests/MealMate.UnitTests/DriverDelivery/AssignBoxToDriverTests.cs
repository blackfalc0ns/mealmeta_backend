using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
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

public class AssignBoxToDriverTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Handle_AssignsExistingTripStop_SendsPushNotification_AndReturnsSuccess()
    {
        // Arrange
        await using var db = CreateDb();
        var driver = new DriverProfile
        {
            FullNameAr = "محمد عبد الله",
            FullNameEn = "Mohamed Abdullah",
            VehiclePlate = "12345"
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            Code = "TRP-001",
            Status = TripStatus.Planned,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-9999",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            CustomerMaskedId = "C-01",
            SlotLabel = "Slot 1",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier);

        // Act
        var result = await handler.Handle(new AssignBoxToDriverCommand(stop.Id, driver.Id, "توصيل سريع"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.BoxCode.Should().Be("BX-9999");

        var assignment = await db.DriverAssignments.FirstOrDefaultAsync(da => da.TripId == trip.Id && da.DriverProfileId == driver.Id);
        assignment.Should().NotBeNull();
        assignment!.Status.Should().Be("Active");

        await pushNotifier.Received(1).NotifyBoxAssignedAsync(
            driver.Id,
            Arg.Is<string>(c => c.Contains("9999")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AssignsSubscriptionDay_AutoGeneratesTripAndStop_SendsPushNotification()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            FullNameAr = "أحمد خالد",
            FullNameEn = "Ahmed Khaled",
            VehiclePlate = "54321"
        };
        db.DriverProfiles.Add(driver);

        var day = new SubscriptionDay
        {
            RestaurantId = restaurantId,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "السالمية قطعة 2"
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 1
        };
        db.RestaurantDailyOrders.Add(dailyOrder);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier);

        // Act
        var result = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver.Id, "إسناد مباشر"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.BoxCode.Should().StartWith("#BX-");

        // Verify Trip was auto-generated
        var createdTrip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.RestaurantId == restaurantId);
        createdTrip.Should().NotBeNull();
        createdTrip!.Status.Should().Be(TripStatus.Assigned);
        createdTrip.PlannedDate.Should().Be(day.ServiceDate);

        // Verify TripStop was created linking SubscriptionDay
        var createdStop = await db.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id);
        createdStop.Should().NotBeNull();
        createdStop!.TripId.Should().Be(createdTrip.Id);
        createdStop.DailyOrderId.Should().Be(dailyOrder.Id);

        // Verify DailyOrder status updated
        dailyOrder.Status.Should().Be(OrderStatus.AssignedToDriver);

        // Verify DriverAssignment was created
        var assignment = await db.DriverAssignments.FirstOrDefaultAsync(da => da.TripId == createdTrip.Id && da.DriverProfileId == driver.Id);
        assignment.Should().NotBeNull();

        // Verify Push Notification called
        await pushNotifier.Received(1).NotifyBoxAssignedAsync(
            driver.Id,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
