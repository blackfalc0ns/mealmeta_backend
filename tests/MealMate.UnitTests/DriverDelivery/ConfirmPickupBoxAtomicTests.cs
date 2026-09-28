using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class ConfirmPickupBoxAtomicTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ConfirmPickup_AtomicallyUpdates_AllAggregatesAndInsertsHandover()
    {
        // Arrange
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "سائق معتمد",
            FullNameEn = "Certified Driver",
            VehicleType = "car",
            VehiclePlate = "12345"
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-9999",
            Status = TripStatus.Assigned,
            PlannedDate = today
        };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        };
        db.DriverAssignments.Add(assignment);

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

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = dailyOrder.Id,
            MealBarcodeId = barcode.Id,
            OrderCode = "#BX-9999",
            CustomerMaskedId = "CUST-101",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "فهد الأحمد",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00-14:00",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var rawToken = "my-secret-validation-token";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        var validation = new PickupValidation
        {
            TripId = trip.Id,
            BoxId = day.Id,
            DriverProfileId = driver.Id,
            DriverAssignmentId = assignment.Id,
            BarcodeValue = barcode.BarcodeValue,
            TokenHash = tokenHash,
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            IsManualEntry = false
        };
        db.PickupValidations.Add(validation);

        var photo = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = trip.Id,
            BoxId = day.Id,
            DriverProfileId = driver.Id,
            StorageKey = "photos/valid.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            Checksum = "sha256",
            UploadedAtUtc = DateTime.UtcNow
        };
        db.PickupConditionPhotos.Add(photo);

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ConfirmPickupBoxCommandHandler(db, currentUser, messages);

        var command = new ConfirmPickupBoxCommand(
            BoxId: day.Id,
            ValidationToken: rawToken,
            ConditionPhotoStorageKey: "photos/valid.jpg",
            Latitude: 24.77m,
            Longitude: 46.73m,
            IdempotencyKey: Guid.NewGuid().ToString());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // 1. Assert TripStop is PickedUp
        var reloadedStop = await db.TripStops.FindAsync(stop.Id);
        reloadedStop.Should().NotBeNull();
        reloadedStop!.Status.Should().Be(TripStopStatus.PickedUp);

        // 2. Assert SubscriptionDay is OutForDelivery
        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay.Should().NotBeNull();
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.OutForDelivery);

        // 3. Assert RestaurantDailyOrder is OnTheWay
        var reloadedOrder = await db.RestaurantDailyOrders.FindAsync(dailyOrder.Id);
        reloadedOrder.Should().NotBeNull();
        reloadedOrder!.Status.Should().Be(OrderStatus.OnTheWay);

        // 4. Assert OrderHandover is Persisted
        var handover = await db.OrderHandovers.FirstOrDefaultAsync(h => h.DailyOrderId == dailyOrder.Id);
        handover.Should().NotBeNull();
        handover!.Status.Should().Be("Completed");
        handover.AttemptNumber.Should().Be(1);
    }
}
