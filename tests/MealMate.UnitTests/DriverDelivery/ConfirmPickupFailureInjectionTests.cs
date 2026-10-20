using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Testing;
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

public class ConfirmPickupFailureInjectionTests
{
    private sealed class TestFailureInjector : IOrderLifecycleFailureInjector
    {
        private readonly string _targetBoundary;

        public TestFailureInjector(string targetBoundary)
        {
            _targetBoundary = targetBoundary;
        }

        public Task ExecuteAsync(string boundary, CancellationToken cancellationToken = default)
        {
            if (string.Equals(boundary, _targetBoundary, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Injected atomic failure at boundary: {boundary}");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RollbackSimulatingTransactionExecutor : IApplicationTransactionExecutor
    {
        private readonly AppDbContext _db;

        public RollbackSimulatingTransactionExecutor(AppDbContext db)
        {
            _db = db;
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch
            {
                // Discard all pending mutations in change tracker to simulate rollback
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }
                throw;
            }
        }

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async ct => { await operation(ct); return true; }, cancellationToken);
        }
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(DriverDeliveryTestConcurrency.ActiveDriverAccounts())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Theory]
    [InlineData("AfterValidationConsumed")]
    [InlineData("AfterStopUpdated")]
    [InlineData("AfterDayUpdated")]
    [InlineData("AfterOrderUpdated")]
    [InlineData("AfterHandoverAdded")]
    [InlineData("AfterScanAdded")]
    [InlineData("AfterAuditAdded")]
    [InlineData("AfterOutboxAdded")]
    [InlineData("BeforeCommit")]
    public async Task ConfirmPickup_WhenFailureInjectedAtBoundary_RollsBackAllMutations(string failureBoundary)
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
            OrderCode = "#BX-8888",
            CustomerMaskedId = "CUST-202",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            CustomerName = "خالد العتيبي",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "12:00-14:00",
            BoxCount = 1,
            Status = TripStopStatus.Pending
        };
        db.TripStops.Add(stop);

        var rawToken = "failure-injection-token";
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
            StorageKey = "photos/fail_check.jpg",
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
        var failureInjector = new TestFailureInjector(failureBoundary);
        var txExecutor = new RollbackSimulatingTransactionExecutor(db);

        var handler = new ConfirmPickupBoxCommandHandler(
            db,
            currentUser,
            messages,
            txExecutor,
            failureInjector,
            locks: DriverDeliveryTestConcurrency.Locks());

        var command = new ConfirmPickupBoxCommand(
            BoxId: day.Id,
            ValidationToken: rawToken,
            ConditionPhotoStorageKey: "photos/fail_check.jpg",
            Latitude: 24.77m,
            Longitude: 46.73m,
            IdempotencyKey: Guid.NewGuid().ToString());

        // Act & Assert
        var act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{failureBoundary}*");

        // Verify Rollback: reload all entities and check that nothing was altered
        var reloadedValidation = await db.PickupValidations.FindAsync(validation.Id);
        reloadedValidation!.ConsumedAtUtc.Should().BeNull("Token consumption must be rolled back");

        var reloadedStop = await db.TripStops.FindAsync(stop.Id);
        reloadedStop!.Status.Should().Be(TripStopStatus.Pending, "Stop status must remain Pending");

        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.Preparing, "SubscriptionDay status must remain Preparing");

        var reloadedDailyOrder = await db.RestaurantDailyOrders.FindAsync(dailyOrder.Id);
        reloadedDailyOrder!.Status.Should().Be(OrderStatus.ReadyForPickup, "DailyOrder status must remain ReadyForPickup");

        var handoverCount = await db.OrderHandovers.CountAsync(h => h.DailyOrderId == dailyOrder.Id);
        handoverCount.Should().Be(0, "No OrderHandover row should exist");

        var scanCount = await db.PickupScans.CountAsync(s => s.SubscriptionDayId == day.Id);
        scanCount.Should().Be(0, "No PickupScan row should exist");

        var outboxCount = await db.OutboxMessages.CountAsync(o => o.EventType == "OrderHandoverCompleted");
        outboxCount.Should().Be(0, "No OutboxMessage should exist");
    }
}
