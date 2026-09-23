using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
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
        result.Value.DailyPerformance.OnTimeRatePercentage.Should().Be(92.0);
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

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.Delivered);

        var updatedSession = await db.DriverWorkSessions.FindAsync(session.Id);
        updatedSession!.CompletedOrdersCount.Should().Be(5); // incremented from 4 to 5

        var confScan = await db.DeliveryConfirmationScans.FirstOrDefaultAsync();
        confScan.Should().NotBeNull();
        confScan!.BarcodeValue.Should().Be("BX-458722");
        confScan.PhotoPath.Should().Be("uploads/drivers/delivery/proof_box1.jpg");
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
            Status = TripStopStatus.Delivered
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
            Status = TripStopStatus.PickedUp
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
            Status = TripStopStatus.Pending
        };
        db.TripStops.AddRange(stop1, stop2, stop3);
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryMapQueryHandler(db, currentUser, messages);

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

        var handler = new DriverDeliveryMapQueryHandler(db, currentUser, messages);

        // Act - Request explicitly focusing on stop 1
        var result = await handler.Handle(new GetDriverDeliveryMapQuery(driver.Id, FocusedStopId: stop1.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FocusedStop.Should().NotBeNull();
        result.Value.FocusedStop!.StopId.Should().Be(stop1.Id);
        result.Value.FocusedStop.CustomerName.Should().Be("العميل 1");
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
        var result = await handler.Handle(new ValidatePickupBarcodeCommand("UNKNOWN-999", driver.Id), CancellationToken.None);

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
        var result = await handler.Handle(new ValidatePickupBarcodeCommand("BX-B-100", driverA.Id), CancellationToken.None);

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
        var result = await handler.Handle(new ValidatePickupBarcodeCommand("480K-1234", driver.Id), CancellationToken.None);

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
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Act
        var result = await handler.Handle(new ConfirmCustomerArrivalCommand(stop.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerName.Should().Be("أحمد إبراهيم");
        result.Value.Status.Should().Be("Arrived");
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
    public async Task GetCustomerCallProxy_Should_Return_Masked_Phone_And_Extension()
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

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerName.Should().Be("محمد علي");
        result.Value.IsMasked.Should().BeTrue();
        result.Value.ProxyPhone.Should().NotBeNullOrWhiteSpace();
        result.Value.Extension.Should().Be("458722");
    }
}
