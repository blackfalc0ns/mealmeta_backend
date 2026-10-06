using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.KitchenCoordination;
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

public class DriverAppTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (ICurrentUser, ILocalizedMessageProvider) CreateMocks()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.CurrentCulture.Returns(new System.Globalization.CultureInfo("ar"));
        return (currentUser, messages);
    }

    private static (DriverDeliveryMapQueryHandler Handler, IGoogleRoutingService Routing) CreateMapHandler(
        AppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        var routing = Substitute.For<IGoogleRoutingService>();
        var navigation = new DriverDeliveryNavigationBuilder(
            routing,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            new MealMate.Infrastructure.Services.DateTimeProvider(),
            Microsoft.Extensions.Options.Options.Create(new DriverNavigationOptions()));
        return (new DriverDeliveryMapQueryHandler(db, currentUser, messages, navigation), routing);
    }

    [Fact]
    public async Task StartShift_Should_Create_WorkSession_And_Make_Driver_Available()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = false,
            VerificationStatus = VerificationStatus.Approved
        };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);

        var handler = new DriverShiftCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new StartDriverShiftCommand(driver.Id, 29.3375, 48.0280), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.IsAvailable.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Active");
        result.Value.DriverName.Should().Be("أحمد إبراهيم");

        var sessionInDb = await db.DriverWorkSessions.FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id);
        sessionInDb.Should().NotBeNull();
        sessionInDb!.Status.Should().Be("Active");
        sessionInDb.DailyTargetOrders.Should().Be(8);

        var updatedDriver = await db.DriverProfiles.FindAsync(driver.Id);
        updatedDriver!.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task EndShift_Should_Fail_When_Pending_Deliveries_Exist()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-101",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        };
        db.DriverAssignments.Add(assignment);

        var pendingStop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(pendingStop);
        await db.SaveChangesAsync();

        var handler = new DriverShiftCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new EndDriverShiftCommand(driver.Id, 29.33, 48.02, ForceEndWithPendingReturns: false), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Shift.PendingOrdersRemaining");
    }

    [Fact]
    public async Task EndShift_Should_Succeed_When_Deliveries_Completed()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var session = new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-5).AddMinutes(-45),
            Status = "Active",
            TotalDistanceKm = 32.4,
            CompletedOrdersCount = 8
        };
        db.DriverWorkSessions.Add(session);
        await db.SaveChangesAsync();

        var handler = new DriverShiftCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new EndDriverShiftCommand(driver.Id, 29.33, 48.02), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.TotalOrdersDelivered.Should().Be(8);
        result.Value.TotalDistanceKm.Should().Be(32.4);
        result.Value.CelebrationTitle.Should().Be("أحسنت العمل اليوم!");

        var updatedDriver = await db.DriverProfiles.FindAsync(driver.Id);
        updatedDriver!.IsAvailable.Should().BeFalse();

        var updatedSession = await db.DriverWorkSessions.FindAsync(session.Id);
        updatedSession!.Status.Should().Be("Ended");
    }

    [Fact]
    public async Task GetHomeScreen_Should_Return_Daily_Target_Progress_And_Task()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var session = new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-3),
            Status = "Active",
            DailyTargetOrders = 8,
            CompletedOrdersCount = 5,
            TotalDistanceKm = 32.4
        };
        db.DriverWorkSessions.Add(session);
        await db.SaveChangesAsync();

        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.DriverName.Should().Be("أحمد إبراهيم");
        result.Value.TargetProgress.CompletedCount.Should().Be(5);
        result.Value.TargetProgress.TargetCount.Should().Be(8);
        result.Value.TargetProgress.RemainingCount.Should().Be(3);
        result.Value.TodaySummary.TotalOrders.Should().Be(8);
        result.Value.TodaySummary.DeliveredOrders.Should().Be(5);
        result.Value.DailyPerformance.OnTimeRatePercentage.Should().Be(62.5);
        result.Value.DailyPerformance.DistanceCoveredKm.Should().Be(32.4);
    }

    [Fact]
    public async Task PickupBox_Should_Fail_When_Barcode_Missing()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Missing Barcode
        var result = await handler.Handle(new PickupBoxCommand(stop.Id, "", "photos/condition_1.jpg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.BarcodeRequired");
    }

    [Fact]
    public async Task PickupBox_Should_Fail_When_ConditionPhoto_Missing()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Missing Condition Photo
        var result = await handler.Handle(new PickupBoxCommand(stop.Id, "BX-458722", null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.ConditionPhotoRequired");
    }

    [Fact]
    public async Task PickupBox_Should_Fail_When_Barcode_Mismatch()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Mismatched Barcode
        var result = await handler.Handle(new PickupBoxCommand(stop.Id, "BX-999999", "photos/condition_1.jpg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.BarcodeMismatch");
    }

    [Fact]
    public async Task PickupBox_Should_Succeed_And_Record_PickupScan_When_Barcode_And_Photo_Valid()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-101",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Valid Barcode and Condition Photo
        var result = await handler.Handle(new PickupBoxCommand(
            BoxId: stop.Id,
            BarcodeValue: "BX-458722",
            ConditionPhotoStorageKey: "uploads/drivers/delivery/condition_box1.jpg",
            Latitude: 29.3375m,
            Longitude: 48.0280m), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Status.Should().Be("PickedUp");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.PickedUp);

        var updatedTrip = await db.Trips.FindAsync(trip.Id);
        updatedTrip!.Status.Should().Be(TripStatus.InProgress);

        var scan = await db.PickupScans.FirstOrDefaultAsync(s => s.TripId == trip.Id);
        scan.Should().NotBeNull();
        scan!.BarcodeValue.Should().Be("BX-458722");
        scan.PhotoPath.Should().Be("uploads/drivers/delivery/condition_box1.jpg");
    }

    [Fact]
    public async Task PickupBox_ManualEntry_Should_Fail_When_ManualBoxCode_Missing()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Manual Entry with empty code
        var result = await handler.Handle(new PickupBoxCommand(
            BoxId: stop.Id,
            IsManualEntry: true,
            ManualBoxCode: "",
            ConditionPhotoStorageKey: "uploads/drivers/labels/manual_box1.jpg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.ManualBoxCodeRequired");
    }

    [Fact]
    public async Task PickupBox_ManualEntry_Should_Fail_When_ConditionPhoto_Missing()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Manual Entry with code but without mandatory condition/label photo
        var result = await handler.Handle(new PickupBoxCommand(
            BoxId: stop.Id,
            IsManualEntry: true,
            ManualBoxCode: "BX-458722",
            ConditionPhotoStorageKey: null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.ManualEntryLabelPhotoRequired");
    }

    [Fact]
    public async Task PickupBox_ManualEntry_Should_Fail_When_ManualBoxCode_Mismatch()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Manual Entry with mismatched code
        var result = await handler.Handle(new PickupBoxCommand(
            BoxId: stop.Id,
            IsManualEntry: true,
            ManualBoxCode: "BX-999999",
            ConditionPhotoStorageKey: "uploads/drivers/labels/manual_box1.jpg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.ManualCodeMismatch");
    }

    [Fact]
    public async Task PickupBox_ManualEntry_Should_Succeed_And_Record_ManualAudit_When_Code_And_Photo_Valid()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-101",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Manual Entry with valid code and mandatory condition/label photo
        var result = await handler.Handle(new PickupBoxCommand(
            BoxId: stop.Id,
            IsManualEntry: true,
            ManualBoxCode: "BX-458722",
            ConditionPhotoStorageKey: "uploads/drivers/labels/manual_label_reviewed.jpg",
            ManualEntryReason: "Barcode damaged on plastic wrap",
            Latitude: 29.3375m,
            Longitude: 48.0280m), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Status.Should().Be("PickedUp");
        result.Value.IsManualEntry.Should().BeTrue();

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.PickedUp);

        var scan = await db.PickupScans.FirstOrDefaultAsync(s => s.TripId == trip.Id);
        scan.Should().NotBeNull();
        scan!.BarcodeValue.Should().Be("BX-458722");
        scan.PhotoPath.Should().Be("uploads/drivers/labels/manual_label_reviewed.jpg");
        scan.IsManualEntry.Should().BeTrue();
        scan.ManualEntryReason.Should().Be("Barcode damaged on plastic wrap");
    }

    [Fact]
    public async Task CompleteDelivery_Should_Fail_When_Barcode_Or_Photo_Missing()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        // The proof key must reference a real recorded upload (no driver identity
        // involved in this test, so no ownership check applies).
        db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = "photos/door.jpg",
            FileName = "door.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 512,
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act 1 - Mismatched Barcode fails
        var result1 = await handler.Handle(new CompleteBoxDeliveryCommand(stop.Id, BarcodeValue: "BX-999999", ProofPhotoStorageKey: "photos/door.jpg"), CancellationToken.None);
        result1.IsSuccess.Should().BeFalse();
        result1.Error.Code.Should().Be("Orders.BarcodeMismatch");

        // Act 2 - Missing Proof Photo fails
        var result2 = await handler.Handle(new CompleteBoxDeliveryCommand(stop.Id, BarcodeValue: "BX-458722", ProofPhotoStorageKey: null), CancellationToken.None);
        result2.IsSuccess.Should().BeFalse();
        result2.Error.Code.Should().Be("Orders.ProofPhotoRequired");

        // Act 3 - Delivery without barcode/code succeeds when photo is provided (Code is fully optional)
        var result3 = await handler.Handle(new CompleteBoxDeliveryCommand(stop.Id, BarcodeValue: null, ProofPhotoStorageKey: "photos/door.jpg"), CancellationToken.None);
        result3.IsSuccess.Should().BeTrue();

    }

    [Fact]
    public async Task CompleteDelivery_Should_Succeed_And_Record_DeliveryConfirmationScan()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var session = new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Status = "Active",
            DailyTargetOrders = 8,
            CompletedOrdersCount = 4
        };
        db.DriverWorkSessions.Add(session);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-101",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        });

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "4 وجبات",
            MealSummaryEn = "4 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = "uploads/drivers/delivery/proof_box1.jpg",
            FileName = "proof_box1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            UploadedByUserId = driver.UserId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act - Complete Delivery with Barcode and Proof Photo
        var result = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            BarcodeValue: "BX-458722",
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof_box1.jpg",
            Latitude: 29.3375m,
            Longitude: 48.0280m,
            DriverId: driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Status.Should().Be("Delivered");
        result.Value.TripId.Should().Be(trip.Id);
        result.Value.IsTripCompleted.Should().BeTrue();
        result.Value.RemainingStopsCount.Should().Be(0);
        result.Value.IsFirstDelivery.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.Delivered);

        var updatedSession = await db.DriverWorkSessions.FindAsync(session.Id);
        updatedSession!.CompletedOrdersCount.Should().Be(5); // incremented from 4 to 5

        var confScan = await db.DeliveryConfirmationScans.FirstOrDefaultAsync();
        confScan.Should().NotBeNull();
        confScan!.BarcodeValue.Should().Be("BX-458722");
        confScan.PhotoPath.Should().Be("uploads/drivers/delivery/proof_box1.jpg");
        confScan.Latitude.Should().Be(29.3375m);
        confScan.Longitude.Should().Be(48.0280m);
    }

    [Fact]
    public async Task GetDeliveryMap_Should_Return_Stops_Carousel_And_PolylineWaypoints()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = "TRP-101",
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        };
        db.DriverAssignments.Add(assignment);

        var stop1 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerName = "أحمد فيصل",
            CustomerPhone = "+966 50 111 2233",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:24 ص",
            SequenceNumber = 1,
            Status = TripStopStatus.Delivered,
            Latitude = 29.3375m,
            Longitude = 48.0225m
        };
        var stop2 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458622",
            CustomerName = "محمد علي",
            CustomerPhone = "+966 50 123 4567",
            CustomerMaskedId = "CUST-02",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            SequenceNumber = 2,
            Status = TripStopStatus.PickedUp,
            Latitude = 29.3380m,
            Longitude = 48.0230m
        };
        var stop3 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerName = "مهند أحمد",
            CustomerPhone = "+966 50 444 5566",
            CustomerMaskedId = "CUST-03",
            ZoneNameAr = "المنامة",
            ZoneNameEn = "Manama",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:24 ص",
            SequenceNumber = 3,
            Status = TripStopStatus.Pending,
            Latitude = 29.3390m,
            Longitude = 48.0240m
        };
        db.TripStops.AddRange(stop1, stop2, stop3);
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.TotalStopsCount.Should().Be(3);
        result.Value.CompletedStopsCount.Should().Be(1);
        result.Value.Stops.Should().HaveCount(3);

        // Focused stop should be the active InTransit stop (Stop 2: Mohammed Ali)
        result.Value.FocusedStop.Should().NotBeNull();
        result.Value.FocusedStop!.BoxCode.Should().Be("BX-458622");
        result.Value.FocusedStop.CustomerName.Should().Be("محمد علي");
        result.Value.FocusedStop.StatusText.Should().Be("خارج التوصيل");
        result.Value.FocusedStop.IsCurrent.Should().BeTrue();

        // Carousel items
        result.Value.Stops[0].SequenceBadge.Should().Be("1/3");
        result.Value.Stops[0].StatusText.Should().Be("تم التسليم");

        result.Value.Stops[1].SequenceBadge.Should().Be("2/3");
        result.Value.Stops[1].StatusText.Should().Be("خارج التوصيل");

        result.Value.Stops[2].SequenceBadge.Should().Be("3/3");
        result.Value.Stops[2].StatusText.Should().Be("في الطريق للعميل");

        // Route polyline waypoints should be generated
        result.Value.RoutePolylineWaypoints.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetDeliveryMap_Should_Support_FocusedStopId()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" };
        db.DriverAssignments.Add(assignment);

        var stop1 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-1",
            CustomerName = "العميل 1",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            SequenceNumber = 1,
            Status = TripStopStatus.Delivered
        };
        var stop2 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-2",
            CustomerName = "العميل 2",
            CustomerMaskedId = "CUST-02",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:24 ص",
            SequenceNumber = 2,
            Status = TripStopStatus.Pending
        };
        db.TripStops.AddRange(stop1, stop2);
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        // Act - Request explicitly focusing on stop 1
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id, FocusedStopId: stop1.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FocusedStop.Should().NotBeNull();
        result.Value.FocusedStop!.StopId.Should().Be(stop1.Id);
        result.Value.FocusedStop.CustomerName.Should().Be("العميل 1");
    }

    [Fact]
    public async Task GetDeliveryMap_Should_Include_Stops_From_All_Active_Trips()
    {
        // Arrange — stops added by the dispatcher to an older trip must stay visible
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var olderTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-501", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress, CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var newerTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-502", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(olderTrip, newerTrip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = olderTrip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = newerTrip.Id, DriverProfileId = driver.Id, Status = "Active" });

        db.TripStops.Add(new TripStop
        {
            TripId = olderTrip.Id, OrderCode = "#BX-1111", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات", MealSummaryEn = "3 meals", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.PickedUp,
            Latitude = 29.3375m, Longitude = 48.0225m
        });
        db.TripStops.Add(new TripStop
        {
            TripId = newerTrip.Id, OrderCode = "#BX-2222", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda",
            MealSummaryAr = "2 وجبة", MealSummaryEn = "2 meals", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.Pending,
            Latitude = 29.3390m, Longitude = 48.0240m
        });
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id), CancellationToken.None);

        // Assert — newest trip's stops come first in the route
        result.IsSuccess.Should().BeTrue();
        // The auto-selected active stop (first PickedUp) belongs to the older trip,
        // and the response trip must match the selected stop's trip — the card list
        // itself stays aggregated across both active trips.
        result.Value.TripId.Should().Be(olderTrip.Id);
        result.Value.TripCode.Should().Be("TRP-501");
        result.Value.TotalStopsCount.Should().Be(2);
        result.Value.Stops.Select(s => s.BoxCode).Should().Equal("#BX-2222", "#BX-1111");
        result.Value.Stops[0].SequenceBadge.Should().Be("1/2");
        result.Value.Stops[1].SequenceBadge.Should().Be("2/2");
    }

    [Fact]
    public async Task GetDeliveryMap_UnknownFocusedStopId_Returns404_StopNotFound_WithoutProviderCall()
    {
        // Arrange — a focusedStopId that is not one of the driver's own active stops
        // must not silently fall back to another stop (foreign / stale ids included)
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-701", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.InProgress };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.TripStops.Add(new TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-7001", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.Pending
        });
        await db.SaveChangesAsync();

        var (handler, routing) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id, FocusedStopId: Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DriverMap.StopNotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDeliveryMap_WithoutActiveTrips_Returns404_TripNotFound()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DriverTrip.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetDeliveryMap_FocusedStopFromOlderTrip_ReportsThatTrip_WhileKeepingAllCards()
    {
        // Arrange — the driver taps a card of an older trip: the response trip must
        // match that stop's trip, and every active-trip card stays in the carousel
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var olderTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-801", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress, CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var newerTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-802", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(olderTrip, newerTrip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = olderTrip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = newerTrip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var olderStop = new TripStop
        {
            TripId = olderTrip.Id, OrderCode = "#BX-8101", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.PickedUp,
            Latitude = 29.3375m, Longitude = 48.0225m
        };
        var newerStop = new TripStop
        {
            TripId = newerTrip.Id, OrderCode = "#BX-8201", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda",
            MealSummaryAr = "وجبتان", MealSummaryEn = "2 meals", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.Pending,
            Latitude = 29.3390m, Longitude = 48.0240m
        };
        db.TripStops.AddRange(olderStop, newerStop);
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id, FocusedStopId: olderStop.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().Be(olderTrip.Id);
        result.Value.TripCode.Should().Be("TRP-801");
        result.Value.FocusedStop!.StopId.Should().Be(olderStop.Id);
        result.Value.TotalStopsCount.Should().Be(2);
        result.Value.Stops.Select(s => s.StopId).Should().Contain(new[] { olderStop.Id, newerStop.Id });
    }

    [Fact]
    public async Task GetDeliveryMap_DeliveredFocusedStop_ReturnsCompletedNavigation_WithoutRoute()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-901", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.InProgress };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var deliveredStop = new TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-9001", CustomerMaskedId = "CUST-01",
            CustomerName = "عميل مسلّم", CustomerPhone = "+965 555 0001",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", FullAddress = "شارع الدائرة الأول، السالمية",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.Delivered,
            Latitude = 29.3375m, Longitude = 48.0225m
        };
        db.TripStops.Add(deliveredStop);
        await db.SaveChangesAsync();

        var (handler, routing) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id, FocusedStopId: deliveredStop.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FocusedStop!.Status.Should().Be("Delivered");

        var navigation = result.Value.Navigation;
        navigation.Should().NotBeNull();
        navigation!.RouteStatus.Should().Be("Completed");
        navigation.UnavailableReason.Should().BeNull();
        navigation.CanNavigate.Should().BeFalse();
        navigation.EncodedPolyline.Should().BeNull();
        navigation.GoogleMapsUrl.Should().BeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDeliveryMap_NeverExposesCustomerPhone_AndKeepsAddressFallback()
    {
        // Arrange — phones stored on the trip stops must never reach the driver map
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-110", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.InProgress };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var withFullAddress = new TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-0111", CustomerMaskedId = "CUST-01",
            CustomerName = "عميل أول", CustomerPhone = "+965 555 1111",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", FullAddress = "شارع بلاد الشام، السالمية، قطعة 12",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 1, Status = TripStopStatus.PickedUp
        };
        var withoutFullAddress = new TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-0112", CustomerMaskedId = "CUST-02",
            CustomerName = "عميل ثانٍ", CustomerPhone = "+965 555 2222",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda", FullAddress = null,
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            SequenceNumber = 2, Status = TripStopStatus.Pending
        };
        db.TripStops.AddRange(withFullAddress, withoutFullAddress);
        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id), CancellationToken.None);

        // Assert — every card's phone field stays empty; the stored phone never leaks
        result.IsSuccess.Should().BeTrue();
        result.Value.Stops.Should().OnlyContain(s => s.CustomerPhone == string.Empty);
        result.Value.FocusedStop!.CustomerPhone.Should().BeEmpty();

        // FullAddress is used when stored, and the zone name is the only fallback —
        // the API never invents an address.
        var firstCard = result.Value.Stops.Single(s => s.StopId == withFullAddress.Id);
        firstCard.FullAddress.Should().Be("شارع بلاد الشام، السالمية، قطعة 12");
        firstCard.AddressShort.Should().Be("السالمية");

        var secondCard = result.Value.Stops.Single(s => s.StopId == withoutFullAddress.Id);
        secondCard.FullAddress.Should().Be("الروضة");
        secondCard.AddressShort.Should().Be("الروضة");
    }

    [Fact]
    public async Task ValidateBarcode_Should_Accept_Box_On_Older_Active_Trip()
    {
        // Arrange — supervisor added the box to an existing older trip while a newer
        // trip is also active; scanning it must NOT return BOX_NOT_ASSIGNED_TO_DRIVER
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var olderTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-601", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress, CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var newerTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-602", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(olderTrip, newerTrip);

        var olderAssignment = new DriverAssignment { TripId = olderTrip.Id, DriverProfileId = driver.Id, Status = "Active" };
        db.DriverAssignments.Add(olderAssignment);
        db.DriverAssignments.Add(new DriverAssignment { TripId = newerTrip.Id, DriverProfileId = driver.Id, Status = "Active" });

        db.TripStops.Add(new TripStop
        {
            TripId = olderTrip.Id, OrderCode = "#BX-3523", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        });
        await db.SaveChangesAsync();

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driver.UserId);

        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ValidatePickupBarcodeCommand("#BX-3523"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("because error was {0}", result.Error?.Code ?? "none");
        result.Value.IsValid.Should().BeTrue();

        // The issued validation must bind to the trip that actually owns the box
        var validation = await db.PickupValidations.SingleAsync();
        validation.TripId.Should().Be(olderTrip.Id);
        validation.DriverAssignmentId.Should().Be(olderAssignment.Id);
    }

    [Fact]
    public async Task ValidateBarcode_Should_Reject_Box_Of_Another_Driver_Trip()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var myDriver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        var otherDriver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق آخر", FullNameEn = "Other Driver", IsAvailable = true };
        db.DriverProfiles.AddRange(myDriver, otherDriver);

        var otherTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-603", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned
        };
        db.Trips.Add(otherTrip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = otherTrip.Id, DriverProfileId = otherDriver.Id, Status = "Active" });

        // Scanning driver also has their own active trip (required to reach the ownership check)
        var myTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-604", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned
        };
        db.Trips.Add(myTrip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = myTrip.Id, DriverProfileId = myDriver.Id, Status = "Active" });

        db.TripStops.Add(new TripStop
        {
            TripId = otherTrip.Id, OrderCode = "#BX-9999", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "حولي", ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        });
        await db.SaveChangesAsync();

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(myDriver.UserId);

        var handler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ValidatePickupBarcodeCommand("#BX-9999"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("BOX_NOT_ASSIGNED_TO_DRIVER");
        db.PickupValidations.Should().BeEmpty();
    }

    [Fact]
    public async Task ArriveAtKitchen_Should_Succeed_And_Record_Arrival()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" };
        db.DriverAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ArriveAtKitchenCommand(driver.Id, 29.3375, 48.0280), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().Be(trip.Id);

        var updatedTrip = await db.Trips.FindAsync(trip.Id);
        updatedTrip!.Notes.Should().Contain("[DriverArrivedAtKitchen]");
    }

    [Fact]
    public async Task ValidateBarcode_Should_Fail_When_Barcode_Invalid()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new LegacyValidatePickupBarcodeCommand("UNKNOWN-999", driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.InvalidBarcode");
    }

    [Fact]
    public async Task ValidateBarcode_Should_Fail_When_Box_Belongs_To_Another_Driver()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driverA = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد إبراهيم", FullNameEn = "Ahmed", IsAvailable = true };
        var driverB = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "خالد محمود", FullNameEn = "Khaled", IsAvailable = true };
        db.DriverProfiles.AddRange(driverA, driverB);

        var tripA = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-A", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        var tripB = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-B", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.AddRange(tripA, tripB);

        db.DriverAssignments.Add(new DriverAssignment { TripId = tripA.Id, DriverProfileId = driverA.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = tripB.Id, DriverProfileId = driverB.Id, Status = "Active" });

        // Box BX-B belongs to tripB (Driver B)
        var stopB = new TripStop
        {
            TripId = tripB.Id,
            OrderCode = "BX-B-100",
            CustomerMaskedId = "CUST-B",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stopB);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act - Driver A scans box belonging to Driver B
        var result = await handler.Handle(new LegacyValidatePickupBarcodeCommand("BX-B-100", driverA.Id), CancellationToken.None);

        // Assert - Screen 06.13: هذا الرمز لا يخصك!
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.BoxBelongsToAnotherDriver");
        result.Error.Description.Should().Contain("خالد محمود");
    }

    [Fact]
    public async Task ValidateBarcode_Should_Succeed_When_Box_Belongs_To_Current_Driver()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "480K-1234",
            CustomerName = "محمد علي",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new LegacyValidatePickupBarcodeCommand("480K-1234", driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.BoxCode.Should().Be("480K-1234");
        result.Value.BelongsToThisDriver.Should().BeTrue();
        result.Value.ScanStatus.Should().Be("PendingScan");
    }

    [Fact]
    public async Task StartTripDelivery_Should_Fail_When_Unscanned_Boxes_Remain()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "480K-1234",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending // Unscanned
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new StartTripDeliveryCommand(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.UnscannedBoxesRemaining");
    }

    [Fact]
    public async Task StartTripDelivery_Should_Succeed_When_All_Boxes_Scanned()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.Assigned };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "480K-1234",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp // Scanned & photo verified
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new StartTripDeliveryCommand(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("InProgress");
        result.Value.DispatchedBoxesCount.Should().Be(1);

        var updatedTrip = await db.Trips.FindAsync(trip.Id);
        updatedTrip!.Status.Should().Be(TripStatus.InProgress);
        updatedTrip.StartedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPickupManifest_Should_Return_Scanned_And_Total_Count()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-101", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var stop1 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "480K-1",
            CustomerMaskedId = "CUST-01",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.PickedUp
        };
        var stop2 = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "480K-2",
            CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.AddRange(stop1, stop2);
        await db.SaveChangesAsync();

        var handler = new DriverPickupQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverPickupManifestQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalBoxesCount.Should().Be(2);
        result.Value.ScannedBoxesCount.Should().Be(1);
        result.Value.RemainingBoxesCount.Should().Be(1);
        result.Value.AllBoxesScanned.Should().BeFalse();
        result.Value.Boxes.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPickupManifest_Should_Include_Boxes_From_All_Active_Trips()
    {
        // Arrange — dispatcher added new stops to an older trip after a newer trip existed
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var olderTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-201", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress, CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var newerTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-202", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(olderTrip, newerTrip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = olderTrip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = newerTrip.Id, DriverProfileId = driver.Id, Status = "Active" });

        var loadedStop = new TripStop
        {
            TripId = olderTrip.Id, OrderCode = "#BX-4665", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات", MealSummaryEn = "3 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.PickedUp
        };
        var newlyAssignedStop = new TripStop
        {
            TripId = olderTrip.Id, OrderCode = "#BX-3523", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda",
            MealSummaryAr = "2 وجبة", MealSummaryEn = "2 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        };
        var newestTripStop = new TripStop
        {
            TripId = newerTrip.Id, OrderCode = "#BX-2249", CustomerMaskedId = "CUST-03",
            ZoneNameAr = "الجابرية", ZoneNameEn = "Jabriya",
            MealSummaryAr = "3 وجبات", MealSummaryEn = "3 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        };
        db.TripStops.AddRange(loadedStop, newlyAssignedStop, newestTripStop);
        await db.SaveChangesAsync();

        var handler = new DriverPickupQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverPickupManifestQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().Be(newerTrip.Id);
        result.Value.TotalBoxesCount.Should().Be(3);
        result.Value.ScannedBoxesCount.Should().Be(1);
        result.Value.RemainingBoxesCount.Should().Be(2);
        result.Value.Boxes.Select(b => b.BoxCode).Should().Contain(new[] { "#BX-4665", "#BX-3523", "#BX-2249" });
        // newest trip first, then the older trip's stops in sequence order
        result.Value.Boxes.Select(b => b.BoxCode).Should().Equal("#BX-2249", "#BX-4665", "#BX-3523");
    }

    [Fact]
    public async Task GetPickupManifest_Should_Exclude_Completed_Trips()
    {
        // Arrange — an older completed trip must not leak its stops into the manifest
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var completedTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-301", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Completed, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var activeTrip = new Trip
        {
            RestaurantId = Guid.NewGuid(), Code = "TRP-302", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(completedTrip, activeTrip);

        db.DriverAssignments.Add(new DriverAssignment { TripId = completedTrip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = activeTrip.Id, DriverProfileId = driver.Id, Status = "Active" });

        db.TripStops.Add(new TripStop
        {
            TripId = completedTrip.Id, OrderCode = "#BX-1111", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات", MealSummaryEn = "3 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Delivered
        });
        db.TripStops.Add(new TripStop
        {
            TripId = activeTrip.Id, OrderCode = "#BX-2222", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda",
            MealSummaryAr = "2 وجبة", MealSummaryEn = "2 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        });
        await db.SaveChangesAsync();

        var handler = new DriverPickupQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverPickupManifestQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalBoxesCount.Should().Be(1);
        result.Value.Boxes.Select(b => b.BoxCode).Should().Equal("#BX-2222");
    }

    [Fact]
    public async Task GetPickupPoint_Should_Count_Stops_Across_All_Active_Trips()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed", IsAvailable = true };
        db.DriverProfiles.Add(driver);

        var restaurantId = Guid.NewGuid();
        var tripA = new Trip
        {
            RestaurantId = restaurantId, Code = "TRP-401", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        var tripB = new Trip
        {
            RestaurantId = restaurantId, Code = "TRP-402", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.Assigned, CreatedAtUtc = DateTime.UtcNow
        };
        db.Trips.AddRange(tripA, tripB);

        db.DriverAssignments.Add(new DriverAssignment { TripId = tripA.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.DriverAssignments.Add(new DriverAssignment { TripId = tripB.Id, DriverProfileId = driver.Id, Status = "Active" });

        db.TripStops.Add(new TripStop
        {
            TripId = tripA.Id, OrderCode = "#BX-5555", CustomerMaskedId = "CUST-01",
            ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات", MealSummaryEn = "3 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.PickedUp
        });
        db.TripStops.Add(new TripStop
        {
            TripId = tripB.Id, OrderCode = "#BX-6666", CustomerMaskedId = "CUST-02",
            ZoneNameAr = "الروضة", ZoneNameEn = "Rawda",
            MealSummaryAr = "2 وجبة", MealSummaryEn = "2 meals", SlotLabel = "09:30-10:30 ص",
            Status = TripStopStatus.Pending
        });
        await db.SaveChangesAsync();

        var handler = new DriverPickupQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverPickupPointQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TripId.Should().Be(tripB.Id);
        result.Value.TotalBoxesCount.Should().Be(2);
    }

    [Fact]
    public async Task ConfirmCustomerArrival_Should_Succeed_And_Return_CustomerInfo()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-201", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-778899",
            CustomerMaskedId = "CUST-99",
            CustomerName = "أحمد إبراهيم",
            CustomerPhone = "01012345678",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "12:15 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);

        // Arrival state belongs to the assigned driver: profile + active trip assignment.
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق الاختبار",
            FullNameEn = "Test Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);
        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id), CancellationToken.None);

        // Assert: success without any customer phone in the payload (F01 containment),
        // and the first arrival persists a stable instant on the stop.
        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerName.Should().Be("أحمد إبراهيم");
        result.Value.Status.Should().Be("Arrived");
        result.Value.IsFirstArrival.Should().BeTrue();
        result.Value.DriverId.Should().Be(driver.Id);

        var savedStop = await db.TripStops.FindAsync(stop.Id);
        savedStop!.ArrivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmCustomerArrival_WhenNotAuthorized_ReturnsNotFound()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-215", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-778900",
            CustomerMaskedId = "CUST-100",
            CustomerName = "سارة محمود",
            CustomerPhone = "01098765432",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبتان",
            MealSummaryEn = "2 meals",
            SlotLabel = "13:15 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        // The caller is not a driver with an active assignment on this stop's trip:
        // no driver profile resolves for the random user id, so arrival is denied.
        currentUser.UserId.Returns(Guid.NewGuid());
        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id), CancellationToken.None);

        // Assert: an unauthenticated-to-this-stop caller gets NotFound with no data.
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(MessageCodes.Orders.NotFound);
    }

    [Fact]
    public async Task FailDelivery_And_ReturnBoxToKitchen_Should_Update_Statuses_Correctly()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-202", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-998877",
            CustomerMaskedId = "CUST-98",
            CustomerName = "خالد ناصر",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "2 وجبة",
            MealSummaryEn = "2 meals",
            SlotLabel = "12:45 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act 1: Fail Delivery
        var failResult = await handler.Handle(new FailDeliveryCommand(
            BoxId: stop.Id,
            ReasonCategory: "CustomerUnreachable",
            Notes: "اتصلت بالعميل 3 مرات ولم يرد"), CancellationToken.None);

        failResult.IsSuccess.Should().BeTrue();
        failResult.Value.Status.Should().Be("Failed");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.Failed);
        updatedStop.FailureReason.Should().Contain("CustomerUnreachable");

        // Act 2: Return Box to Kitchen
        var returnResult = await handler.Handle(new ReturnBoxToKitchenCommand(
            BoxId: stop.Id,
            Reason: "تعذر التسليم لعدم رد العميل",
            Notes: "تم التسليم لمشرف المطبخ الشيف علاء"), CancellationToken.None);

        returnResult.IsSuccess.Should().BeTrue();
        returnResult.Value.Status.Should().Be("ReturnedToKitchen");

        var returnedStop = await db.TripStops.FindAsync(stop.Id);
        returnedStop!.Status.Should().Be(TripStopStatus.ReturnedToKitchen);
    }

    [Fact]
    public async Task RequestReassignment_Should_Succeed_And_Record_AuditLog()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-203", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-112233",
            CustomerMaskedId = "CUST-97",
            CustomerName = "يوسف أحمد",
            ZoneNameAr = "الفروانية",
            ZoneNameEn = "Farwaniya",
            MealSummaryAr = "1 وجبة",
            MealSummaryEn = "1 meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new RequestReassignmentCommand(
            BoxId: stop.Id,
            Reason: "عطل بالمركبة",
            Notes: "بنشر في الإطار الخلفي للدراجة"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("ReassignmentRequested");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.FailureReason.Should().Contain("عطل بالمركبة");
    }

    [Fact]
    public async Task GetCustomerCallProxy_IsRetired_AndNeverReturnsTheStoredPhone()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-204", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-96",
            CustomerName = "محمد علي",
            CustomerPhone = "+965 98765432",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "2 وجبة",
            MealSummaryEn = "2 meals",
            SlotLabel = "01:15 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetCustomerCallProxyQuery(stop.Id), CancellationToken.None);

        // Assert: the legacy proxy path is retired (F01) — it must never disclose the
        // stored phone, even to its own handler, and fails with the documented code.
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("driver.call_proxy_retired");
        result.Error.Description.Should().NotContain("+965 98765432");
    }

    [Fact]
    public async Task ValidatePickupBarcode_Should_Match_When_Given_Kitchen_MealBarcode_Prefix()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-880", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var subDayId = Guid.NewGuid();
        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-9941",
            SubscriptionDayId = subDayId,
            CustomerMaskedId = "CUST-9941",
            CustomerName = "فاطمة أحمد",
            CustomerPhone = "+965 99887766",
            ZoneNameAr = "الدسمة",
            ZoneNameEn = "Dasma",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        });
        currentUser.UserId.Returns(driver.UserId);

        var kitchenBarcode = $"MM-20260923-{subDayId:N}";
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = subDayId,
            BarcodeValue = kitchenBarcode,
            GeneratedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new DriverPickupCommandHandlers(db, currentUser, messages);

        // Act - Scan the Kitchen printed barcode MM-...
        var result = await handler.Handle(new LegacyValidatePickupBarcodeCommand(kitchenBarcode, driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.BoxId.Should().Be(stop.Id);
        result.Value.BoxCode.Should().Be("BX-9941");
    }

    [Fact]
    public async Task KitchenMarkTripReadyForPickup_Should_Notify_Driver_And_Update_Notes()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-881", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-111",
            CustomerMaskedId = "CUST-111",
            CustomerName = "علي سالم",
            CustomerPhone = "+965 99001122",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "خالد العازمي",
            FullNameEn = "Khaled Al-Azmi",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        };
        db.DriverAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new KitchenPickupCoordinationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new MarkTripReadyForPickupCommand(trip.Id, "تجهيز الشحنة اكتمل بنجاح"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BoxesCount.Should().Be(1);
        result.Value.DriverName.Should().Be("خالد العازمي");

        var notification = await db.DriverNotifications.FirstOrDefaultAsync(n => n.DriverProfileId == driver.Id);
        notification.Should().NotBeNull();
        notification!.Type.Should().Be("trip-ready-for-pickup");
        notification.SubjectId.Should().Be(trip.Id);
    }

    [Fact]
    public async Task KitchenAcknowledgeReturnedBox_Should_Record_Disposition_And_AuditLog()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var stop = new TripStop
        {
            TripId = Guid.NewGuid(),
            OrderCode = "BX-999",
            CustomerMaskedId = "CUST-999",
            CustomerName = "بدر ناصر",
            CustomerPhone = "+965 99003344",
            ZoneNameAr = "اليرموك",
            ZoneNameEn = "Yarmouk",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.ReturnedToKitchen
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var handler = new KitchenPickupCoordinationCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new AcknowledgeReturnedBoxCommand(stop.Id, "Refrigerated", "تم التأكد من سلامة التغليف وتبريده"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Disposition.Should().Be("Refrigerated");

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.FailureReason.Should().Contain("Refrigerated");
    }

    [Fact]
    public async Task CustomerLiveTracking_Should_Return_Driver_Position_And_Eta()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-882", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-440",
            CustomerMaskedId = "CUST-440",
            CustomerName = "سارة العلي",
            CustomerPhone = "+965 99005566",
            ZoneNameAr = "كيفان",
            ZoneNameEn = "Kaifan",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سالم محمد",
            FullNameEn = "Salem Mohammed",
            VehicleType = "سيارة",
            VehiclePlate = "18-9922",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow
        });

        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            Status = "Active",
            StartLatitude = 29.3550,
            StartLongitude = 47.9650,
            StartedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new CustomerLiveTrackingQueryHandler(db, messages);

        // Act
        var result = await handler.Handle(new GetCustomerLiveOrderTrackingQuery(stop.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Driver.Should().NotBeNull();
        result.Value.Driver!.DriverName.Should().Be("سالم محمد");
        result.Value.Driver.PlateNumber.Should().Be("18-9922");
        result.Value.Location.IsLive.Should().BeTrue();
        result.Value.Location.DriverLatitude.Should().Be(29.3550);
        result.Value.Location.DriverLongitude.Should().Be(47.9650);
    }

    [Fact]
    public async Task SyncDriverOfflineQueue_Should_Process_Batch_Scans_And_Deliveries()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-883", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-550",
            CustomerMaskedId = "CUST-550",
            CustomerName = "يوسف أحمد",
            CustomerPhone = "+965 99007788",
            ZoneNameAr = "الفيحاء",
            ZoneNameEn = "Faiha",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "01:00 م",
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "حمد جابر", FullNameEn = "Hamad Jaber" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var handler = new DriverOfflineSyncCommandHandler(db, currentUser, messages);

        var queue = new List<OfflineQueueItemDto>
        {
            new(
                ClientActionId: Guid.NewGuid(),
                ActionType: "PickupScan",
                BoxId: stop.Id,
                BarcodeValue: stop.OrderCode,
                Latitude: 29.3600m,
                Longitude: 47.9700m,
                CapturedAtUtc: DateTime.UtcNow.AddMinutes(-10),
                Notes: "Offline pickup"
            ),
            new(
                ClientActionId: Guid.NewGuid(),
                ActionType: "DeliveryConfirmation",
                BoxId: stop.Id,
                PhotoStorageKey: "proofs/bx550.jpg",
                Latitude: 29.3650m,
                Longitude: 47.9750m,
                CapturedAtUtc: DateTime.UtcNow.AddMinutes(-2),
                Notes: "Offline delivered"
            )
        };

        // Act
        var result = await handler.Handle(new SyncDriverOfflineQueueCommand(queue, driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalReceived.Should().Be(2);
        result.Value.SuccessCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(0);

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.Delivered);
    }

    #region Driver Operational Status Contract (shift-status managed by delivery supervisor)

    [Fact]
    public async Task GetHomeScreen_InactiveWithoutSession_ReturnsInactiveContract()
    {
        // Arrange — driver not operating: no active work session and not available.
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = false
        };
        db.DriverProfiles.Add(driver);

        db.DriverNotifications.Add(new DriverNotification
        {
            UserId = driver.UserId,
            DriverProfileId = driver.Id,
            Type = "trip-update",
            TitleAr = "إشعار تجريبي",
            TitleEn = "Test notification",
            BodyAr = "نص",
            BodyEn = "Body",
            IsRead = false,
            SentAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert — operational status is derived from the active session, never from availability.
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Inactive");
        result.Value.IsAvailable.Should().BeFalse();
        result.Value.CurrentStatusText.Should().Be("غير متصل");

        // The six operational fields are null in Inactive state; identity stays real.
        result.Value.NextLocationText.Should().BeNull();
        result.Value.TargetProgress.Should().BeNull();
        result.Value.CurrentDeliveryTask.Should().BeNull();
        result.Value.TodaySummary.Should().BeNull();
        result.Value.DailyPerformance.Should().BeNull();
        result.Value.ActiveSession.Should().BeNull();
        result.Value.DriverId.Should().Be(driver.Id);
        result.Value.DriverName.Should().Be("أحمد إبراهيم");

        // Fixed contract: Inactive screen always reports zero unread notifications.
        result.Value.UnreadNotificationsCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHomeScreen_ActiveAvailableWithoutOrder_ShowsAvailableState()
    {
        // Arrange — supervisor opened a session; driver is idle and available.
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = "Active"
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Active");
        result.Value.IsAvailable.Should().BeTrue();
        result.Value.CurrentStatusText.Should().Be("متصل ومتاح لاستلام الطلبات");
        result.Value.TodaySummary.Should().NotBeNull();
        result.Value.TargetProgress.Should().NotBeNull();
        result.Value.CurrentDeliveryTask.Should().BeNull();
        result.Value.ActiveSession.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHomeScreen_ActiveBusyWithCurrentOrder_KeepsOrderVisible()
    {
        // Arrange — busy driver: active session, availability off during delivery.
        // The current order must still be shown; availability must not hide it.
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = false
        };
        db.DriverProfiles.Add(driver);

        var session = new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Status = "Active"
        };
        db.DriverWorkSessions.Add(session);

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-901", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = TripStatus.InProgress };
        db.Trips.Add(trip);
        db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" });
        db.TripStops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-458722",
            CustomerMaskedId = "CUST-01",
            CustomerName = "محمد علي",
            CustomerPhone = "+965 50123456",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            SequenceNumber = 1,
            Status = TripStopStatus.PickedUp,
            Latitude = 29.3375m,
            Longitude = 48.0225m
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert — Active with isAvailable=false stays on home and shows the order.
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Active");
        result.Value.IsAvailable.Should().BeFalse();
        result.Value.CurrentStatusText.Should().Be("خارج التوصيل (توصيل الطلب)");
        result.Value.CurrentDeliveryTask.Should().NotBeNull();
        result.Value.CurrentDeliveryTask!.BoxCode.Should().Be("BX-458722");
        result.Value.ActiveSession.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHomeScreen_LegacyAvailableWithoutSession_ReportsInactiveWithoutMutating()
    {
        // Arrange — legacy data: IsAvailable=true with no active session.
        // GET home must report Inactive (session is the source of truth) and must
        // never open a session or flip IsAvailable as a side effect.
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        db.DriverNotifications.Add(new DriverNotification
        {
            UserId = driver.UserId,
            DriverProfileId = driver.Id,
            Type = "trip-update",
            TitleAr = "إشعار تجريبي",
            TitleEn = "Test notification",
            BodyAr = "نص",
            BodyEn = "Body",
            IsRead = false,
            SentAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert — no derivation of operational status from availability.
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Inactive");
        result.Value.IsAvailable.Should().BeFalse(); // normalize the inactive response, without changing storage
        result.Value.CurrentStatusText.Should().Be("غير متصل");
        result.Value.TargetProgress.Should().BeNull();
        result.Value.TodaySummary.Should().BeNull();
        result.Value.UnreadNotificationsCount.Should().Be(0);

        // GET must not mutate anything: no session opened, availability untouched.
        db.DriverWorkSessions.Should().BeEmpty();
        var persisted = await db.DriverProfiles.AsNoTracking().FirstAsync(d => d.Id == driver.Id);
        persisted.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetHomeScreen_EndedSession_ReturnsInactive()
    {
        // Arrange — a previously ended session must not count as operational.
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "أحمد إبراهيم",
            FullNameEn = "Ahmed Ibrahim",
            IsAvailable = false
        };
        db.DriverProfiles.Add(driver);
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-5),
            EndedAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = "Ended"
        });
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(driver.UserId);
        var handler = new DriverHomeScreenQueryHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new GetDriverHomeScreenQuery(driver.Id), CancellationToken.None);

        // Assert — internal session states (Ended/Cancelled) are never exposed as shiftStatus.
        result.IsSuccess.Should().BeTrue();
        result.Value.ShiftStatus.Should().Be("Inactive");
        result.Value.ActiveSession.Should().BeNull();
    }

    #endregion

    [Fact]
    public async Task OptimizeTripRoute_Should_Sort_Stops_Geographically_And_Update_Sequence()
    {
        // Arrange
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();

        var trip = new Trip { RestaurantId = Guid.NewGuid(), Code = "TRP-884", PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        db.Trips.Add(trip);

        var stop1 = new TripStop { TripId = trip.Id, OrderCode = "BX-01", CustomerMaskedId = "CUST-01", CustomerName = "عميل 1", CustomerPhone = "+965 991", ZoneNameAr = "حولي", ZoneNameEn = "Hawally", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00 م", SequenceNumber = 3 };
        var stop2 = new TripStop { TripId = trip.Id, OrderCode = "BX-02", CustomerMaskedId = "CUST-02", CustomerName = "عميل 2", CustomerPhone = "+965 992", ZoneNameAr = "الروضة", ZoneNameEn = "Rawda", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00 م", SequenceNumber = 1 };
        var stop3 = new TripStop { TripId = trip.Id, OrderCode = "BX-03", CustomerMaskedId = "CUST-03", CustomerName = "عميل 3", CustomerPhone = "+965 993", ZoneNameAr = "السالمية", ZoneNameEn = "Salmiya", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00 م", SequenceNumber = 2 };

        db.TripStops.AddRange(stop1, stop2, stop3);
        await db.SaveChangesAsync();

        var handler = new OptimizeTripRouteCommandHandler(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new OptimizeTripRouteCommand(trip.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.StopsCount.Should().Be(3);
        result.Value.OptimizedStops.Should().HaveCount(3);

        // Check sequence numbers are 1, 2, 3
        var stops = await db.TripStops.Where(s => s.TripId == trip.Id).OrderBy(s => s.SequenceNumber).ToListAsync();
        stops[0].SequenceNumber.Should().Be(1);
        stops[1].SequenceNumber.Should().Be(2);
        stops[2].SequenceNumber.Should().Be(3);
    }

    #region Screens 07 contract — arrival persistence, deliver idempotency, proof binding, stop card fields

    private static (DriverProfile Driver, Trip Trip, DriverAssignment Assignment) SeedActiveTrip(
        AppDbContext db, string code = "TRP-700", DriverProfile? existingDriver = null)
    {
        var driver = existingDriver ?? new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق الاختبار",
            FullNameEn = "Test Driver",
            IsAvailable = true
        };
        if (existingDriver is null)
        {
            db.DriverProfiles.Add(driver);
        }

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = code,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = TripStatus.InProgress
        };
        db.Trips.Add(trip);

        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Status = "Active"
        };
        db.DriverAssignments.Add(assignment);

        return (driver, trip, assignment);
    }

    private static TripStop SeedStop(
        AppDbContext db,
        Guid tripId,
        string orderCode,
        TripStopStatus status = TripStopStatus.PickedUp,
        int sequence = 1,
        Guid? subscriptionDayId = null,
        Guid? dailyOrderId = null)
    {
        var stop = new TripStop
        {
            TripId = tripId,
            OrderCode = orderCode,
            CustomerMaskedId = $"CUST-{sequence:00}",
            CustomerName = $"عميل {sequence}",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "3 وجبات",
            MealSummaryEn = "3 meals",
            SlotLabel = "09:20 ص",
            SequenceNumber = sequence,
            Status = status,
            SubscriptionDayId = subscriptionDayId,
            DailyOrderId = dailyOrderId
        };
        db.TripStops.Add(stop);
        return stop;
    }

    private static MealMate.Domain.Common.UploadFileRecord SeedUploadRecord(
        Guid storageKeyOwnerUserId, string key = "uploads/drivers/delivery/proof.jpg", string contentType = "image/jpeg")
        => new()
        {
            StorageKey = key,
            FileName = "proof.jpg",
            ContentType = contentType,
            SizeBytes = 1024,
            UploadedByUserId = storageKeyOwnerUserId,
            UploadedAtUtc = DateTime.UtcNow
        };

    private static void SeedProofForDriver(AppDbContext db, DriverProfile driver)
    {
        db.UploadFileRecords.Add(SeedUploadRecord(driver.UserId));
    }

    [Fact]
    public async Task ConfirmArrival_FirstCall_PersistsArrivedAtUtc_AndNotifiesCustomerOnce()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, trip, _) = SeedActiveTrip(db);
        currentUser.UserId.Returns(driver.UserId);

        var customerUserId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = customerUserId,
            Label = "المنزل",
            AddressLine = "حولي، شارع تونس",
            Notes = "اتصل عند البوابة الشمالية"
        };
        db.CustomerAddresses.Add(address);

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = customerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 30,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(29),
            Status = SubscriptionStatus.Active,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);

        var day = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.OutForDelivery,
            DeliveryAddressId = address.Id
        };
        db.SubscriptionDays.Add(day);

        var stop = SeedStop(db, trip.Id, "BX-7001", subscriptionDayId: day.Id);
        await db.SaveChangesAsync();

        var customerPush = Substitute.For<ICustomerPushNotifier>();
        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages, customerPushNotifier: customerPush);

        var first = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id, DriverId: driver.Id), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.IsFirstArrival.Should().BeTrue();
        first.Value.DriverId.Should().Be(driver.Id);
        first.Value.ArrivedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

        var savedStop = await db.TripStops.FindAsync(stop.Id);
        savedStop!.ArrivedAtUtc.Should().NotBeNull();
        savedStop.ArrivedAtUtc.Should().Be(first.Value.ArrivedAtUtc);

        await customerPush.Received(1).NotifyDriverArrivedAsync(
            customerUserId, day.Id, day.ServiceDate, Arg.Any<CancellationToken>());

        // Repeat: same persisted instant, no new push, no new write.
        var savedInstant = savedStop.ArrivedAtUtc!.Value;
        await Task.Delay(5);
        var repeat = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id, DriverId: driver.Id), CancellationToken.None);

        repeat.IsSuccess.Should().BeTrue();
        repeat.Value.IsFirstArrival.Should().BeFalse();
        repeat.Value.ArrivedAtUtc.Should().Be(savedInstant);
        await customerPush.Received(1).NotifyDriverArrivedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmArrival_ForeignDriver_Rejected_And_Saves_Nothing()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (_, trip, _) = SeedActiveTrip(db);
        var stop = SeedStop(db, trip.Id, "BX-7002");
        await db.SaveChangesAsync();

        var foreignDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق آخر",
            FullNameEn = "Other Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(foreignDriver);
        await db.SaveChangesAsync();

        currentUser.UserId.Returns(foreignDriver.UserId);
        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var result = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id, DriverId: foreignDriver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Orders.NotFound);

        var savedStop = await db.TripStops.FindAsync(stop.Id);
        savedStop!.ArrivedAtUtc.Should().BeNull("a rejected arrival request must not persist any state");
    }

    [Fact]
    public async Task ConfirmArrival_UnknownStop_Returns_NotFound()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, _, _) = SeedActiveTrip(db);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var result = await handler.Handle(new ConfirmCustomerArrivalCommand(Guid.NewGuid(), DriverId: driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Orders.NotFound);
    }

    [Fact]
    public async Task CompleteDelivery_Repeat_Is_Idempotent_NoDoubleCountersScansOrPush()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, trip, _) = SeedActiveTrip(db);

        var session = new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Status = "Active",
            DailyTargetOrders = 8,
            CompletedOrdersCount = 4
        };
        db.DriverWorkSessions.Add(session);

        var customerUserId = Guid.NewGuid();
        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = customerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 30,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(29),
            Status = SubscriptionStatus.Active,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        var day = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.OutForDelivery
        };
        db.SubscriptionDays.Add(day);

        var stop = SeedStop(db, trip.Id, "BX-7003", subscriptionDayId: day.Id);
        await db.SaveChangesAsync();
        SeedProofForDriver(db, driver);
        await db.SaveChangesAsync();

        var customerPush = Substitute.For<ICustomerPushNotifier>();
        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages, customerPushNotifier: customerPush);

        var command = new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg",
            DriverId: driver.Id);

        var first = await handler.Handle(command, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        first.Value.IsFirstDelivery.Should().BeTrue();
        var firstDeliveredAt = first.Value.DeliveredAtUtc;

        await Task.Delay(5);
        var repeat = await handler.Handle(command, CancellationToken.None);

        repeat.IsSuccess.Should().BeTrue();
        repeat.Value.IsFirstDelivery.Should().BeFalse();
        repeat.Value.DeliveredAtUtc.Should().Be(firstDeliveredAt);
        repeat.Value.TripId.Should().Be(trip.Id);
        repeat.Value.IsTripCompleted.Should().BeTrue();
        repeat.Value.RemainingStopsCount.Should().Be(0);

        var sessionReloaded = await db.DriverWorkSessions.FindAsync(session.Id);
        sessionReloaded!.CompletedOrdersCount.Should().Be(5, "the repeat call must not increment the counter again");

        (await db.DeliveryConfirmationScans.CountAsync(s => s.OrderId == stop.DailyOrderId || s.SubscriptionDayId == stop.SubscriptionDayId))
            .Should().Be(1, "the repeat call must not add a second proof scan");

        await customerPush.Received(1).NotifyOrderDeliveredAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteDelivery_CompletionCount_Is_PerTrip_Across_ActiveTrips_AndExcludesFailed()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, tripA, _) = SeedActiveTrip(db, "TRP-A");
        var (_, tripB, _) = SeedActiveTrip(db, "TRP-B", existingDriver: driver);

        // Trip A: one already-Failed stop (final, excluded from remaining), two deliverable stops.
        var failedStop = SeedStop(db, tripA.Id, "BX-A0", TripStopStatus.Failed, sequence: 1);
        failedStop.FailureReason = "[CustomerAbsent] nobody home";
        var stopA1 = SeedStop(db, tripA.Id, "BX-A1", TripStopStatus.PickedUp, sequence: 2);
        var stopA2 = SeedStop(db, tripA.Id, "BX-A2", TripStopStatus.PickedUp, sequence: 3);

        // Trip B: another active trip that must NOT be counted into trip A's numbers.
        SeedStop(db, tripB.Id, "BX-B1", TripStopStatus.PickedUp, sequence: 1);

        await db.SaveChangesAsync();
        SeedProofForDriver(db, driver);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var first = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stopA1.Id, ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg", DriverId: driver.Id), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.TripId.Should().Be(tripA.Id);
        first.Value.IsTripCompleted.Should().BeFalse();
        first.Value.RemainingStopsCount.Should().Be(1, "only stopA2 remains in trip A — Failed stops and trip B stops are excluded");

        var tripAReloaded = await db.Trips.FindAsync(tripA.Id);
        tripAReloaded!.Status.Should().Be(TripStatus.InProgress);

        var second = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stopA2.Id, ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg", DriverId: driver.Id), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.IsTripCompleted.Should().BeTrue();
        second.Value.RemainingStopsCount.Should().Be(0);

        var tripAReloaded2 = await db.Trips.FindAsync(tripA.Id);
        tripAReloaded2!.Status.Should().Be(TripStatus.Completed);

        var tripBReloaded = await db.Trips.FindAsync(tripB.Id);
        tripBReloaded!.Status.Should().Be(TripStatus.InProgress, "completing trip A must not complete the driver's other active trip");
    }

    [Fact]
    public async Task CompleteDelivery_ForeignDriver_Is_Forbidden()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (_, trip, _) = SeedActiveTrip(db);
        var stop = SeedStop(db, trip.Id, "BX-7004");
        await db.SaveChangesAsync();

        var foreignDriver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق آخر",
            FullNameEn = "Other Driver",
            IsAvailable = true
        };
        db.DriverProfiles.Add(foreignDriver);
        await db.SaveChangesAsync();
        SeedProofForDriver(db, foreignDriver);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var result = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg",
            DriverId: foreignDriver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.DeliveryForbidden");

        var savedStop = await db.TripStops.FindAsync(stop.Id);
        savedStop!.Status.Should().Be(TripStopStatus.PickedUp, "a rejected delivery must not change any state");
    }

    [Fact]
    public async Task CompleteDelivery_Coordinates_Require_Pair_And_ValidRange()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, trip, _) = SeedActiveTrip(db);
        var stop = SeedStop(db, trip.Id, "BX-7005");
        await db.SaveChangesAsync();
        SeedProofForDriver(db, driver);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var halfPair = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg",
            Latitude: 29.3375m,
            DriverId: driver.Id), CancellationToken.None);

        halfPair.IsSuccess.Should().BeFalse();
        halfPair.Error.Code.Should().Be("Orders.CoordinatesPairRequired");

        var outOfRange = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg",
            Latitude: 95m,
            Longitude: 48m,
            DriverId: driver.Id), CancellationToken.None);

        outOfRange.IsSuccess.Should().BeFalse();
        outOfRange.Error.Code.Should().Be("Orders.CoordinatesOutOfRange");

        var savedStop = await db.TripStops.FindAsync(stop.Id);
        savedStop!.Status.Should().Be(TripStopStatus.PickedUp, "invalid coordinates must be rejected before any state change");
    }

    [Fact]
    public async Task CompleteDelivery_Proof_Must_Be_Owned_Image()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, trip, _) = SeedActiveTrip(db);
        var stop = SeedStop(db, trip.Id, "BX-7006");
        await db.SaveChangesAsync();

        // Owned by someone else entirely.
        db.UploadFileRecords.Add(SeedUploadRecord(Guid.NewGuid()));
        // Owned by the driver but not an image.
        db.UploadFileRecords.Add(SeedUploadRecord(driver.UserId, "uploads/drivers/delivery/proof.pdf", "application/pdf"));
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var foreign = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.jpg",
            DriverId: driver.Id), CancellationToken.None);

        foreign.IsSuccess.Should().BeFalse();
        foreign.Error.Code.Should().Be("Orders.ProofPhotoForbidden");

        var wrongType = await handler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: stop.Id,
            ProofPhotoStorageKey: "uploads/drivers/delivery/proof.pdf",
            DriverId: driver.Id), CancellationToken.None);

        wrongType.IsSuccess.Should().BeFalse();
        wrongType.Error.Code.Should().Be("Orders.ProofPhotoInvalidType");
    }

    [Fact]
    public async Task GetDeliveryMap_Stops_Expose_BoxId_TripId_CustomerNote_AndArrivedAtUtc()
    {
        await using var db = CreateDb();
        var (currentUser, messages) = CreateMocks();
        var (driver, tripA, _) = SeedActiveTrip(db, "TRP-A");
        var (_, tripB, _) = SeedActiveTrip(db, "TRP-B", existingDriver: driver);

        var customerUserId = Guid.NewGuid();
        var addressWithNote = new CustomerAddress
        {
            UserId = customerUserId,
            Label = "المنزل",
            AddressLine = "حولي، شارع تونس",
            Notes = "اتصل عند البوابة الشمالية"
        };
        db.CustomerAddresses.Add(addressWithNote);

        var dayA = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.OutForDelivery,
            DeliveryAddressId = addressWithNote.Id
        };
        db.SubscriptionDays.Add(dayA);

        var arrivedStop = SeedStop(db, tripA.Id, "BX-A1", TripStopStatus.PickedUp, subscriptionDayId: dayA.Id);
        arrivedStop.ArrivedAtUtc = new DateTime(2026, 10, 6, 7, 30, 0, DateTimeKind.Utc);

        // Trip B stop with a subscription day whose address carries no note.
        var addressNoNote = new CustomerAddress
        {
            UserId = customerUserId,
            Label = "العمل",
            AddressLine = "مدينة الكويت، البرج"
        };
        db.CustomerAddresses.Add(addressNoNote);
        var dayB = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.OutForDelivery,
            DeliveryAddressId = addressNoNote.Id
        };
        db.SubscriptionDays.Add(dayB);
        SeedStop(db, tripB.Id, "BX-B1", TripStopStatus.PickedUp, subscriptionDayId: dayB.Id);

        await db.SaveChangesAsync();

        var (handler, _) = CreateMapHandler(db, currentUser, messages);

        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var cardA = result.Value!.Stops.First(s => s.StopId == arrivedStop.Id);
        cardA.BoxId.Should().Be(cardA.StopId, "boxId is an explicit alias of stopId in the current contract");
        cardA.TripId.Should().Be(tripA.Id);
        cardA.CustomerNote.Should().Be("اتصل عند البوابة الشمالية");
        cardA.ArrivedAtUtc.Should().Be(arrivedStop.ArrivedAtUtc);

        var cardB = result.Value!.Stops.First(s => s.TripId == tripB.Id);
        cardB.CustomerNote.Should().BeNull("an address without a note maps to null, never fabricated text");
        cardB.ArrivedAtUtc.Should().BeNull();

        // Existing contract intact: navigation payload is still served for the focused stop.
        result.Value.Navigation.Should().NotBeNull();
        result.Value.FocusedStop.Should().NotBeNull();
    }

    #endregion
}
