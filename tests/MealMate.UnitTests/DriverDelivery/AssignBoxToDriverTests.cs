using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
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
            VehiclePlate = "12345",
            VerificationStatus = VerificationStatus.Approved
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

        // Verify Outbox message was persisted for DriverBoxAssigned event
        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "DriverBoxAssigned");
        outboxMessage.Should().NotBeNull();
        outboxMessage!.PayloadJson.Should().Contain("9999");
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
            VehiclePlate = "54321",
            VerificationStatus = VerificationStatus.Approved
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

        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
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

        // Legacy days without a delivery region fall back to the address line prefixed as a zone.
        createdStop.ZoneNameAr.Should().Be("منطقة السالمية قطعة 2");
        createdStop.FullAddress.Should().Be("السالمية قطعة 2");

        // Verify DailyOrder status updated
        dailyOrder.Status.Should().Be(OrderStatus.AssignedToDriver);

        // Verify DriverAssignment was created
        var assignment = await db.DriverAssignments.FirstOrDefaultAsync(da => da.TripId == createdTrip.Id && da.DriverProfileId == driver.Id);
        assignment.Should().NotBeNull();

        // Verify Outbox message was persisted for DriverBoxAssigned event
        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "DriverBoxAssigned");
        outboxMessage.Should().NotBeNull();
        outboxMessage!.PayloadJson.Should().Contain(driver.Id.ToString());
    }

    [Fact]
    public async Task Handle_AssignsSubscriptionDay_ZoneUsesRegionName_AndSnapshotsFullAddressAndCustomer()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق المنطقة",
            FullNameEn = "Zone Driver",
            VehiclePlate = "77777",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        var region = new Region
        {
            Code = "HAW",
            NameAr = "حولي",
            NameEn = "Hawalli"
        };
        db.Regions.Add(region);

        var customerUserId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = customerUserId,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(15m, "KWD")
        };
        db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = restaurantId,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryRegionId = region.Id,
            DeliveryAddressLine = "Hawalli, Salmiya, Block 4, شارع 1, 22"
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

        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users
            .GetUserIdentitiesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<UserIdentityDto>>(new List<UserIdentityDto>
            {
                new(customerUserId, "فاطمة العنزي", "fatma@example.com", "+96550001111")
            }));

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier, users: users);

        // Act
        var result = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver.Id, "إسناد مباشر"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var createdStop = await db.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id);
        createdStop.Should().NotBeNull();

        // The zone must be the region name only, never the full address line.
        createdStop!.ZoneNameAr.Should().Be("حولي");
        createdStop.ZoneNameEn.Should().Be("Hawalli");
        createdStop.ZoneNameAr.Should().NotContain("Block");

        // The full address is snapshoted separately for the map/directions pages.
        createdStop.FullAddress.Should().Be("Hawalli, Salmiya, Block 4, شارع 1, 22");

        // Customer identity is resolved onto the stop for the driver manifest (name only).
        createdStop.CustomerName.Should().Be("فاطمة العنزي");

        // The customer's phone number must never be stored or shown to the driver.
        createdStop.CustomerPhone.Should().BeNull();
    }

    [Fact]
    public async Task AssignBox_WhenVehicleBoxCapacityExceeded_ReturnsError()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق دراجة",
            FullNameEn = "Motorcycle Driver",
            VehicleType = "motorcycle", // Max capacity = 5
            VehiclePlate = "M-999",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Pre-existing trip with active stops having 4 boxes
        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-EXISTING",
            Status = TripStatus.Assigned,
            PlannedDate = today
        };
        db.Trips.Add(trip);

        var existingAssignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        };
        db.DriverAssignments.Add(existingAssignment);

        var existingStop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-001",
            CustomerMaskedId = "CUST-001",
            ZoneNameAr = "منطقة حولي",
            ZoneNameEn = "Hawally Zone",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "12:00-14:00 م",
            BoxCount = 4,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(existingStop);

        // New day with 2 boxes (4 + 2 = 6 > 5)
        var newDay = new SubscriptionDay
        {
            RestaurantId = restaurantId,
            ServiceDate = today,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(newDay);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = newDay.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 2
        };
        db.RestaurantDailyOrders.Add(dailyOrder);

        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier);

        // Act: Assign newDay (2 boxes) to driver who already has 4 boxes (total 6 > max 5)
        var result = await handler.Handle(new AssignBoxToDriverCommand(newDay.Id, driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VEHICLE_CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task AssignBox_BindsMealBarcodeId_WhenMealBarcodeExists()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق سيارة",
            FullNameEn = "Car Driver",
            VehicleType = "car",
            VehiclePlate = "C-123",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var day = new SubscriptionDay
        {
            RestaurantId = restaurantId,
            ServiceDate = today,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressLine = "حولي"
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 1
        };
        db.RestaurantDailyOrders.Add(dailyOrder);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"MM-{today:yyyyMMdd}-{day.Id:N}",
            Status = MealBarcodeStatus.Generated
        };
        db.MealBarcodes.Add(barcode);

        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier);

        // Act
        var result = await handler.Handle(new AssignBoxToDriverCommand(day.Id, driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var stop = await db.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id);
        stop.Should().NotBeNull();
        stop!.MealBarcodeId.Should().Be(barcode.Id);
    }
}
