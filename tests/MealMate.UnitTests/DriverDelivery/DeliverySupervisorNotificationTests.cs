using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Domain.AdminDashboard;
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

public class DeliverySupervisorNotificationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (DriverProfile Driver, Trip Trip, DriverAssignment Assignment) SeedDriverAndTrip(
        AppDbContext db, string tripCode = "TRP-7777", TripStatus status = TripStatus.Assigned)
    {
        var driver = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            FullNameAr = "سائق التجربة",
            FullNameEn = "Test Driver",
            VehicleType = "car",
            VehiclePlate = "7777"
        };
        db.DriverProfiles.Add(driver);

        var trip = new Trip
        {
            RestaurantId = Guid.NewGuid(),
            Code = tripCode,
            Status = status,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
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

        return (driver, trip, assignment);
    }

    private static (SubscriptionDay Day, RestaurantDailyOrder DailyOrder, MealBarcode Barcode) SeedDay(
        AppDbContext db, Trip trip, int boxCount = 1, string slotLabel = "12:00-14:00")
    {
        var day = new SubscriptionDay
        {
            RestaurantId = trip.RestaurantId,
            ServiceDate = trip.PlannedDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = trip.RestaurantId,
            SubscriptionDayId = day.Id,
            ServiceDate = trip.PlannedDate,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = boxCount
        };
        db.RestaurantDailyOrders.Add(dailyOrder);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"#BX-{Random.Shared.Next(1000, 10000)}",
            Status = MealBarcodeStatus.Generated
        };
        db.MealBarcodes.Add(barcode);

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = dailyOrder.Id,
            MealBarcodeId = barcode.Id,
            OrderCode = barcode.BarcodeValue,
            CustomerMaskedId = "CUST-901",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "فهد",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = slotLabel,
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        return (day, dailyOrder, barcode);
    }

    private static (string RawToken, PickupValidation Validation, PickupConditionPhoto Photo) SeedValidationAndPhoto(
        AppDbContext db, Trip trip, DriverProfile driver, DriverAssignment assignment, SubscriptionDay day, string barcodeValue)
    {
        var rawToken = "token-" + Guid.NewGuid().ToString("N");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        var validation = new PickupValidation
        {
            TripId = trip.Id,
            BoxId = day.Id,
            DriverProfileId = driver.Id,
            DriverAssignmentId = assignment.Id,
            BarcodeValue = barcodeValue,
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
            StorageKey = $"photos/{day.Id}.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            Checksum = "sha256",
            UploadedAtUtc = DateTime.UtcNow
        };
        db.PickupConditionPhotos.Add(photo);

        return (rawToken, validation, photo);
    }

    [Fact]
    public async Task ConfirmPickup_WhenLastBoxScannedAndPhotographed_NotifiesSupervisorOnce()
    {
        await using var db = CreateDb();
        var (driver, trip, assignment) = SeedDriverAndTrip(db, tripCode: "TRP-5001");
        var (day1, _, barcode1) = SeedDay(db, trip);
        var (day2, _, barcode2) = SeedDay(db, trip);
        var (raw1, v1, p1) = SeedValidationAndPhoto(db, trip, driver, assignment, day1, barcode1.BarcodeValue);
        var (raw2, v2, p2) = SeedValidationAndPhoto(db, trip, driver, assignment, day2, barcode2.BarcodeValue);
        await db.SaveChangesAsync();

        var notifier = Substitute.For<IDeliverySupervisorNotifier>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driver.UserId);

        var handler = new ConfirmPickupBoxCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance, supervisorNotifier: notifier);

        // First box: not all boxes received yet → no supervisor notification.
        var first = await handler.Handle(new ConfirmPickupBoxCommand(
            BoxId: day1.Id, ValidationToken: raw1,
            ConditionPhotoStorageKey: $"photos/{day1.Id}.jpg", Latitude: 24.7m, Longitude: 46.7m,
            IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

        first.IsSuccess.Should().BeTrue("because error was {0}", first.Error?.Code ?? "none");
        first.Value.AllBoxesReceived.Should().BeFalse();
        await notifier.DidNotReceive().NotifyPickupCompletedAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Second (last) box: all scanned + photographed → single consolidated notification.
        var second = await handler.Handle(new ConfirmPickupBoxCommand(
            BoxId: day2.Id, ValidationToken: raw2,
            ConditionPhotoStorageKey: $"photos/{day2.Id}.jpg", Latitude: 24.7m, Longitude: 46.7m,
            IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.AllBoxesReceived.Should().BeTrue();
        await notifier.Received(1).NotifyPickupCompletedAsync(
            trip.Id, "TRP-5001", "سائق التجربة", "Test Driver", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteDelivery_WhenLastStopDelivered_NotifiesSupervisorAndCompletesTrip()
    {
        await using var db = CreateDb();
        var (driver, trip, _) = SeedDriverAndTrip(db, tripCode: "TRP-6001");
        var (day1, order1, barcode1) = SeedDay(db, trip);
        var (day2, order2, barcode2) = SeedDay(db, trip);
        await db.SaveChangesAsync();

        // Mark both helper-created stops as picked up (ready for delivery).
        foreach (var s in await db.TripStops.Where(x => x.TripId == trip.Id).ToListAsync())
        {
            s.Status = TripStopStatus.PickedUp;
        }
        await db.SaveChangesAsync();

        var notifier = Substitute.For<IDeliverySupervisorNotifier>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driver.UserId);

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, NullLocalizedMessageProvider.Instance, supervisorNotifier: notifier);

        var stops = await db.TripStops.OrderBy(s => s.OrderCode).ToListAsync();

        var first = await handler.Handle(new CompleteBoxDeliveryCommand(BoxId: stops[0].Id, ProofPhotoStorageKey: "proof/1.jpg"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        await notifier.DidNotReceive().NotifyTripDeliveredAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var second = await handler.Handle(new CompleteBoxDeliveryCommand(BoxId: stops[1].Id, ProofPhotoStorageKey: "proof/2.jpg"), CancellationToken.None);
        second.IsSuccess.Should().BeTrue("because error was {0}", second.Error?.Code ?? "none");

        var completed = await db.Trips.FirstAsync(t => t.Id == trip.Id);
        completed.Status.Should().Be(TripStatus.Completed, "because delivery status was {0} / resp={1}", completed.Status, second.Value?.Message);
        completed.CompletedAtUtc.Should().NotBeNull();

        await notifier.Received(1).NotifyTripDeliveredAsync(
            trip.Id, "TRP-6001", "سائق التجربة", "Test Driver", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DeliveryWindowParser_ParsesSlotLabelRanges_WithArabicMeridiem()
    {
        var utcNow = new DateTime(2026, 10, 2, 5, 0, 0, DateTimeKind.Utc); // 08:00 Kuwait

        // 09:30-10:30 ص → 10:30 Kuwait = 07:30 UTC
        DeliveryWindowParser.TryGetExpectedEndTimeUtc("09:30-10:30 ص", null, utcNow, out var end1).Should().BeTrue();
        end1.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 7, 30, 0, DateTimeKind.Utc));

        // 24h range without meridiem
        DeliveryWindowParser.TryGetExpectedEndTimeUtc("18:00-20:00", null, utcNow, out var end2).Should().BeTrue();
        end2.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc));

        // مساء with small hours → +12
        DeliveryWindowParser.TryGetExpectedEndTimeUtc("06:00-08:00 م", null, utcNow, out var end3).Should().BeTrue();
        end3.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc));

        // en dash with spaces
        DeliveryWindowParser.TryGetExpectedEndTimeUtc("12:00 – 14:00", null, utcNow, out var end4).Should().BeTrue();
        end4.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 11, 0, 0, DateTimeKind.Utc));

        // shift fallbacks
        DeliveryWindowParser.TryGetExpectedEndTimeUtc(null, "مساء", utcNow, out var end5).Should().BeTrue();
        end5.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc));
        DeliveryWindowParser.TryGetExpectedEndTimeUtc(null, "صباحي", utcNow, out var end6).Should().BeTrue();
        end6.ToUniversalTime().Should().Be(new DateTime(2026, 10, 2, 7, 0, 0, DateTimeKind.Utc));

        // nothing parseable → false
        DeliveryWindowParser.TryGetExpectedEndTimeUtc("بلا وقت", "نص آخر", utcNow, out _).Should().BeFalse();
    }

    [Fact]
    public async Task DelayEscalation_NotifiesOnlyLateStops_AndDoesNotDuplicate()
    {
        await using var db = CreateDb();
        var (driver, trip, _) = SeedDriverAndTrip(db, tripCode: "TRP-7001", status: TripStatus.InProgress);

        // The escalation service only inspects trips planned for the fixed clock's
        // Kuwait "today" — anchor the trip to that date instead of the machine date,
        // otherwise the test breaks on every day after 2026-10-02.
        trip.PlannedDate = new DateOnly(2026, 10, 2);

        var lateStop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "#BX-1111",
            CustomerMaskedId = "CUST-1",
            ZoneNameAr = "السالمية",
            ZoneNameEn = "Salmiya",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "09:30-10:30 ص",
            BoxCount = 1,
            Status = TripStopStatus.PickedUp
        };
        var onTimeStop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "#BX-2222",
            CustomerMaskedId = "CUST-2",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00-14:00",
            BoxCount = 1,
            Status = TripStopStatus.PickedUp
        };
        var deliveredStop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "#BX-3333",
            CustomerMaskedId = "CUST-3",
            ZoneNameAr = "الجهراء",
            ZoneNameEn = "Jahra",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "09:30-10:30 ص",
            BoxCount = 1,
            Status = TripStopStatus.Delivered
        };
        trip.Stops.Add(lateStop);
        trip.Stops.Add(onTimeStop);
        trip.Stops.Add(deliveredStop);
        await db.SaveChangesAsync();

        // Fixed clock: 11:30 Kuwait = 08:30 UTC → late stop window ended 10:30+15m grace = 45 minutes late.
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc));

        var notifier = Substitute.For<IDeliverySupervisorNotifier>();
        var service = new DeliveryDelayEscalationService(db, clock, notifier);

        await service.DetectAndNotifyAsync(CancellationToken.None);

        await notifier.Received(1).NotifyStopDelayedAsync(
            lateStop.Id, trip.Id, "TRP-7001", "سائق التجربة", "Test Driver",
            "#BX-1111", "#BX-1111", Arg.Any<string>(), Arg.Any<string>(), 45, Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyStopDelayedAsync(
            onTimeStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyStopDelayedAsync(
            deliveredStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Rerun after an alert already exists for this stop today → no duplicate.
        db.AdminNotifications.Add(new AdminNotification
        {
            TitleAr = "تأخير في التسليم",
            TitleEn = "Delivery delay",
            SentAtUtc = new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc),
            PayloadJson = $$"""{"eventKey":"delivery.stop.delayed","stopId":"{{lateStop.Id}}"}"""
        });
        await db.SaveChangesAsync();

        await service.DetectAndNotifyAsync(CancellationToken.None);

        await notifier.Received(1).NotifyStopDelayedAsync(
            lateStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
