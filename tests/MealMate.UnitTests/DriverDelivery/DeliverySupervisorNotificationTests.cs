using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerApp;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Infrastructure.Push;
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
            .AddInterceptors(DriverDeliveryTestConcurrency.ActiveDriverAccounts())
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
    public async Task ConfirmPickup_WhenLastBoxScannedAndPhotographed_NotifiesRestaurantOnce()
    {
        await using var db = CreateDb();
        var (driver, trip, assignment) = SeedDriverAndTrip(db, tripCode: "TRP-5001");
        var (day1, _, barcode1) = SeedDay(db, trip);
        var (day2, _, barcode2) = SeedDay(db, trip);
        var (raw1, v1, p1) = SeedValidationAndPhoto(db, trip, driver, assignment, day1, barcode1.BarcodeValue);
        var (raw2, v2, p2) = SeedValidationAndPhoto(db, trip, driver, assignment, day2, barcode2.BarcodeValue);
        await db.SaveChangesAsync();

        var notifier = Substitute.For<IRestaurantDeliveryNotifier>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driver.UserId);
        currentUser.UserType.Returns(UserType.Driver);

        var handler = new ConfirmPickupBoxCommandHandler(db, currentUser, NullLocalizedMessageProvider.Instance, restaurantNotifier: notifier,
            transactionExecutor: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());

        // First box: not all boxes received yet → no restaurant notification.
        var first = await handler.Handle(new ConfirmPickupBoxCommand(
            BoxId: day1.Id, ValidationToken: raw1,
            ConditionPhotoStorageKey: $"photos/{day1.Id}.jpg", Latitude: 24.7m, Longitude: 46.7m,
            IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

        first.IsSuccess.Should().BeTrue("because error was {0}", first.Error?.Code ?? "none");
        first.Value.AllBoxesReceived.Should().BeFalse();
        await notifier.DidNotReceive().NotifyPickupCompletedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Second (last) box: all scanned + photographed → single consolidated notification
        // targeted at the trip's restaurant (the batch owner), not the admin.
        var second = await handler.Handle(new ConfirmPickupBoxCommand(
            BoxId: day2.Id, ValidationToken: raw2,
            ConditionPhotoStorageKey: $"photos/{day2.Id}.jpg", Latitude: 24.7m, Longitude: 46.7m,
            IdempotencyKey: Guid.NewGuid().ToString()), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.AllBoxesReceived.Should().BeTrue();
        await notifier.Received(1).NotifyPickupCompletedAsync(
            trip.RestaurantId, trip.Id, "TRP-5001", "سائق التجربة", "Test Driver", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteDelivery_WhenLastStopDelivered_NotifiesRestaurantAndCompletesTrip()
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

        var notifier = Substitute.For<IRestaurantDeliveryNotifier>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(driver.UserId);
        currentUser.UserType.Returns(UserType.Driver);

        // Delivery proofs must reference real uploads owned by the delivering driver.
        db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = "proof/1.jpg",
            FileName = "1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            UploadedByUserId = driver.UserId,
            UploadedAtUtc = DateTime.UtcNow
        });
        db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = "proof/2.jpg",
            FileName = "2.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            UploadedByUserId = driver.UserId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var handler = new DriverDeliveryOrderCommandHandlers(db, currentUser, NullLocalizedMessageProvider.Instance, restaurantNotifier: notifier,
            transactions: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks());

        var stops = await db.TripStops.OrderBy(s => s.OrderCode).ToListAsync();

        var first = await handler.Handle(new CompleteBoxDeliveryCommand(BoxId: stops[0].Id, ProofPhotoStorageKey: "proof/1.jpg"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        await notifier.DidNotReceive().NotifyTripDeliveredAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var second = await handler.Handle(new CompleteBoxDeliveryCommand(BoxId: stops[1].Id, ProofPhotoStorageKey: "proof/2.jpg"), CancellationToken.None);
        second.IsSuccess.Should().BeTrue("because error was {0}", second.Error?.Code ?? "none");

        var completed = await db.Trips.FirstAsync(t => t.Id == trip.Id);
        completed.Status.Should().Be(TripStatus.Completed, "because delivery status was {0} / resp={1}", completed.Status, second.Value?.Message);
        completed.CompletedAtUtc.Should().NotBeNull();

        await notifier.Received(1).NotifyTripDeliveredAsync(
            trip.RestaurantId, trip.Id, "TRP-6001", "سائق التجربة", "Test Driver", 2, Arg.Any<CancellationToken>());
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

        db.SystemSettings.Add(new SystemSetting
        {
            Key = OperationalPolicyService.SettingKey,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(new OperationalPolicySettings
            {
                DeliveryDelay = new DeliveryDelayPolicySettings(30)
            }),
            IsActive = true
        });
        await db.SaveChangesAsync();

        // The dispatcher threshold is 30 minutes; at 08:30 UTC this stop is 60 minutes late.
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc));

        var notifier = Substitute.For<IDeliverySupervisorNotifier>();
        var service = new DeliveryDelayEscalationService(db, clock, notifier);

        await service.DetectAndNotifyAsync(CancellationToken.None);

        await notifier.Received(1).NotifyStopDelayedAsync(
            trip.RestaurantId, lateStop.Id, trip.Id, "TRP-7001", "سائق التجربة", "Test Driver",
            "#BX-1111", "#BX-1111", Arg.Any<string>(), Arg.Any<string>(), 60, Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyStopDelayedAsync(
            trip.RestaurantId, onTimeStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyStopDelayedAsync(
            trip.RestaurantId, deliveredStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Rerun after an alert already exists for this stop today → no duplicate.
        db.DeliveryManagerNotifications.Add(new DeliveryManagerNotification
        {
            UserId = Guid.NewGuid(),
            RestaurantId = trip.RestaurantId,
            Type = DeliveryDelayEscalationService.DelayNotificationType,
            TitleAr = "تأخير في التسليم",
            TitleEn = "Delivery delay",
            BodyAr = "تأخير 60 دقيقة في تسليم الطلب.",
            BodyEn = "Order is 60 minutes late.",
            SubjectId = lateStop.Id,
            SentAtUtc = new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        await service.DetectAndNotifyAsync(CancellationToken.None);

        await notifier.Received(1).NotifyStopDelayedAsync(
            trip.RestaurantId, lateStop.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopDelayed_NotifiesDeliveryManagersOnly_NotAdminNorRestaurant()
    {
        await using var db = CreateDb();
        var (_, trip, _) = SeedDriverAndTrip(db, tripCode: "TRP-8001", status: TripStatus.InProgress);

        var dmUser1 = Guid.NewGuid();
        var dmUser2 = Guid.NewGuid();
        db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration
        {
            RestaurantId = trip.RestaurantId,
            FullName = "مسئول التوصيل الأول",
            Phone = "+96551000001",
            Email = "dm1@test.local",
            NationalId = "111111111111",
            Status = VerificationStatus.Approved,
            ResultingUserId = dmUser1
        });
        db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration
        {
            RestaurantId = trip.RestaurantId,
            FullName = "مسئول التوصيل الثاني",
            Phone = "+96551000002",
            Email = "dm2@test.local",
            NationalId = "222222222222",
            Status = VerificationStatus.Approved,
            ResultingUserId = dmUser2
        });
        // Another restaurant's delivery manager must NOT receive the alert.
        db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration
        {
            RestaurantId = Guid.NewGuid(),
            FullName = "مسئول مطعم آخر",
            Phone = "+96551000003",
            Email = "dm3@test.local",
            NationalId = "333333333333",
            Status = VerificationStatus.Approved,
            ResultingUserId = Guid.NewGuid()
        });

        db.DeliveryManagerDeviceTokens.Add(new DeliveryManagerDeviceToken
        {
            UserId = dmUser1,
            RestaurantId = trip.RestaurantId,
            Token = "dm1-fcm-token",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        db.DeliveryManagerDeviceTokens.Add(new DeliveryManagerDeviceToken
        {
            UserId = Guid.NewGuid(), // inactive ownership check: different user
            RestaurantId = trip.RestaurantId,
            Token = "dm2-fcm-token",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<DeliverySupervisorNotifier>>();
        var notifier = new DeliverySupervisorNotifier(db, fcm, logger);

        await notifier.NotifyStopDelayedAsync(
            trip.RestaurantId, Guid.NewGuid(), trip.Id, "TRP-8001",
            "سائق التجربة", "Test Driver", "#BX-4444", "#BX-4444",
            "السالمية", "Salmiya", 20, CancellationToken.None);

        // One bell row per resolved delivery manager of the trip's restaurant only.
        var rows = await db.DeliveryManagerNotifications.ToListAsync();
        rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { dmUser1, dmUser2 });
        rows.Should().OnlyContain(r => r.RestaurantId == trip.RestaurantId);
        rows.Should().OnlyContain(r => r.Type == DeliveryDelayEscalationService.DelayNotificationType);
        rows.Should().OnlyContain(r => r.SubjectId != null);

        // Neither the admin bell nor the restaurant bell may receive the delay alert.
        (await db.AdminNotifications.CountAsync()).Should().Be(0);
        (await db.RestaurantNotifications.CountAsync()).Should().Be(0);

        // Push goes only to the resolved delivery managers' active tokens.
        await fcm.Received(1).SendAsync(
            "dm1-fcm-token", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
        await fcm.DidNotReceive().SendAsync(
            "dm2-fcm-token", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliveryManagerNotificationHandlers_RegisterListAndMarkRead_AreScopedToCaller()
    {
        await using var db = CreateDb();
        var dmUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        db.DeliveryManagerNotifications.Add(new DeliveryManagerNotification
        {
            UserId = dmUser,
            RestaurantId = Guid.NewGuid(),
            Type = DeliveryDelayEscalationService.DelayNotificationType,
            TitleAr = "تأخير عاجل في التسليم ⚠️",
            TitleEn = "Urgent Delivery Delay",
            BodyAr = "تأخير 11 دقيقة في تسليم الطلب #BX-1256.",
            BodyEn = "Order #BX-1256 is 11 minutes late.",
            Section = "delivery",
            SubjectId = Guid.NewGuid(),
            DeepLink = "/trips/x",
            SentAtUtc = DateTime.UtcNow.AddMinutes(-12)
        });
        db.DeliveryManagerNotifications.Add(new DeliveryManagerNotification
        {
            UserId = otherUser, // someone else's alert — must never leak to the caller
            Type = DeliveryDelayEscalationService.DelayNotificationType,
            TitleAr = "عن غيره",
            TitleEn = "Not mine",
            BodyAr = "تنبيه مستخدم آخر.",
            BodyEn = "Another user's alert.",
            SentAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(dmUser);
        currentUser.UserType.Returns(UserType.DeliveryManager);
        currentUser.RestaurantId.Returns((Guid?)null);

        var handlers = new DeliveryManagerNotificationCommandHandlers(
            db, currentUser, NullLocalizedMessageProvider.Instance);

        // Register the FCM device token → upsert keyed on token, bound to caller.
        var registered = await handlers.Handle(
            new RegisterDeliveryManagerDeviceTokenCommand(null, "dm-app-fcm-token", "Android", "device-1"),
            CancellationToken.None);
        registered.IsSuccess.Should().BeTrue("because error was {0}", registered.Error?.Code ?? "none");

        var tokenRow = await db.DeliveryManagerDeviceTokens.SingleAsync(t => t.Token == "dm-app-fcm-token");
        tokenRow.UserId.Should().Be(dmUser);
        tokenRow.IsActive.Should().BeTrue();

        // Re-register the same token updates instead of duplicating.
        await handlers.Handle(
            new RegisterDeliveryManagerDeviceTokenCommand(null, "dm-app-fcm-token", "iOS", "device-1"),
            CancellationToken.None);
        (await db.DeliveryManagerDeviceTokens.CountAsync(t => t.Token == "dm-app-fcm-token")).Should().Be(1);
        (await db.DeliveryManagerDeviceTokens.FirstAsync(t => t.Token == "dm-app-fcm-token")).Platform
            .Should().Be(MobilePlatform.Ios);

        // Bell list: only the caller's rows.
        var list = await handlers.Handle(new GetDeliveryManagerNotificationsQuery(), CancellationToken.None);
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle("because the other user's notification must not leak");

        // Mark as read → only own notification.
        var markMine = await handlers.Handle(
            new MarkDeliveryManagerNotificationReadCommand(list.Value[0].NotificationId), CancellationToken.None);
        markMine.IsSuccess.Should().BeTrue();
        (await db.DeliveryManagerNotifications.FirstAsync(n => n.Id == list.Value[0].NotificationId)).IsRead.Should().BeTrue();

        var markForeign = await handlers.Handle(
            new MarkDeliveryManagerNotificationReadCommand(Guid.NewGuid()), CancellationToken.None);
        markForeign.IsSuccess.Should().BeFalse();

        // A customer may not register a token on the delivery-manager endpoint.
        var customer = Substitute.For<ICurrentUser>();
        customer.IsAuthenticated.Returns(true);
        customer.UserId.Returns(Guid.NewGuid());
        customer.UserType.Returns(UserType.Customer);
        var customerHandlers = new DeliveryManagerNotificationCommandHandlers(
            db, customer, NullLocalizedMessageProvider.Instance);
        var forbidden = await customerHandlers.Handle(
            new RegisterDeliveryManagerDeviceTokenCommand(null, "intruder-token"), CancellationToken.None);
        forbidden.IsSuccess.Should().BeFalse();
    }
}
