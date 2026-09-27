using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.DriverDelivery;

public class SecureDriverPickupIntegrationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
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
    public async Task EndToEnd_SecurePickupFlow_SequentialExecution_Succeeds()
    {
        await using var db = CreateDb();
        var driverUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driver = new MealMate.Domain.IdentityAccess.DriverProfile
        {
            UserId = driverUserId,
            RestaurantId = restaurantId,
            FullNameAr = "سائق تجريبي",
            FullNameEn = "Test Driver"
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-E2E-01",
            Status = TripStatus.Assigned
        };
        var assignment = new DriverAssignment
        {
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        };
        trip.DriverAssignments.Add(assignment);

        var stop1 = CreateStop("BX-E2E-01", TripStopStatus.Pending);
        var stop2 = CreateStop("BX-E2E-02", TripStopStatus.Pending);
        trip.Stops.Add(stop1);
        trip.Stops.Add(stop2);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driverUserId);

        var storage = Substitute.For<IFileStorage>();
        storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var fileName = callInfo.ArgAt<string>(1);
                var contentType = callInfo.ArgAt<string>(2);
                var folder = callInfo.ArgAt<string>(3);
                var key = $"{folder}/{Guid.NewGuid():N}.jpg";
                return new StoredFile(key, fileName, contentType, 2048, "hash-123");
            });

        var messages = NullLocalizedMessageProvider.Instance;

        var validateHandler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);
        var uploadHandler = new UploadPickupConditionPhotoCommandHandler(db, currentUser, storage, messages);
        var confirmHandler = new ConfirmPickupBoxCommandHandler(db, currentUser, messages);
        var summaryHandler = new GetTripPickupSummaryQueryHandler(db, currentUser, messages);
        var startHandler = new StartTripCommandHandler(db, currentUser, messages);

        // 1. Initial State: Resumable summary shows 0/2 picked up, Cannot start trip
        var initSummary = await summaryHandler.Handle(new GetTripPickupSummaryQuery(trip.Id), CancellationToken.None);
        initSummary.IsSuccess.Should().BeTrue();
        initSummary.Value.AssignedBoxesCount.Should().Be(2);
        initSummary.Value.ReceivedBoxesCount.Should().Be(0);
        initSummary.Value.RemainingBoxesCount.Should().Be(2);
        initSummary.Value.CanStartTrip.Should().BeFalse();

        // 2. Cannot start trip while unscanned boxes remain
        var earlyStartKey = Guid.NewGuid().ToString();
        var earlyStartResult = await startHandler.Handle(new StartTripCommand(trip.Id, 29.33m, 48.07m, earlyStartKey), CancellationToken.None);
        earlyStartResult.IsSuccess.Should().BeFalse();
        earlyStartResult.Error.Code.Should().Be("UNSCANNED_BOXES_EXIST");

        // 3. Validate Box 1 Barcode
        var valResult1 = await validateHandler.Handle(new ValidatePickupBarcodeCommand("BX-E2E-01"), CancellationToken.None);
        valResult1.IsSuccess.Should().BeTrue();
        var token1 = valResult1.Value.ValidationToken;
        valResult1.Value.NextAction.Should().Be("CaptureConditionPhoto");

        // 4. Upload Condition Photo for Box 1 (Valid JPEG magic bytes)
        using var jpegStream1 = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });
        var uploadResult1 = await uploadHandler.Handle(new UploadPickupConditionPhotoCommand(
            stop1.Id,
            token1,
            "box1.jpg",
            "image/jpeg",
            jpegStream1), CancellationToken.None);
        uploadResult1.IsSuccess.Should().BeTrue();
        var storageKey1 = uploadResult1.Value.StorageKey;
        uploadResult1.Value.NextAction.Should().Be("ConfirmPickup");

        // 5. Confirm Box 1 Pickup
        var confirmKey1 = Guid.NewGuid().ToString();
        var confirmResult1 = await confirmHandler.Handle(new ConfirmPickupBoxCommand(
            stop1.Id,
            token1,
            storageKey1,
            29.3375m,
            48.0280m,
            confirmKey1), CancellationToken.None);
        confirmResult1.IsSuccess.Should().BeTrue();
        confirmResult1.Value.Status.Should().Be("PickedUp");
        confirmResult1.Value.ReceivedBoxesCount.Should().Be(1);
        confirmResult1.Value.RemainingBoxesCount.Should().Be(1);
        confirmResult1.Value.AllBoxesReceived.Should().BeFalse();
        confirmResult1.Value.NextAction.Should().Be("ShowBoxSuccess");

        // 6. Idempotency Replay on Confirm Box 1 (same key and payload returns identical result)
        var replayConfirmResult1 = await confirmHandler.Handle(new ConfirmPickupBoxCommand(
            stop1.Id,
            token1,
            storageKey1,
            29.3375m,
            48.0280m,
            confirmKey1), CancellationToken.None);
        replayConfirmResult1.IsSuccess.Should().BeTrue();
        replayConfirmResult1.Value.BoxId.Should().Be(stop1.Id);
        replayConfirmResult1.Value.Status.Should().Be("PickedUp");

        // 7. Reusing same Idempotency-Key with different payload fails
        var conflictKeyResult = await confirmHandler.Handle(new ConfirmPickupBoxCommand(
            stop1.Id,
            token1,
            "uploads/different_key.jpg",
            29.99m,
            48.99m,
            confirmKey1), CancellationToken.None);
        conflictKeyResult.IsSuccess.Should().BeFalse();
        conflictKeyResult.Error.Code.Should().Be("IDEMPOTENCY_KEY_REUSED");

        // 8. Progress check: Trip summary shows 1/2 picked up, still cannot start trip
        var midSummary = await summaryHandler.Handle(new GetTripPickupSummaryQuery(trip.Id), CancellationToken.None);
        midSummary.IsSuccess.Should().BeTrue();
        midSummary.Value.ReceivedBoxesCount.Should().Be(1);
        midSummary.Value.RemainingBoxesCount.Should().Be(1);
        midSummary.Value.CanStartTrip.Should().BeFalse();

        // 9. Process Box 2: Validate -> Upload -> Confirm
        var valResult2 = await validateHandler.Handle(new ValidatePickupBarcodeCommand("BX-E2E-02"), CancellationToken.None);
        valResult2.IsSuccess.Should().BeTrue();
        var token2 = valResult2.Value.ValidationToken;

        using var pngStream2 = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var uploadResult2 = await uploadHandler.Handle(new UploadPickupConditionPhotoCommand(
            stop2.Id,
            token2,
            "box2.png",
            "image/png",
            pngStream2), CancellationToken.None);
        uploadResult2.IsSuccess.Should().BeTrue();
        var storageKey2 = uploadResult2.Value.StorageKey;

        var confirmKey2 = Guid.NewGuid().ToString();
        var confirmResult2 = await confirmHandler.Handle(new ConfirmPickupBoxCommand(
            stop2.Id,
            token2,
            storageKey2,
            29.3375m,
            48.0280m,
            confirmKey2), CancellationToken.None);
        confirmResult2.IsSuccess.Should().BeTrue();
        confirmResult2.Value.ReceivedBoxesCount.Should().Be(2);
        confirmResult2.Value.RemainingBoxesCount.Should().Be(0);
        confirmResult2.Value.AllBoxesReceived.Should().BeTrue();
        confirmResult2.Value.NextAction.Should().Be("ShowPickupSummary");

        // 10. Summary shows AllBoxesReceived = true and CanStartTrip = true
        var finalSummary = await summaryHandler.Handle(new GetTripPickupSummaryQuery(trip.Id), CancellationToken.None);
        finalSummary.IsSuccess.Should().BeTrue();
        finalSummary.Value.AllBoxesReceived.Should().BeTrue();
        finalSummary.Value.CanStartTrip.Should().BeTrue();
        finalSummary.Value.PickupStatus.Should().Be("ReadyToStart");
        finalSummary.Value.Boxes.Should().HaveCount(2);
        finalSummary.Value.Boxes.All(b => b.Status == "PickedUp").Should().BeTrue();

        // 11. Start Trip
        var startTripKey = Guid.NewGuid().ToString();
        var startResult = await startHandler.Handle(new StartTripCommand(
            trip.Id,
            29.3375m,
            48.0280m,
            startTripKey), CancellationToken.None);
        startResult.IsSuccess.Should().BeTrue();
        startResult.Value.Status.Should().Be("InProgress");
        startResult.Value.DispatchedBoxesCount.Should().Be(2);

        // 12. Idempotency Replay on Start Trip
        var replayStartResult = await startHandler.Handle(new StartTripCommand(
            trip.Id,
            29.3375m,
            48.0280m,
            startTripKey), CancellationToken.None);
        replayStartResult.IsSuccess.Should().BeTrue();
        replayStartResult.Value.Status.Should().Be("InProgress");

        // Verify DB Trip status
        var persistedTrip = await db.Trips.FindAsync(trip.Id);
        persistedTrip!.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public async Task Security_CrossDriverAccess_IsStrictlyPrevented()
    {
        await using var db = CreateDb();
        var driverAUserId = Guid.NewGuid();
        var driverBUserId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var driverA = new MealMate.Domain.IdentityAccess.DriverProfile
        {
            UserId = driverAUserId,
            RestaurantId = restaurantId,
            FullNameAr = "سائق أ",
            FullNameEn = "Driver A"
        };
        var driverB = new MealMate.Domain.IdentityAccess.DriverProfile
        {
            UserId = driverBUserId,
            RestaurantId = restaurantId,
            FullNameAr = "سائق ب",
            FullNameEn = "Driver B"
        };
        db.DriverProfiles.AddRange(driverA, driverB);

        var tripA = new Trip { RestaurantId = restaurantId, Code = "TRP-A", Status = TripStatus.Assigned };
        tripA.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driverA.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
        var stopA = CreateStop("BX-A", TripStopStatus.Pending);
        tripA.Stops.Add(stopA);

        var tripB = new Trip { RestaurantId = restaurantId, Code = "TRP-B", Status = TripStatus.Assigned };
        tripB.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driverB.Id, Status = "Active", AssignedAtUtc = DateTime.UtcNow });
        var stopB = CreateStop("BX-B", TripStopStatus.Pending);
        tripB.Stops.Add(stopB);

        db.Trips.AddRange(tripA, tripB);
        await db.SaveChangesAsync();

        var currentUserB = Substitute.For<ICurrentUser>();
        currentUserB.IsAuthenticated.Returns(true);
        currentUserB.UserId.Returns(driverBUserId);

        var messages = NullLocalizedMessageProvider.Instance;

        // Driver B attempts to validate Driver A's box
        var validateHandler = new ValidatePickupBarcodeCommandHandler(db, currentUserB, messages);
        var valResult = await validateHandler.Handle(new ValidatePickupBarcodeCommand("BX-A"), CancellationToken.None);
        valResult.IsSuccess.Should().BeFalse();
        valResult.Error.Code.Should().Be("BOX_NOT_ASSIGNED_TO_DRIVER");

        // Driver A validates Driver A's box and gets a legitimate token
        var currentUserA = Substitute.For<ICurrentUser>();
        currentUserA.IsAuthenticated.Returns(true);
        currentUserA.UserId.Returns(driverAUserId);
        var validateHandlerA = new ValidatePickupBarcodeCommandHandler(db, currentUserA, messages);
        var valResultA = await validateHandlerA.Handle(new ValidatePickupBarcodeCommand("BX-A"), CancellationToken.None);
        valResultA.IsSuccess.Should().BeTrue();
        var tokenA = valResultA.Value.ValidationToken;

        // Driver B attempts to upload condition photo for Driver A's box using Driver A's token
        var storage = Substitute.For<IFileStorage>();
        var uploadHandler = new UploadPickupConditionPhotoCommandHandler(db, currentUserB, storage, messages);
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });
        var upResult = await uploadHandler.Handle(new UploadPickupConditionPhotoCommand(stopA.Id, tokenA, "photo.jpg", "image/jpeg", stream), CancellationToken.None);
        upResult.IsSuccess.Should().BeFalse();
        upResult.Error.Code.Should().Be("VALIDATION_TOKEN_INVALID");

        // Driver B attempts to confirm Driver A's box using Driver A's token
        var confirmHandler = new ConfirmPickupBoxCommandHandler(db, currentUserB, messages);
        var confResult = await confirmHandler.Handle(new ConfirmPickupBoxCommand(stopA.Id, tokenA, "key.jpg", 29m, 48m, Guid.NewGuid().ToString()), CancellationToken.None);
        confResult.IsSuccess.Should().BeFalse();
        confResult.Error.Code.Should().Be("VALIDATION_TOKEN_INVALID");

        // Driver B attempts to start Driver A's trip
        var startHandler = new StartTripCommandHandler(db, currentUserB, messages);
        var startResult = await startHandler.Handle(new StartTripCommand(tripA.Id, 29m, 48m, Guid.NewGuid().ToString()), CancellationToken.None);
        startResult.IsSuccess.Should().BeFalse();
        startResult.Error.Code.Should().Be("DRIVER_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task Security_UnauthenticatedUser_IsRejectedAcrossAllEndpoints()
    {
        await using var db = CreateDb();
        var unauthenticatedUser = Substitute.For<ICurrentUser>();
        unauthenticatedUser.IsAuthenticated.Returns(false);
        unauthenticatedUser.UserId.Returns((Guid?)null);

        var messages = NullLocalizedMessageProvider.Instance;
        var storage = Substitute.For<IFileStorage>();

        var validateHandler = new ValidatePickupBarcodeCommandHandler(db, unauthenticatedUser, messages);
        var valResult = await validateHandler.Handle(new ValidatePickupBarcodeCommand("BX-01"), CancellationToken.None);
        valResult.IsSuccess.Should().BeFalse();
        valResult.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);

        var uploadHandler = new UploadPickupConditionPhotoCommandHandler(db, unauthenticatedUser, storage, messages);
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });
        var upResult = await uploadHandler.Handle(new UploadPickupConditionPhotoCommand(Guid.NewGuid(), "token", "photo.jpg", "image/jpeg", stream), CancellationToken.None);
        upResult.IsSuccess.Should().BeFalse();
        upResult.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);

        var confirmHandler = new ConfirmPickupBoxCommandHandler(db, unauthenticatedUser, messages);
        var confResult = await confirmHandler.Handle(new ConfirmPickupBoxCommand(Guid.NewGuid(), "token", "key", 29m, 48m, "key"), CancellationToken.None);
        confResult.IsSuccess.Should().BeFalse();
        confResult.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);

        var summaryHandler = new GetTripPickupSummaryQueryHandler(db, unauthenticatedUser, messages);
        var sumResult = await summaryHandler.Handle(new GetTripPickupSummaryQuery(Guid.NewGuid()), CancellationToken.None);
        sumResult.IsSuccess.Should().BeFalse();
        sumResult.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);

        var startHandler = new StartTripCommandHandler(db, unauthenticatedUser, messages);
        var startResult = await startHandler.Handle(new StartTripCommand(Guid.NewGuid(), 29m, 48m, "key"), CancellationToken.None);
        startResult.IsSuccess.Should().BeFalse();
        startResult.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
    }
}
