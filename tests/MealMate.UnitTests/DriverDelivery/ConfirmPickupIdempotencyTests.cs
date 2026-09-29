using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class ConfirmPickupIdempotencyTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task SameKey_SamePayload_ReturnsCachedResponse_WithoutDuplicatingHandovers()
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
            Code = "TRP-7777",
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
            OrderCode = "#BX-7777",
            CustomerMaskedId = "CUST-303",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "عمر الشمري",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00-14:00",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var rawToken = "idempotent-test-token";
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
            StorageKey = "photos/idem.jpg",
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

        var idempotencyKey = Guid.NewGuid().ToString();
        var command = new ConfirmPickupBoxCommand(
            BoxId: day.Id,
            ValidationToken: rawToken,
            ConditionPhotoStorageKey: "photos/idem.jpg",
            Latitude: 24.77m,
            Longitude: 46.73m,
            IdempotencyKey: idempotencyKey);

        // First call
        var firstResult = await handler.Handle(command, CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Second call with same key and same payload
        var secondResult = await handler.Handle(command, CancellationToken.None);
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.BoxId.Should().Be(firstResult.Value.BoxId);
        secondResult.Value.Status.Should().Be("PickedUp");

        // Assert exactly one OrderHandover exists
        var handovers = await db.OrderHandovers.Where(h => h.DailyOrderId == dailyOrder.Id).ToListAsync();
        handovers.Should().HaveCount(1);

        // Assert exactly one IdempotencyRecord exists
        var idempotencyRecords = await db.IdempotencyRecords.Where(r => r.Key == idempotencyKey).ToListAsync();
        idempotencyRecords.Should().HaveCount(1);
    }

    [Fact]
    public async Task SameKey_DifferentPayload_ReturnsConflictPayloadMismatch()
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

        var idempotencyKey = Guid.NewGuid().ToString();
        var idempotencyScope = $"PickupConfirm:{driver.Id}";

        // Seed an existing idempotency record with a different payload hash
        var existingRecord = new IdempotencyRecord
        {
            Scope = idempotencyScope,
            Key = idempotencyKey,
            RequestHash = "different-hash-value-12345",
            ResponseBody = "{}",
            HttpStatusCode = 200,
            UserId = driverUserId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };
        db.IdempotencyRecords.Add(existingRecord);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ConfirmPickupBoxCommandHandler(db, currentUser, messages);

        var command = new ConfirmPickupBoxCommand(
            BoxId: Guid.NewGuid(),
            ValidationToken: "some-token",
            ConditionPhotoStorageKey: "photos/other.jpg",
            Latitude: 24.77m,
            Longitude: 46.73m,
            IdempotencyKey: idempotencyKey);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("IDEMPOTENCY_KEY_REUSED", "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH");
    }
}
