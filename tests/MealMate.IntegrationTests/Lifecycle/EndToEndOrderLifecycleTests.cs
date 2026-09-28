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
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace MealMate.IntegrationTests.Lifecycle;

public sealed class EndToEndOrderLifecycleTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        await _container.StartAsync();

        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task CompleteOrderLifecycle_CheckoutToHandover_SucceedsDeterministically()
    {
        var driverUserId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var serviceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));

        Guid restaurantId;
        Guid driverProfileId;
        Guid dailyOrderId;
        Guid subscriptionDayId;
        string canonicalBarcodeValue;

        // =========================================================================
        // Step 1: Initial Setup - Restaurant, Driver, Customer Address, SubscriptionDay
        // =========================================================================
        await using (var db = CreateDb())
        {
            var restaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "مطعم الطهي الصحي",
                LegalNameEn = "Healthy Cooking Restaurant",
                TradeNameAr = "مطعم الطهي الصحي",
                TradeNameEn = "Healthy Cooking Restaurant",
                Latitude = 29.3375m,
                Longitude = 47.9780m
            };
            db.RestaurantProfiles.Add(restaurant);
            restaurantId = restaurant.Id;

            var driver = new DriverProfile
            {
                UserId = driverUserId,
                RestaurantId = restaurantId,
                FullNameAr = "محمد الفهد",
                FullNameEn = "Mohammed Al-Fahad",
                VehicleType = "car",
                VehiclePlate = "KW-8812",
                VerificationStatus = VerificationStatus.Approved,
                IsAvailable = true
            };
            db.DriverProfiles.Add(driver);
            driverProfileId = driver.Id;

            var address = new CustomerAddress
            {
                UserId = Guid.NewGuid(),
                AddressLine = "السالمية، قطعة 4، شارع البحر",
                Label = "المنزل",
                Latitude = 29.3320m,
                Longitude = 48.0280m
            };
            db.CustomerAddresses.Add(address);

            var subDay = new SubscriptionDay
            {
                SubscriptionId = subscriptionId,
                RestaurantId = restaurantId,
                ServiceDate = serviceDate,
                Status = SubscriptionDayStatus.Locked72h,
                DeliveryAddressId = address.Id,
                DeliveryAddressLine = address.AddressLine
            };
            db.SubscriptionDays.Add(subDay);
            await db.SaveChangesAsync();

            subscriptionDayId = subDay.Id;
        }

        // =========================================================================
        // Step 2: 72h Cutoff Execution -> Kitchen Order Creation
        // =========================================================================
        await using (var db = CreateDb())
        {
            var order = new RestaurantDailyOrder
            {
                RestaurantId = restaurantId,
                SubscriptionDayId = subscriptionDayId,
                ServiceDate = serviceDate,
                Status = OrderStatus.AwaitingRestaurantConfirmation,
                BoxCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.RestaurantDailyOrders.Add(order);
            await db.SaveChangesAsync();

            dailyOrderId = order.Id;
        }

        // =========================================================================
        // Step 3: Kitchen Confirmation & Cooking -> ReadyForPickup + Canonical Barcode
        // =========================================================================
        canonicalBarcodeValue = $"MM-{serviceDate:yyyyMMdd}-{subscriptionDayId:N}";

        await using (var db = CreateDb())
        {
            var order = await db.RestaurantDailyOrders.SingleAsync(o => o.Id == dailyOrderId);

            // Strict domain transition: AwaitingRestaurantConfirmation -> RestaurantConfirmed -> Preparing -> ReadyForPickup
            order.Status = OrderStatus.RestaurantConfirmed;
            order.ConfirmedAtUtc = DateTime.UtcNow;

            order.Status = OrderStatus.Preparing;

            order.Status = OrderStatus.ReadyForPickup;
            order.ReadyForPickupAtUtc = DateTime.UtcNow;

            var barcode = new MealBarcode
            {
                SubscriptionDayId = subscriptionDayId,
                BarcodeValue = canonicalBarcodeValue,
                Status = MealBarcodeStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow
            };
            db.MealBarcodes.Add(barcode);

            await db.SaveChangesAsync();
        }

        // =========================================================================
        // Step 4: Dispatcher Trip Assembly & Capacity Allocation
        // =========================================================================
        Guid tripId;
        Guid tripStopId;

        await using (var db = CreateDb())
        {
            var driver = await db.DriverProfiles.SingleAsync(d => d.UserId == driverUserId);
            var barcode = await db.MealBarcodes.SingleAsync(b => b.BarcodeValue == canonicalBarcodeValue);

            var trip = new Trip
            {
                RestaurantId = restaurantId,
                Code = "TRP-E2E-01",
                Status = TripStatus.Assigned,
                PlannedDate = serviceDate
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

            var stop = new TripStop
            {
                TripId = trip.Id,
                SubscriptionDayId = subscriptionDayId,
                DailyOrderId = dailyOrderId,
                MealBarcodeId = barcode.Id,
                OrderCode = "#BX-E2E-01",
                CustomerMaskedId = "CUST-E2E",
                CustomerName = "فاطمة أحمد",
                ZoneNameAr = "السالمية",
                ZoneNameEn = "Salmiya",
                MealSummaryAr = "وجبة غداء صحية",
                MealSummaryEn = "Healthy Lunch Meal",
                SlotLabel = "12:00 م - 02:00 م",
                BoxCount = 1,
                Status = TripStopStatus.Pending
            };
            db.TripStops.Add(stop);

            await db.SaveChangesAsync();

            tripId = trip.Id;
            tripStopId = stop.Id;
        }

        // =========================================================================
        // Step 5: Driver Scanner Viewfinder Pre-Validation
        // =========================================================================
        string validationToken;

        await using (var db = CreateDb())
        {
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(driverUserId);

            var messages = NullLocalizedMessageProvider.Instance;
            var validateHandler = new ValidatePickupBarcodeCommandHandler(db, currentUser, messages);

            var validateResult = await validateHandler.Handle(
                new ValidatePickupBarcodeCommand(canonicalBarcodeValue, IsManualEntry: false),
                CancellationToken.None);

            validateResult.IsSuccess.Should().BeTrue("Canonical barcode must pre-validate successfully for assigned trip");
            validateResult.Value.IsValid.Should().BeTrue();
            validateResult.Value.BoxId.Should().Be(subscriptionDayId);
            validateResult.Value.OrderCode.Should().Be("#BX-E2E-01");
            validateResult.Value.ValidationToken.Should().NotBeNullOrWhiteSpace();

            validationToken = validateResult.Value.ValidationToken;

            // Seed the corresponding condition photo bound to this active validation token
            var activeValidation = await db.PickupValidations.SingleAsync(v => v.BoxId == subscriptionDayId);
            var photo = new PickupConditionPhoto
            {
                PickupValidationId = activeValidation.Id,
                TripId = tripId,
                BoxId = subscriptionDayId,
                DriverProfileId = driverProfileId,
                StorageKey = "uploads/driver/condition/proof-99.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 1024,
                UploadedAtUtc = DateTime.UtcNow
            };
            db.PickupConditionPhotos.Add(photo);
            await db.SaveChangesAsync();
        }

        // =========================================================================
        // Step 6: Atomic Handover Confirmation via PostgresApplicationTransactionExecutor
        // =========================================================================
        await using (var db = CreateDb())
        {
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(driverUserId);

            var messages = NullLocalizedMessageProvider.Instance;
            var txExecutor = new PostgresApplicationTransactionExecutor(db);

            var confirmHandler = new ConfirmPickupBoxCommandHandler(
                db,
                currentUser,
                messages,
                txExecutor);

            var confirmCommand = new ConfirmPickupBoxCommand(
                BoxId: subscriptionDayId,
                ValidationToken: validationToken,
                ConditionPhotoStorageKey: "uploads/driver/condition/proof-99.jpg",
                Latitude: 29.3375m,
                Longitude: 47.9780m,
                IdempotencyKey: Guid.NewGuid().ToString());

            var confirmResult = await confirmHandler.Handle(confirmCommand, CancellationToken.None);

            confirmResult.IsSuccess.Should().BeTrue("Pickup confirmation must commit atomically on real PostgreSQL");
            confirmResult.Value.Status.Should().Be("PickedUp");
            confirmResult.Value.AllBoxesReceived.Should().BeTrue();
        }

        // =========================================================================
        // Step 7: Authoritative Database Invariants Verification
        // =========================================================================
        await using (var db = CreateDb())
        {
            // Invariant 1: TripStop transitioned to PickedUp
            var reloadedStop = await db.TripStops.SingleAsync(s => s.Id == tripStopId);
            reloadedStop.Status.Should().Be(TripStopStatus.PickedUp);

            // Invariant 2: SubscriptionDay transitioned to OutForDelivery
            var reloadedDay = await db.SubscriptionDays.SingleAsync(d => d.Id == subscriptionDayId);
            reloadedDay.Status.Should().Be(SubscriptionDayStatus.OutForDelivery);

            // Invariant 3: RestaurantDailyOrder transitioned to OnTheWay
            var reloadedOrder = await db.RestaurantDailyOrders.SingleAsync(o => o.Id == dailyOrderId);
            reloadedOrder.Status.Should().Be(OrderStatus.OnTheWay);

            // Invariant 4: OrderHandover record persisted with AttemptNumber = 1
            var handover = await db.OrderHandovers.FirstOrDefaultAsync(h => h.DailyOrderId == dailyOrderId);
            handover.Should().NotBeNull("Atomic handover must create persistent OrderHandover audit record");
            handover!.Status.Should().Be("Completed");
            handover.AttemptNumber.Should().Be(1);
            handover.DriverProfileId.Should().Be(driverProfileId);
            handover.TripStopId.Should().Be(tripStopId);

            // Invariant 5: Canonical barcode marked Scanned
            var reloadedBarcode = await db.MealBarcodes.SingleAsync(b => b.BarcodeValue == canonicalBarcodeValue);
            reloadedBarcode.Status.Should().Be(MealBarcodeStatus.Scanned);
            reloadedBarcode.ScannedAtUtc.Should().NotBeNull();

            // Invariant 6: OutboxMessage emitted for downstream broadcast / notifications
            var outboxMessage = await db.OutboxMessages
                .FirstOrDefaultAsync(m => m.AggregateType == "OrderHandover" && m.AggregateId == handover.Id.ToString());
            outboxMessage.Should().NotBeNull("OutboxMessage must be queued inside the same database transaction");
            outboxMessage!.EventType.Should().Be("OrderHandoverCompleted");

            // Invariant 7: PickupScan record persisted
            var scan = await db.PickupScans.FirstOrDefaultAsync(s => s.OrderId == dailyOrderId);
            scan.Should().NotBeNull();
            scan!.PhotoPath.Should().Be("uploads/driver/condition/proof-99.jpg");
        }
    }
}
