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

public class BulkAssignBoxesCommandTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenBoxIdsIsEmpty()
    {
        var validator = new BulkAssignBoxesCommandValidator();
        var command = new BulkAssignBoxesCommand([], Guid.NewGuid());
        var result = validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BoxIds");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenBoxIdsExceeds25()
    {
        var validator = new BulkAssignBoxesCommandValidator();
        var boxIds = Enumerable.Range(0, 26).Select(_ => Guid.NewGuid()).ToList();
        var command = new BulkAssignBoxesCommand(boxIds, Guid.NewGuid());
        var result = validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BoxIds");
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenDriverIdIsEmpty()
    {
        var validator = new BulkAssignBoxesCommandValidator();
        var command = new BulkAssignBoxesCommand([Guid.NewGuid()], Guid.Empty);
        var result = validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DriverId");
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDriverDoesNotExist()
    {
        await using var db = CreateDb();
        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new BulkAssignBoxesCommandHandler(db, messages, pushNotifier);

        var result = await handler.Handle(
            new BulkAssignBoxesCommand([Guid.NewGuid()], Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Dispatcher.DriverNotFound);
    }

    [Fact]
    public async Task Handle_Assigns5Boxes_CreatesSingleTripWithSequentialStops_AndNotifiesDriver()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "خالد العتيبي",
            FullNameEn = "Khaled Al-Otaibi",
            VehiclePlate = "78901",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var boxIds = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var day = new SubscriptionDay
            {
                RestaurantId = restaurantId,
                ServiceDate = today,
                Status = SubscriptionDayStatus.ConfirmedByRestaurant,
                DeliveryAddressLine = $"منطقة حولي قطعة {i + 1}"
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
            db.MealBarcodes.Add(new MealBarcode
            {
                SubscriptionDayId = day.Id,
                OrderId = dailyOrder.Id,
                BarcodeValue = $"BC-BULK-{i + 1}",
                Status = MealBarcodeStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            boxIds.Add(day.Id);
        }
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new BulkAssignBoxesCommandHandler(db, messages, pushNotifier);

        // Act
        var result = await handler.Handle(
            new BulkAssignBoxesCommand(boxIds, driver.Id, "وجبات الوردية الصباحية"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var response = result.Value;
        response.AssignedCount.Should().Be(5);
        response.DriverId.Should().Be(driver.Id);
        response.DriverName.Should().Be("خالد العتيبي");
        response.AssignedBoxIds.Should().BeEquivalentTo(boxIds);
        response.TripCode.Should().StartWith("TRP-");

        // Verify Trip was created
        var trip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == response.TripId);
        trip.Should().NotBeNull();
        trip!.Status.Should().Be(TripStatus.Assigned);
        trip.Stops.Should().HaveCount(5);

        // Verify sequential stop numbers 1, 2, 3, 4, 5
        var sequenceNumbers = trip.Stops.Select(s => s.SequenceNumber).OrderBy(x => x).ToList();
        sequenceNumbers.Should().Equal(1, 2, 3, 4, 5);

        // Verify all 5 SubscriptionDays transitioned to Preparing
        var updatedDays = await db.SubscriptionDays.Where(d => boxIds.Contains(d.Id)).ToListAsync();
        updatedDays.Should().OnlyContain(d => d.Status == SubscriptionDayStatus.Preparing);

        // Verify all 5 DailyOrders transitioned to AssignedToDriver
        var updatedOrders = await db.RestaurantDailyOrders.Where(o => boxIds.Contains(o.SubscriptionDayId)).ToListAsync();
        updatedOrders.Should().OnlyContain(o => o.Status == OrderStatus.AssignedToDriver);

        // Verify DriverAssignment was created
        var assignment = await db.DriverAssignments.FirstOrDefaultAsync(da => da.TripId == trip.Id && da.DriverProfileId == driver.Id);
        assignment.Should().NotBeNull();
        assignment!.Status.Should().Be("Active");

        // Verify Outbox messages were persisted for assigned boxes
        var outboxMessages = await db.OutboxMessages.Where(m => m.EventType == "DriverBoxAssigned").ToListAsync();
        outboxMessages.Should().HaveCount(5);
    }

    [Fact]
    public async Task AssignBox_WhenVehicleBoxCapacityExceeded_ReturnsError()
    {
        // Arrange: Motorcycle driver with capacity limit = 5
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            FullNameAr = "سائق دراجة",
            FullNameEn = "Motorcycle Driver",
            VehicleType = "motorcycle",
            VehiclePlate = "M-123",
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var boxIds = new List<Guid>();

        // 2 stops, but each stop has 3 boxes! Total requested boxes = 6 > max 5!
        for (var i = 0; i < 2; i++)
        {
            var day = new SubscriptionDay
            {
                RestaurantId = restaurantId,
                ServiceDate = today,
                Status = SubscriptionDayStatus.Preparing
            };
            db.SubscriptionDays.Add(day);

            var dailyOrder = new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = day.Id,
                ServiceDate = today,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 3
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

            boxIds.Add(day.Id);
        }
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new BulkAssignBoxesCommandHandler(db, messages, pushNotifier);

        // Act: Assign 2 stops (total 6 boxes) to motorcycle (max 5)
        var result = await handler.Handle(
            new BulkAssignBoxesCommand(boxIds, driver.Id),
            CancellationToken.None);

        // Assert: Must fail with VEHICLE_CAPACITY_EXCEEDED
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VEHICLE_CAPACITY_EXCEEDED");
    }
}
