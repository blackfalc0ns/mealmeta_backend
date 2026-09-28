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

public class ValidatePickupBarcodeTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ValidateBarcode_WithPrintedCanonicalFormat_MatchesCorrectly()
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
            VehiclePlate = "11111"
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-8888",
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
        var dayId = day.Id;

        var barcode = new MealBarcode
        {
            SubscriptionDayId = dayId,
            BarcodeValue = $"MM-{today:yyyyMMdd}-{dayId:N}",
            Status = MealBarcodeStatus.Generated
        };
        db.MealBarcodes.Add(barcode);

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = dayId,
            MealBarcodeId = barcode.Id,
            OrderCode = "#BX-8888",
            CustomerMaskedId = "CUST-999",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "علي سالم",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        // Act: Driver scans the canonical barcode printed on the box
        var result = await handler.Handle(
            new ValidatePickupBarcodeCommand(barcode.BarcodeValue, IsManualEntry: false),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BoxId.Should().Be(stop.SubscriptionDayId!.Value);
        result.Value.ValidationToken.Should().NotBeNullOrWhiteSpace();
        result.Value.ScanStatus.Should().Be("BarcodeValidated");
        result.Value.NextAction.Should().Be("CaptureConditionPhoto");
    }

    [Fact]
    public async Task ValidateBarcode_WhenBarcodeRevoked_ReturnsConflict()
    {
        // Arrange
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new DriverProfile
        {
            UserId = driverUserId,
            FullNameAr = "سائق",
            FullNameEn = "Driver"
        };
        db.DriverProfiles.Add(driver);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-REV",
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

        var dayId = Guid.NewGuid();
        var barcode = new MealBarcode
        {
            SubscriptionDayId = dayId,
            BarcodeValue = $"MM-{today:yyyyMMdd}-{dayId:N}",
            Status = MealBarcodeStatus.Revoked,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcode);

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = dayId,
            MealBarcodeId = barcode.Id,
            OrderCode = "#BX-REV",
            CustomerMaskedId = "CUST-REV",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(
            new ValidatePickupBarcodeCommand(barcode.BarcodeValue),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BARCODE_REVOKED");
    }
}
