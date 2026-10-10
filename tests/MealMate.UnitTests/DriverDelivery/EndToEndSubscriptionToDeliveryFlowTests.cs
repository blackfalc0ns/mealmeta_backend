using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class EndToEndSubscriptionToDeliveryFlowTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Scenario1_StandardFlow_FromCalendarSelection_ToKitchen_ToDispatch_ToDriverPickup_ToFinalDelivery()
    {
        // =========================================================================
        // Step 1: Arrange & Seed (Customer Subscription, Restaurant, Driver)
        // =========================================================================
        await using var db = CreateDb();
        var customerUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم الصحة واللياقة",
            LegalNameEn = "Health & Fitness Restaurant",
            TradeNameAr = "مطعم الصحة واللياقة",
            TradeNameEn = "Health & Fitness Restaurant",
            Latitude = 29.3375m,
            Longitude = 47.9780m
        };
        db.RestaurantProfiles.Add(restaurant);

        var driver = new DriverProfile
        {
            UserId = driverUserId,
            RestaurantId = restaurant.Id,
            FullNameAr = "عبدالله الكندري",
            FullNameEn = "Abdullah Al-Kandari",
            NationalId = "295010101999",
            LicenseNumber = "DRV-KUW-8899",
            VehicleType = "سيارة",
            VehicleModel = "تويوتا كورولا",
            VehiclePlate = "50-99881",
            VehicleColor = "فضي",
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        var address = new CustomerAddress
        {
            UserId = customerUserId,
            AddressLine = "حولي، شارع تونس، مجمع الأمل، شقة 14",
            Label = "المنزل",
            Latitude = 29.3320m,
            Longitude = 48.0280m
        };
        db.CustomerAddresses.Add(address);

        // =========================================================================
        // Step 2: Customer selects meals from calendar (SubscriptionDay)
        // =========================================================================
        var subDay = new SubscriptionDay
        {
            SubscriptionId = subscriptionId,
            RestaurantId = restaurant.Id,
            ServiceDate = today,
            Status = SubscriptionDayStatus.CustomerSelected,
            DeliveryAddressId = address.Id,
            DeliveryAddressLine = address.AddressLine
        };
        subDay.MealSelections.Add(new DailyMealSelection { MealId = Guid.NewGuid(), SelectedAtUtc = DateTime.UtcNow });
        subDay.MealSelections.Add(new DailyMealSelection { MealId = Guid.NewGuid(), SelectedAtUtc = DateTime.UtcNow });
        db.SubscriptionDays.Add(subDay);
        await db.SaveChangesAsync();

        // =========================================================================
        // Step 3: Kitchen receives and confirms order (RestaurantDailyOrder)
        // =========================================================================
        subDay.Status = SubscriptionDayStatus.ConfirmedByRestaurant;
        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = subDay.Id,
            ServiceDate = today,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 1
        };
        db.RestaurantDailyOrders.Add(dailyOrder);
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = subDay.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"#BX-{Math.Abs(subDay.Id.GetHashCode()) % 9000 + 1000}",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        subDay.Status.Should().Be(SubscriptionDayStatus.ConfirmedByRestaurant);
        dailyOrder.Status.Should().Be(OrderStatus.ReadyForPickup);

        // =========================================================================
        // Step 4: Dispatcher assigns box to driver (AssignBoxToDriverCommand)
        // =========================================================================
        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var assignHandler = new AssignBoxToDriverCommandHandler(db, messages, pushNotifier);

        var assignResult = await assignHandler.Handle(
            new AssignBoxToDriverCommand(subDay.Id, driver.Id, "إسناد أولوية الوردية الصباحية"),
            CancellationToken.None);

        assignResult.IsSuccess.Should().BeTrue();
        assignResult.Value.DriverId.Should().Be(driver.Id);
        assignResult.Value.BoxCode.Should().StartWith("#BX-");

        var assignedDay = await db.SubscriptionDays.FindAsync(subDay.Id);
        assignedDay!.Status.Should().Be(SubscriptionDayStatus.Preparing);

        var assignedOrder = await db.RestaurantDailyOrders.FindAsync(dailyOrder.Id);
        assignedOrder!.Status.Should().Be(OrderStatus.AssignedToDriver);

        var tripStop = await db.TripStops.Include(s => s.Trip).FirstOrDefaultAsync(s => s.SubscriptionDayId == subDay.Id);
        tripStop.Should().NotBeNull();
        tripStop!.Status.Should().Be(TripStopStatus.Pending);

        // =========================================================================
        // Step 5: Driver App - Pickup Scan at Restaurant Kitchen (PickupBoxCommand)
        // =========================================================================
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId);
        var deliveryHandler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // Validation: Manual entry without photo MUST FAIL
        var invalidManualPickup = await deliveryHandler.Handle(new PickupBoxCommand(
            BoxId: tripStop.Id,
            BarcodeValue: null,
            ConditionPhotoStorageKey: null,
            DriverId: driver.Id,
            IsManualEntry: true,
            ManualBoxCode: tripStop.OrderCode,
            ManualEntryReason: "الكاميرا لا تقرأ الباركود بسبب تلف في الملصق"), CancellationToken.None);

        invalidManualPickup.IsSuccess.Should().BeFalse();
        invalidManualPickup.Error.Code.Should().Be("Orders.ManualEntryLabelPhotoRequired");

        // Valid manual entry with label photo
        var validPickup = await deliveryHandler.Handle(new PickupBoxCommand(
            BoxId: tripStop.Id,
            BarcodeValue: null,
            ConditionPhotoStorageKey: "uploads/driver/pickup-labels/label-proof-101.jpg",
            DriverId: driver.Id,
            IsManualEntry: true,
            ManualBoxCode: tripStop.OrderCode,
            ManualEntryReason: "الملصق مجعد قليلاً - تم تصوير الملصق للمراجعة"), CancellationToken.None);

        validPickup.IsSuccess.Should().BeTrue();
        validPickup.Value.Status.Should().Be("PickedUp");
        validPickup.Value.IsManualEntry.Should().BeTrue();

        var pickupScan = await db.PickupScans.FirstOrDefaultAsync(s => s.SubscriptionDayId == subDay.Id);
        pickupScan.Should().NotBeNull();
        pickupScan!.IsManualEntry.Should().BeTrue();

        var pickedUpDay = await db.SubscriptionDays.FindAsync(subDay.Id);
        pickedUpDay!.Status.Should().Be(SubscriptionDayStatus.OutForDelivery);

        var pickedUpOrder = await db.RestaurantDailyOrders.FindAsync(dailyOrder.Id);
        pickedUpOrder!.Status.Should().Be(OrderStatus.OnTheWay);

        tripStop = await db.TripStops.Include(s => s.Trip).FirstOrDefaultAsync(s => s.Id == tripStop.Id);
        tripStop!.Status.Should().Be(TripStopStatus.PickedUp);
        tripStop.Trip.Status.Should().Be(TripStatus.InProgress);

        // =========================================================================
        // Step 6: Driver Arrives at Customer & Completes Delivery
        // =========================================================================
        // The delivery proof must reference a real upload owned by this driver.
        db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
        {
            StorageKey = "uploads/driver/delivery-proof/front-door-drop-101.jpg",
            FileName = "front-door-drop-101.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 2048,
            UploadedByUserId = driverUserId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var arrivalResult = await deliveryHandler.Handle(
            new ConfirmCustomerArrivalCommand(tripStop.Id),
            CancellationToken.None);
        arrivalResult.IsSuccess.Should().BeTrue();

        var completeResult = await deliveryHandler.Handle(new CompleteBoxDeliveryCommand(
            BoxId: tripStop.Id,
            BarcodeValue: null,
            ProofPhotoStorageKey: "uploads/driver/delivery-proof/front-door-drop-101.jpg",
            DriverId: driver.Id), CancellationToken.None);

        completeResult.IsSuccess.Should().BeTrue();
        completeResult.Value.Status.Should().Be("Delivered");

        var deliveredStop = await db.TripStops.Include(s => s.Trip).FirstOrDefaultAsync(s => s.Id == tripStop.Id);
        deliveredStop!.Status.Should().Be(TripStopStatus.Delivered);
        deliveredStop.Trip.Status.Should().Be(TripStatus.Completed);

        var finalDay = await db.SubscriptionDays.FindAsync(subDay.Id);
        finalDay!.Status.Should().Be(SubscriptionDayStatus.Delivered);

        var finalOrder = await db.RestaurantDailyOrders.FindAsync(dailyOrder.Id);
        finalOrder!.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Scenario2_BulkAssignMultipleStops_ManifestQuery_AndRealTimeLiveTracking()
    {
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم فيت هاوس",
            LegalNameEn = "Fit House Restaurant",
            TradeNameAr = "مطعم فيت هاوس",
            TradeNameEn = "Fit House Restaurant",
            Latitude = 29.3759m,
            Longitude = 47.9774m
        };
        db.RestaurantProfiles.Add(restaurant);

        var driverUserId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = driverUserId,
            RestaurantId = restaurant.Id,
            FullNameAr = "سالم مبارك",
            FullNameEn = "Salem Mubarak",
            VehicleType = "سيارة",
            VehicleModel = "نيسان صني",
            VehiclePlate = "44-12345",
            VehicleColor = "أبيض",
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddMonths(10),
            IsAvailable = true
        };
        db.DriverProfiles.Add(driver);

        // Driver registration with phone for tracking
        db.DriverRegistrations.Add(new DriverRegistration
        {
            ResultingDriverProfileId = driver.Id,
            Phone = "+96598765432",
            FullNameAr = "سالم مبارك",
            FullNameEn = "Salem Mubarak",
            NationalId = "294010109999",
            Status = VerificationStatus.Approved
        });

        // Driver active session with GPS coordinates
        db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = driver.Id,
            Status = "Active",
            StartLatitude = 29.3700,
            StartLongitude = 47.9800,
            StartedAtUtc = DateTime.UtcNow
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var boxIds = new List<Guid>();

        // Live tracking now authorizes through the subscription ownership gate, so the
        // scenario seeds the real subscription the customer tracks boxes through.
        var trackingCustomerId = Guid.NewGuid();
        var trackedSubscription = new Subscription
        {
            CustomerUserId = trackingCustomerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 6,
            StartDate = today,
            EndDate = today.AddDays(5),
            Status = SubscriptionStatus.Active,
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        db.Subscriptions.Add(trackedSubscription);

        for (var i = 0; i < 3; i++)
        {
            var addr = new CustomerAddress
            {
                UserId = trackingCustomerId,
                AddressLine = $"السالمية قطعة {i + 1} شارع سالم المبارك",
                Label = $"عنوان {i + 1}",
                Latitude = 29.3300m + (i * 0.01m),
                Longitude = 48.0700m + (i * 0.01m)
            };
            db.CustomerAddresses.Add(addr);

            var day = new SubscriptionDay
            {
                SubscriptionId = trackedSubscription.Id,
                RestaurantId = restaurant.Id,
                ServiceDate = today,
                Status = SubscriptionDayStatus.ConfirmedByRestaurant,
                DeliveryAddressId = addr.Id,
                DeliveryAddressLine = addr.AddressLine
            };
            db.SubscriptionDays.Add(day);

            var order = new RestaurantDailyOrder
            {
                RestaurantId = restaurant.Id,
                SubscriptionDayId = day.Id,
                ServiceDate = today,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            };
            db.RestaurantDailyOrders.Add(order);
            db.MealBarcodes.Add(new MealBarcode
            {
                SubscriptionDayId = day.Id,
                OrderId = order.Id,
                BarcodeValue = $"#BX-BULK-{i + 1}",
                Status = MealBarcodeStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            boxIds.Add(day.Id);
        }
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var bulkAssignHandler = new BulkAssignBoxesCommandHandler(db, messages, pushNotifier);

        // 1. Bulk Assign all 3 boxes to driver
        var bulkResult = await bulkAssignHandler.Handle(
            new BulkAssignBoxesCommand(boxIds, driver.Id, "توصيل وردية الظهر"),
            CancellationToken.None);

        bulkResult.IsSuccess.Should().BeTrue();
        bulkResult.Value.AssignedCount.Should().Be(3);
        bulkResult.Value.DriverId.Should().Be(driver.Id);

        // 2. Query Driver Pickup Point & Manifest
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId);
        var pickupQueryHandler = new DriverPickupQueryHandlers(db, currentUser, messages);

        var pickupPointResult = await pickupQueryHandler.Handle(
            new GetDriverPickupPointQuery(driver.Id),
            CancellationToken.None);

        pickupPointResult.IsSuccess.Should().BeTrue();
        pickupPointResult.Value.TotalBoxesCount.Should().Be(3);
        pickupPointResult.Value.KitchenName.Should().Be("Fit House Restaurant");
        pickupPointResult.Value.RouteDistanceText.Should().NotBeNullOrWhiteSpace();
        pickupPointResult.Value.EstimatedArrivalMinutesText.Should().NotBeNullOrWhiteSpace();

        var manifestResult = await pickupQueryHandler.Handle(
            new GetDriverPickupManifestQuery(driver.Id),
            CancellationToken.None);

        manifestResult.IsSuccess.Should().BeTrue();
        manifestResult.Value.TotalBoxesCount.Should().Be(3);
        manifestResult.Value.ScannedBoxesCount.Should().Be(0);
        manifestResult.Value.AllBoxesScanned.Should().BeFalse();

        // 2.1 Kitchen Arrival and Barcode Scanning (OrderCode & Raw Guid)
        var pickupCommandHandler = new DriverPickupCommandHandlers(db, currentUser, messages);
        var arriveResult = await pickupCommandHandler.Handle(new ArriveAtKitchenCommand(driver.Id, 29.3375, 47.9780), CancellationToken.None);
        arriveResult.IsSuccess.Should().BeTrue();

        var box1StopId = manifestResult.Value.Boxes[0].BoxId;
        var box1Code = manifestResult.Value.Boxes[0].BoxCode;
        var val1 = await pickupCommandHandler.Handle(new LegacyValidatePickupBarcodeCommand(box1Code, driver.Id), CancellationToken.None);
        val1.IsSuccess.Should().BeTrue();
        val1.Value.BoxId.Should().Be(box1StopId);

        var box2StopId = manifestResult.Value.Boxes[1].BoxId;
        var val2 = await pickupCommandHandler.Handle(new LegacyValidatePickupBarcodeCommand(box2StopId.ToString(), driver.Id), CancellationToken.None);
        val2.IsSuccess.Should().BeTrue();
        val2.Value.BoxId.Should().Be(box2StopId);

        var valUnknown = await pickupCommandHandler.Handle(new LegacyValidatePickupBarcodeCommand("UNKNOWN-NONEXISTENT-CODE", driver.Id), CancellationToken.None);
        valUnknown.IsSuccess.Should().BeFalse();
        valUnknown.Error.Code.Should().Be("Orders.InvalidBarcode");

        // 3. Driver picks up Box 1
        var deliveryHandler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        var pickupBox1 = await deliveryHandler.Handle(new PickupBoxCommand(
            BoxId: box1StopId,
            BarcodeValue: box1Code,
            ConditionPhotoStorageKey: "photos/box1_label.jpg",
            DriverId: driver.Id), CancellationToken.None);

        pickupBox1.IsSuccess.Should().BeTrue();
        pickupBox1.Value.Status.Should().Be("PickedUp");

        // 4. Customer Live Tracking for Box 1 — with the ownership gate and the real
        // location builder wired (a fresh driver fix backs the live location; the
        // shift-start coordinate is never reported as live anymore).
        var pickedStop = await db.TripStops.AsNoTracking().SingleAsync(s => s.Id == box1StopId);
        db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = pickedStop.TripId,
            DriverProfileId = driver.Id,
            Latitude = 29.3550m,
            Longitude = 48.0100m,
            RecordedAtUtc = DateTime.UtcNow,
            CapturedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var trackingCustomer = Substitute.For<ICurrentUser>();
        trackingCustomer.IsAuthenticated.Returns(true);
        trackingCustomer.UserId.Returns(trackingCustomerId);
        trackingCustomer.UserType.Returns(UserType.Customer);

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new RouteDirectionsResult(
                DistanceMeters: 5200, DistanceKm: 5.2, DurationSeconds: 600, DurationMinutes: 10,
                OverviewPolyline: "abc{de", IsFallbackEstimate: false,
                OriginLatitude: 29.3550m, OriginLongitude: 48.0100m,
                DestinationLatitude: 29.3300m, DestinationLongitude: 48.0700m));

        var trackingClock = Substitute.For<IDateTimeProvider>();
        trackingClock.UtcNow.Returns(DateTime.UtcNow);

        var trackingHandler = new CustomerLiveTrackingQueryHandler(
            db,
            messages,
            new MealMate.Application.Features.FamilyManager.FamilyCalendarAccessGate(db, trackingCustomer),
            new CustomerDeliveryReadService(db),
            new CustomerDeliveryLocationBuilder(
                db,
                routing,
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                trackingClock,
                Microsoft.Extensions.Options.Options.Create(new DriverNavigationOptions())));
        var trackResult = await trackingHandler.Handle(
            new GetCustomerLiveOrderTrackingQuery(box1StopId),
            CancellationToken.None);

        trackResult.IsSuccess.Should().BeTrue();
        trackResult.Value.Status.Should().Be("OutForDelivery");
        trackResult.Value.Driver.Should().NotBeNull();
        trackResult.Value.Driver!.DriverName.Should().Be("Salem Mubarak");
        trackResult.Value.Driver.MaskedPhone.Should().Be("+96***32");
        trackResult.Value.Location.IsLive.Should().BeTrue();
        trackResult.Value.Location.DistanceMeters.Should().BeGreaterThan(0);
        trackResult.Value.Location.EstimatedArrivalMinutes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Scenario3_DriverBreakdown_MidRouteReassignment_AndSuccessfulDelivery()
    {
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            LegalNameAr = "مطعم دايت بلس",
            LegalNameEn = "Diet Plus Restaurant",
            TradeNameAr = "مطعم دايت بلس",
            TradeNameEn = "Diet Plus Restaurant"
        };
        db.RestaurantProfiles.Add(restaurant);

        var driverA = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "أحمد السائق الأول",
            FullNameEn = "Ahmed First Driver",
            VehicleType = "سيارة",
            VehiclePlate = "11-11111",
            IsAvailable = true
        };
        db.DriverProfiles.Add(driverA);

        var driverB = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            FullNameAr = "بدر السائق البديل",
            FullNameEn = "Bader Replacement Driver",
            VehicleType = "سيارة",
            VehicleModel = "تويوتا كامري",
            VehicleColor = "أبيض",
            VehiclePlate = "22-22222",
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            IsAvailable = true
        };
        db.DriverProfiles.Add(driverB);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = new Trip
        {
            RestaurantId = restaurant.Id,
            Code = "TRP-EMERGENCY-01",
            Status = TripStatus.InProgress,
            PlannedDate = today
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BX-EMERGENCY-99",
            CustomerMaskedId = "CUST-99",
            Status = TripStopStatus.PickedUp,
            ZoneNameAr = "الروضة",
            ZoneNameEn = "Rawda",
            MealSummaryAr = "وجبتين",
            MealSummaryEn = "2 meals",
            SlotLabel = "12:00 م"
        };
        db.TripStops.Add(stop);
        trip.Stops.Add(stop);

        var assignmentA = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driverA.Id,
            Status = "Active",
            AssignedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        db.DriverAssignments.Add(assignmentA);
        trip.DriverAssignments.Add(assignmentA);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverA.UserId);
        var deliveryHandler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // 1. Driver A's car breaks down and Driver A requests reassignment
        var reassignRequest = await deliveryHandler.Handle(new RequestReassignmentCommand(
            BoxId: stop.Id,
            DriverId: driverA.Id,
            Reason: "VehicleBreakdown",
            Notes: "المركبة متوقفة في شارع دمشق أمام الروضة"), CancellationToken.None);

        reassignRequest.IsSuccess.Should().BeTrue();
        reassignRequest.Value.RequestId.Should().NotBeEmpty();

        var updatedStop = await db.TripStops.FindAsync(stop.Id);
        updatedStop!.Status.Should().Be(TripStopStatus.Pending, "Stop goes back to Pending for dispatcher re-routing");

        var updatedAssignmentA = await db.DriverAssignments.FindAsync(assignmentA.Id);
        updatedAssignmentA!.Status.Should().Be("Reassigned");

        // 2. Dispatcher reassigns to Driver B
        var audit = Substitute.For<IAuditLogger>();
        var reassignHandler = new ReassignReplacementDriverCommandHandler(db, messages, audit);

        var reassignResult = await reassignHandler.Handle(new ReassignReplacementDriverCommand(
            IssueId: reassignRequest.Value.RequestId,
            ReplacementDriverId: driverB.Id,
            Notes: "تم توجيه السائق بدر لاستلام الصندوق وتوصيله"), CancellationToken.None);

        reassignResult.IsSuccess.Should().BeTrue();
        reassignResult.Value.ReplacementDriverId.Should().Be(driverB.Id);
        reassignResult.Value.ReplacementDriver.FullName.Should().Be("Bader Replacement Driver");

        var driverAPost = await db.DriverProfiles.FindAsync(driverA.Id);
        driverAPost!.IsAvailable.Should().BeTrue("replacement must preserve supervisor-managed availability; marking a driver unavailable requires an explicit status command");

        var activeAssignmentB = await db.DriverAssignments.FirstOrDefaultAsync(a => a.TripId == trip.Id && a.DriverProfileId == driverB.Id);
        activeAssignmentB.Should().NotBeNull();
        activeAssignmentB!.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Scenario4_FailedDelivery_CustomerUnreachable_ReturnedToKitchen_AndHoldState()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var driver = new DriverProfile
        {
            UserId = driverUserId,
            RestaurantId = restaurantId,
            FullNameAr = "طارق المري",
            FullNameEn = "Tariq Al-Marri",
            VehicleType = "سيارة",
            VehiclePlate = "99-88776"
        };
        db.DriverProfiles.Add(driver);

        var day = new SubscriptionDay
        {
            RestaurantId = restaurantId,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.OutForDelivery
        };
        db.SubscriptionDays.Add(day);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = restaurantId,
            SubscriptionDayId = day.Id,
            Status = OrderStatus.OnTheWay,
            BoxCount = 1
        };
        db.RestaurantDailyOrders.Add(order);

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-RET-01",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Trips.Add(trip);

        var stop = new TripStop
        {
            TripId = trip.Id,
            SubscriptionDayId = day.Id,
            DailyOrderId = order.Id,
            OrderCode = "BX-NO-ANSWER-10",
            CustomerMaskedId = "CUST-88",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة واحدة",
            MealSummaryEn = "1 meal",
            SlotLabel = "12:00 م",
            Status = TripStopStatus.PickedUp
        };
        db.TripStops.Add(stop);
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(driverUserId);
        var deliveryHandler = new DriverDeliveryOrderCommandHandlers(db, currentUser, messages);

        // 1. Delivery Fails: Customer unreachable
        var failResult = await deliveryHandler.Handle(new FailDeliveryCommand(
            BoxId: stop.Id,
            ReasonCategory: "CustomerUnreachable",
            Notes: "تم الاتصال بالعميل 4 مرات ودق الجرس دون أي استجابة"), CancellationToken.None);

        failResult.IsSuccess.Should().BeTrue();
        failResult.Value.Status.Should().Be("Failed");

        var failedDay = await db.SubscriptionDays.FindAsync(day.Id);
        failedDay!.Status.Should().Be(SubscriptionDayStatus.Failed);

        var failedOrder = await db.RestaurantDailyOrders.FindAsync(order.Id);
        failedOrder!.Status.Should().Be(OrderStatus.DeliveryFailed);

        // 2. Driver returns box to restaurant kitchen
        var returnResult = await deliveryHandler.Handle(new ReturnBoxToKitchenCommand(
            BoxId: stop.Id,
            Reason: "CustomerUnreachable",
            Notes: "إعادة البوكس إلى ثلاجة المطبخ لحفظ الوجبة"), CancellationToken.None);

        returnResult.IsSuccess.Should().BeTrue();
        returnResult.Value.Status.Should().Be("ReturnedToKitchen");

        var returnedDay = await db.SubscriptionDays.FindAsync(day.Id);
        returnedDay!.Status.Should().Be(SubscriptionDayStatus.Preparing, "Returned box returns to Preparing so it can be re-dispatched or replaced");

        var returnedOrder = await db.RestaurantDailyOrders.FindAsync(order.Id);
        returnedOrder!.Status.Should().Be(OrderStatus.Hold, "Returned order placed on Hold at kitchen");
    }

    [Fact]
    public async Task Scenario5_DefensiveGuardrails_CapacityLimits_TenantIsolation_AndExpiredLicense()
    {
        await using var db = CreateDb();
        var restaurantA = new RestaurantProfile
        {
            LegalNameAr = "مطعم أ",
            LegalNameEn = "Restaurant A",
            TradeNameAr = "مطعم أ",
            TradeNameEn = "Restaurant A"
        };
        var restaurantB = new RestaurantProfile
        {
            LegalNameAr = "مطعم ب",
            LegalNameEn = "Restaurant B",
            TradeNameAr = "مطعم ب",
            TradeNameEn = "Restaurant B"
        };
        db.RestaurantProfiles.AddRange(restaurantA, restaurantB);

        // 5.1 Motorcycle driver (Capacity = 5 max)
        var bikeDriver = new DriverProfile
        {
            RestaurantId = restaurantA.Id,
            FullNameAr = "سائق دراجة",
            FullNameEn = "Bike Driver",
            VehicleType = "دراجة نارية",
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true
        };
        db.DriverProfiles.Add(bikeDriver);

        // 5.2 Driver with expired driving license
        var expiredDriver = new DriverProfile
        {
            RestaurantId = restaurantA.Id,
            FullNameAr = "سائق رخصة منتهية",
            FullNameEn = "Expired License Driver",
            VehicleType = "سيارة",
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddDays(-1), // Expired!
            IsAvailable = true
        };
        db.DriverProfiles.Add(expiredDriver);

        // 5.3 Driver affiliated strictly with Restaurant B
        var driverOfRestaurantB = new DriverProfile
        {
            RestaurantId = restaurantB.Id,
            FullNameAr = "سائق مطعم ب",
            FullNameEn = "Driver B",
            VehicleType = "سيارة",
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            IsAvailable = true
        };
        db.DriverProfiles.Add(driverOfRestaurantB);

        // 6 boxes from Restaurant A
        var boxIds = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var day = new SubscriptionDay
            {
                RestaurantId = restaurantA.Id,
                ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = SubscriptionDayStatus.ConfirmedByRestaurant
            };
            db.SubscriptionDays.Add(day);
            boxIds.Add(day.Id);
        }
        await db.SaveChangesAsync();

        var messages = NullLocalizedMessageProvider.Instance;
        var pushNotifier = Substitute.For<IDriverPushNotifier>();
        var handler = new BulkAssignBoxesCommandHandler(db, messages, pushNotifier);

        // Test 1: Capacity Limit Violation (6 boxes on motorcycle max 5)
        var capacityError = await handler.Handle(
            new BulkAssignBoxesCommand(boxIds, bikeDriver.Id, "توصيل"),
            CancellationToken.None);
        capacityError.IsSuccess.Should().BeFalse();
        capacityError.Error.Code.Should().BeOneOf("Driver.VehicleCapacityExceeded", "VEHICLE_CAPACITY_EXCEEDED");

        // Test 2: Expired License Violation
        var expiredError = await handler.Handle(
            new BulkAssignBoxesCommand(boxIds.Take(2).ToList(), expiredDriver.Id, "توصيل"),
            CancellationToken.None);
        expiredError.IsSuccess.Should().BeFalse();
        expiredError.Error.Code.Should().Be("Driver.LicenseExpired");

        // Test 3: Restaurant Fleet Isolation (Assigning Restaurant A boxes to Restaurant B driver)
        var tenantError = await handler.Handle(
            new BulkAssignBoxesCommand(boxIds.Take(2).ToList(), driverOfRestaurantB.Id, "توصيل"),
            CancellationToken.None);
        tenantError.IsSuccess.Should().BeFalse();
        tenantError.Error.Code.Should().Be("Driver.NotAffiliatedWithRestaurant");
    }
}
