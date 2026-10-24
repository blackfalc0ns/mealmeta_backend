using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverDeliveryQrTests
{
    private const string Proof = "uploads/drivers/delivery/qr-proof.jpg";

    [Theory]
    [InlineData(null, "Orders.DeliveryBarcodeRequired")]
    [InlineData("  ", "Orders.DeliveryBarcodeRequired")]
    [InlineData("123", "Orders.BarcodeMismatch")]
    [InlineData("OTHER-QR", "Orders.BarcodeMismatch")]
    public async Task Online_rejects_missing_or_different_pickup_QR_without_delivery_side_effects(string? code, string expected)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var result = await f.Online.Handle(new CompleteBoxDeliveryCommand(f.Stop.Id, code, Proof), default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(expected);
        f.Stop.Status.Should().Be(TripStopStatus.PickedUp);
        f.Stop.DeliveredAtUtc.Should().BeNull();
        f.Session.CompletedOrdersCount.Should().Be(0);
        (await f.Db.DeliveryConfirmationScans.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-order")]
    [InlineData("other-trip")]
    [InlineData("other-driver")]
    public async Task Online_requires_pickup_record_bound_to_current_order_trip_and_driver(string scenario)
    {
        await using var f = new Fixture();
        if (scenario == "missing") f.Db.PickupScans.Remove(f.Pickup);
        if (scenario == "other-order") f.Pickup.OrderId = Guid.NewGuid();
        if (scenario == "other-trip") f.Pickup.TripId = Guid.NewGuid();
        if (scenario == "other-driver") f.Pickup.DriverProfileId = Guid.NewGuid();
        await f.SeedAsync();
        var result = await f.Online.Handle(new CompleteBoxDeliveryCommand(f.Stop.Id, "LABEL-QR-123", Proof), default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Orders.DeliveryPickupScanRequired");
        f.Stop.Status.Should().Be(TripStopStatus.PickedUp);
    }

    [Fact]
    public async Task Online_accepts_same_pickup_QR_even_when_different_from_display_order_code_and_replays_once()
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var command = new CompleteBoxDeliveryCommand(f.Stop.Id, "LABEL-QR-123", Proof);
        var first = await f.Online.Handle(command, default);
        first.IsSuccess.Should().BeTrue();
        var repeat = await f.Online.Handle(command, default);
        repeat.IsSuccess.Should().BeTrue();
        repeat.Value.IsFirstDelivery.Should().BeFalse();
        f.Session.CompletedOrdersCount.Should().Be(1);
        (await f.Db.DeliveryConfirmationScans.SingleAsync()).BarcodeValue.Should().Be("LABEL-QR-123");
    }

    [Theory]
    [InlineData("deliver", null, false)]
    [InlineData("delivery", "123", false)]
    [InlineData("deliveryconfirmation", "OTHER-QR", false)]
    [InlineData("deliver", "LABEL-QR-123", true)]
    public async Task Offline_enforces_same_pickup_QR_for_every_delivery_alias(string action, string? code, bool succeeds)
    {
        await using var f = new Fixture();
        await f.SeedAsync();
        var handler = new DriverOfflineSyncCommandHandler(f.Db, f.User, f.Messages,
            DriverDeliveryTestConcurrency.Transactions(), DriverDeliveryTestConcurrency.Locks(), f.Evidence);
        var result = await handler.Handle(new SyncDriverOfflineQueueCommand(new[]
        {
            new OfflineQueueItemDto(Guid.NewGuid(), action, f.Stop.Id, code, Proof)
        }), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Single().IsSuccess.Should().Be(succeeds);
        f.Stop.Status.Should().Be(succeeds ? TripStopStatus.Delivered : TripStopStatus.PickedUp);
        (await f.Db.DeliveryConfirmationScans.CountAsync()).Should().Be(succeeds ? 1 : 0);
        f.Session.CompletedOrdersCount.Should().Be(succeeds ? 1 : 0);
    }

    [Theory]
    [InlineData("INVENTED-QR", false)]
    [InlineData("BX-DISPLAY-123", true)]
    public async Task Offline_pickup_then_delivery_in_one_batch_requires_a_real_order_code(string code, bool succeeds)
    {
        await using var f = new Fixture();
        f.Db.PickupScans.Remove(f.Pickup);
        f.Stop.Status = TripStopStatus.Pending;
        await f.SeedAsync();
        var handler = new DriverOfflineSyncCommandHandler(f.Db, f.User, f.Messages,
            DriverDeliveryTestConcurrency.Transactions(), DriverDeliveryTestConcurrency.Locks(), f.Evidence);
        var result = await handler.Handle(new SyncDriverOfflineQueueCommand(new[]
        {
            new OfflineQueueItemDto(Guid.NewGuid(), "pickup", f.Stop.Id, code, Proof),
            new OfflineQueueItemDto(Guid.NewGuid(), "deliver", f.Stop.Id, code, Proof)
        }), default);
        result.Value.Results.Should().OnlyContain(x => x.IsSuccess == succeeds);
        f.Stop.Status.Should().Be(succeeds ? TripStopStatus.Delivered : TripStopStatus.Pending);
        (await f.Db.DeliveryConfirmationScans.CountAsync()).Should().Be(succeeds ? 1 : 0);
    }

    [Theory]
    [InlineData(TripStopStatus.Pending)]
    [InlineData(TripStopStatus.Failed)]
    [InlineData(TripStopStatus.ReturnedToKitchen)]
    public async Task Online_cannot_deliver_a_box_not_in_picked_up_state(TripStopStatus status)
    {
        await using var f = new Fixture();
        f.Stop.Status = status;
        await f.SeedAsync();
        var result = await f.Online.Handle(new CompleteBoxDeliveryCommand(f.Stop.Id, "LABEL-QR-123", Proof), default);
        result.IsSuccess.Should().BeFalse();
        f.Stop.Status.Should().Be(status);
        (await f.Db.DeliveryConfirmationScans.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Completed", "Received", true)]
    [InlineData("Receiving", "Received", false)]
    [InlineData("Completed", "Missing", false)]
    public async Task Replacement_driver_requires_completed_custody_transfer_for_original_pickup_QR(string handoverStatus, string boxStatus, bool succeeds)
    {
        await using var f = new Fixture();
        var originalTrip = Guid.NewGuid();
        var originalDriver = Guid.NewGuid();
        var replacementDriver = f.Pickup.DriverProfileId;
        f.Pickup.TripId = originalTrip;
        f.Pickup.DriverProfileId = originalDriver;
        var handover = new DriverSupportHandover
        {
            TripId = originalTrip, ReplacementTripId = f.Stop.TripId,
            OriginalDriverProfileId = originalDriver, ReplacementDriverProfileId = replacementDriver,
            Status = handoverStatus, CompletedAtUtc = handoverStatus == "Completed" ? DateTime.UtcNow : null,
            IdempotencyKey = "qr-transfer", PickupLocationSource = "Driver"
        };
        handover.Boxes.Add(new DriverSupportHandoverBox
        {
            TripStopId = f.Stop.Id, ManifestCode = f.Stop.OrderCode, Status = boxStatus,
            ScannedAtUtc = DateTime.UtcNow, ScanMethod = "Barcode"
        });
        f.Db.DriverSupportHandovers.Add(handover);
        await f.SeedAsync();
        var result = await f.Online.Handle(new CompleteBoxDeliveryCommand(f.Stop.Id, "LABEL-QR-123", Proof), default);
        result.IsSuccess.Should().Be(succeeds);
        f.Stop.Status.Should().Be(succeeds ? TripStopStatus.Delivered : TripStopStatus.PickedUp);
    }

    [Theory]
    [InlineData("ACTIVE-QR", true)]
    [InlineData("OLD-QR", false)]
    [InlineData("OTHER-LABEL", false)]
    [InlineData("BX-DISPLAY-123", false)]
    public async Task Offline_pickup_requires_current_assigned_label_and_supports_hash_prefix(string code, bool succeeds)
    {
        await using var f = new Fixture();
        f.Db.PickupScans.Remove(f.Pickup);
        f.Stop.Status = TripStopStatus.Pending;
        var dayId = Guid.NewGuid();
        f.Stop.SubscriptionDayId = dayId;
        var active = new MealBarcode { SubscriptionDayId = dayId, OrderId = f.Stop.DailyOrderId,
            TripStopId = f.Stop.Id, BarcodeValue = "#ACTIVE-QR", Status = MealBarcodeStatus.Printed };
        f.Stop.MealBarcodeId = active.Id;
        f.Db.AddRange(active,
            new MealBarcode { SubscriptionDayId = dayId, BarcodeValue = "OLD-QR", Status = MealBarcodeStatus.Revoked, IsRevoked = true, ReissuedBarcodeId = active.Id },
            new MealBarcode { SubscriptionDayId = dayId, BarcodeValue = "OTHER-LABEL", Status = MealBarcodeStatus.Printed });
        await f.SeedAsync();
        var handler = new DriverOfflineSyncCommandHandler(f.Db, f.User, f.Messages,
            DriverDeliveryTestConcurrency.Transactions(), DriverDeliveryTestConcurrency.Locks(), f.Evidence);
        var result = await handler.Handle(new SyncDriverOfflineQueueCommand(new[]
        {
            new OfflineQueueItemDto(Guid.NewGuid(), "pickup", f.Stop.Id, code, Proof),
            new OfflineQueueItemDto(Guid.NewGuid(), "deliver", f.Stop.Id, code, Proof)
        }), default);
        result.Value.Results.Should().OnlyContain(x => x.IsSuccess == succeeds);
        f.Stop.Status.Should().Be(succeeds ? TripStopStatus.Delivered : TripStopStatus.Pending);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public ICurrentUser User { get; } = Substitute.For<ICurrentUser>();
        public ILocalizedMessageProvider Messages { get; } = Substitute.For<ILocalizedMessageProvider>();
        public TripStop Stop { get; }
        public PickupScan Pickup { get; }
        public DriverWorkSession Session { get; }
        public SupportEvidenceScanService Evidence { get; }
        public DriverDeliveryOrderCommandHandlers Online { get; }

        public Fixture()
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(DriverDeliveryTestConcurrency.ActiveDriverAccounts()).Options,
                Substitute.For<IDomainEventDispatcher>());
            var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
            User.IsAuthenticated.Returns(true);
            User.UserId.Returns(driver.UserId);
            User.UserType.Returns(UserType.Driver);
            Messages.CurrentCulture.Returns(new System.Globalization.CultureInfo("en"));
            Messages.GetMessage(Arg.Any<string>(), Arg.Any<string>()).Returns(x => x.ArgAt<string>(1));
            var trip = new Trip { Code = "QR-TRIP", RestaurantId = Guid.NewGuid(), Status = TripStatus.InProgress };
            Stop = new TripStop { TripId = trip.Id, Trip = trip, DailyOrderId = Guid.NewGuid(), OrderCode = "BX-DISPLAY-123", Status = TripStopStatus.PickedUp,
                CustomerMaskedId = "CUSTOMER", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "12:00", ZoneNameAr = "منطقة", ZoneNameEn = "Zone" };
            Pickup = new PickupScan { TripId = trip.Id, OrderId = Stop.DailyOrderId, DriverProfileId = driver.Id, BarcodeValue = "LABEL-QR-123", ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5) };
            Session = new DriverWorkSession { DriverProfileId = driver.Id, Status = "Active" };
            Db.AddRange(driver, trip, Stop, Pickup, Session,
                new DriverAssignment { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active" },
                new UploadFileRecord { StorageKey = Proof, Scope = "drivers", Folder = "delivery", FileName = "qr-proof.jpg", ContentType = "image/jpeg", SizeBytes = 3, UploadedByUserId = driver.UserId });
            var storage = Substitute.For<IFileStorage>();
            storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 255, 216, 255 })));
            var receipts = Substitute.For<ISupportEvidenceScanReceiptStore>();
            receipts.GetOrScanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<Func<CancellationToken, Task<EvidenceScanResult>>>(), Arg.Any<CancellationToken>())
                .Returns(new EvidenceScanResult(EvidenceScanVerdict.Clean, "test", "clean"));
            Evidence = new SupportEvidenceScanService(storage, Substitute.For<IEvidenceMalwareScanner>(), receipts);
            Online = new DriverDeliveryOrderCommandHandlers(Db, User, Messages, fileStorage: storage,
                transactions: DriverDeliveryTestConcurrency.Transactions(), locks: DriverDeliveryTestConcurrency.Locks(), evidenceScans: Evidence);
        }

        public Task SeedAsync() => Db.SaveChangesAsync();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
