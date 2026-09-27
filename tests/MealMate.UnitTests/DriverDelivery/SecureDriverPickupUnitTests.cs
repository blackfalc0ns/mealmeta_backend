using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class SecureDriverPickupUnitTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<MealMate.Application.Common.Abstractions.IDomainEventDispatcher>());
    }

    [Fact]
    public async Task AppDbContext_CanPersistAndQuery_PickupValidation_And_PickupConditionPhoto()
    {
        await using var db = CreateDb();
        var tripId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var driverProfileId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var validation = new PickupValidation
        {
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            DriverAssignmentId = assignmentId,
            BarcodeValue = "480K-1234",
            TokenHash = "mock-sha256-hash",
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            IsManualEntry = false
        };
        db.PickupValidations.Add(validation);

        var photo = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            StorageKey = "uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 204800,
            Checksum = "mock-sha256",
            UploadedAtUtc = DateTime.UtcNow
        };
        db.PickupConditionPhotos.Add(photo);

        await db.SaveChangesAsync();

        var queriedValidation = await db.PickupValidations.FirstOrDefaultAsync(v => v.Id == validation.Id);
        queriedValidation.Should().NotBeNull();
        queriedValidation!.TokenHash.Should().Be("mock-sha256-hash");
        queriedValidation.ConsumedAtUtc.Should().BeNull();

        var queriedPhoto = await db.PickupConditionPhotos.FirstOrDefaultAsync(p => p.Id == photo.Id);
        queriedPhoto.Should().NotBeNull();
        queriedPhoto!.StorageKey.Should().Be("uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg");
        queriedPhoto.ConfirmedAtUtc.Should().BeNull();
    }

    private static TripStop CreateStop(string orderCode, TripStopStatus status = TripStopStatus.Pending, int boxCount = 1)
    {
        return new TripStop
        {
            OrderCode = orderCode,
            CustomerMaskedId = "CUST-" + Guid.NewGuid().ToString("N")[..6],
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "محمد علي",
            MealSummaryAr = "وجبة غداء",
            MealSummaryEn = "Lunch Meal",
            SlotLabel = "غداء",
            Status = status,
            BoxCount = boxCount
        };
    }

    [Fact]
    public async Task ValidateBarcode_WhenValid_ReturnsToken_And_PersistsSha256Hash()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new MealMate.Domain.IdentityAccess.DriverProfile
        {
            UserId = driverUserId,
            RestaurantId = restaurantId,
            FullNameAr = "خالد المحمد",
            FullNameEn = "Khaled Al-Mohammad",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-901",
            Status = TripStatus.Assigned,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        var assignment = new DriverAssignment
        {
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        };
        trip.DriverAssignments.Add(assignment);
        var stop = new TripStop
        {
            OrderCode = "480K-1234",
            CustomerMaskedId = "CUST-1",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "محمد علي",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 Meals",
            SlotLabel = "غداء",
            Status = TripStopStatus.Pending,
            BoxCount = 3
        };
        trip.Stops.Add(stop);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        // Act
        var command = new ValidatePickupBarcodeCommand("480K-1234", IsManualEntry: false);
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var response = result.Value;
        response.IsValid.Should().BeTrue();
        response.ValidationToken.Should().NotBeNullOrWhiteSpace();
        response.ScanStatus.Should().Be("BarcodeValidated");
        response.NextAction.Should().Be("CaptureConditionPhoto");
        response.Box.BoxId.Should().Be(stop.Id);
        response.Box.BoxCode.Should().Be("480K-1234");
        response.Box.CustomerName.Should().Be("محمد علي");
        response.Box.DeliveryZone.Should().Be("Hawally");
        response.Box.MealsCount.Should().Be(3);

        // Assert DB audit record stores hash, not raw token
        var record = await db.PickupValidations.FirstOrDefaultAsync(v => v.BoxId == stop.Id);
        record.Should().NotBeNull();
        record!.TokenHash.Should().NotBe(response.ValidationToken);
        record.DriverProfileId.Should().Be(driver.Id);
        record.DriverAssignmentId.Should().Be(assignment.Id);
    }

    [Fact]
    public async Task ValidateBarcode_WhenBoxBelongsToAnotherDriver_Returns_BoxNotAssignedToDriver()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var otherDriverId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var myDriver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائقي", FullNameEn = "My Driver" };
        var otherDriver = new MealMate.Domain.IdentityAccess.DriverProfile { RestaurantId = restaurantId, FullNameAr = "سائق آخر", FullNameEn = "Other Driver" };
        db.DriverProfiles.AddRange(myDriver, otherDriver);

        var otherTrip = new Trip { RestaurantId = restaurantId, Code = "TRP-888", Status = TripStatus.Assigned };
        otherTrip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = otherDriver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
        otherTrip.Stops.Add(CreateStop("999K-9999", TripStopStatus.Pending));
        db.Trips.Add(otherTrip);

        // My active trip
        var myTrip = new Trip { RestaurantId = restaurantId, Code = "TRP-111", Status = TripStatus.Assigned };
        myTrip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = myDriver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
        db.Trips.Add(myTrip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        var result = await handler.Handle(new ValidatePickupBarcodeCommand("999K-9999"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BOX_NOT_ASSIGNED_TO_DRIVER");
    }

    [Fact]
    public async Task ValidateBarcode_WhenBoxAlreadyPickedUp_Returns_BoxAlreadyPickedUp()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
        trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
        var stop = CreateStop("111K-0001", TripStopStatus.PickedUp);
        trip.Stops.Add(stop);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        var result = await handler.Handle(new ValidatePickupBarcodeCommand("111K-0001"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BOX_ALREADY_PICKED_UP");
    }

    [Fact]
    public async Task UploadPhoto_WhenTokenValidAndJpegSignature_SavesToStorage_And_RecordsConditionPhoto()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
        var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        trip.DriverAssignments.Add(assignment);
        var stop = CreateStop("BOX-777", TripStopStatus.Pending);
        trip.Stops.Add(stop);
        db.Trips.Add(trip);

        var rawToken = "my-secret-token-123456";
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        var validation = new PickupValidation
        {
            TripId = trip.Id,
            BoxId = stop.Id,
            DriverProfileId = driver.Id,
            DriverAssignmentId = assignment.Id,
            BarcodeValue = "BOX-777",
            TokenHash = tokenHash,
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };
        db.PickupValidations.Add(validation);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var storage = Substitute.For<IFileStorage>();
        var expectedKey = $"uploads/drivers/delivery/pickup-condition/{trip.Id}/{stop.Id}/photo.jpg";
        storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), "image/jpeg", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile(expectedKey, "photo.jpg", "image/jpeg", 1024, "hash"));

        var messages = NullLocalizedMessageProvider.Instance;
        var handler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, messages);

        // JPEG magic bytes: FF D8 FF
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        using var stream = new MemoryStream(jpegBytes);

        var command = new UploadPickupConditionPhotoCommand(
            BoxId: stop.Id,
            ValidationToken: rawToken,
            FileName: "photo.jpg",
            ContentType: "image/jpeg",
            ContentStream: stream);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BoxId.Should().Be(stop.Id);
        result.Value.StorageKey.Should().Be(expectedKey);
        result.Value.ScanStatus.Should().Be("PhotoUploaded");
        result.Value.NextAction.Should().Be("ConfirmPickup");

        var photoRow = await db.PickupConditionPhotos.FirstOrDefaultAsync(p => p.BoxId == stop.Id);
        photoRow.Should().NotBeNull();
        photoRow!.StorageKey.Should().Be(expectedKey);
    }

    [Fact]
    public async Task UploadPhoto_WhenInvalidMagicBytes_Returns_PhotoInvalidSignature()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var driver = new MealMate.Domain.IdentityAccess.DriverProfile { UserId = driverUserId, RestaurantId = restaurantId, FullNameAr = "سائق", FullNameEn = "Driver" };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = restaurantId, Code = "TRP-101", Status = TripStatus.Assigned };
        var assignment = new DriverAssignment { DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow };
        trip.DriverAssignments.Add(assignment);
        var stop = CreateStop("BOX-FAKE", TripStopStatus.Pending);
        trip.Stops.Add(stop);
        db.Trips.Add(trip);

        var rawToken = "raw-tok";
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        var validation = new PickupValidation
        {
            TripId = trip.Id,
            BoxId = stop.Id,
            DriverProfileId = driver.Id,
            DriverAssignmentId = assignment.Id,
            BarcodeValue = "BOX-FAKE",
            TokenHash = tokenHash,
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };
        db.PickupValidations.Add(validation);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var storage = Substitute.For<IFileStorage>();
        var handler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, NullLocalizedMessageProvider.Instance);

        // Text file bytes masquerading as image/jpeg
        var fakeBytes = System.Text.Encoding.UTF8.GetBytes("Not an image header");
        using var stream = new MemoryStream(fakeBytes);

        var command = new UploadPickupConditionPhotoCommand(validation.BoxId, rawToken, "fake.jpg", "image/jpeg", stream);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PHOTO_INVALID_SIGNATURE");
    }
}
