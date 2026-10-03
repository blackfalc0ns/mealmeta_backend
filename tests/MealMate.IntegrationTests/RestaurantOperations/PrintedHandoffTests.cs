using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace MealMate.IntegrationTests.RestaurantOperations;

/// <summary>
/// Printing the box labels is the kitchen's final step: the barcode is marked Printed
/// AND the daily order must reach ReadyForPickup so it appears in the delivery
/// dispatcher queue (which filters on OrderStatus.ReadyForPickup).
/// </summary>
public class PrintedHandoffTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("mealmate_test_printed_handoff")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    private static async Task<(Guid RestaurantId, SubscriptionDay Day, DateTime? ReadyAtUtc)> SeedAsync(
        AppDbContext db,
        SubscriptionDayStatus dayStatus,
        OrderStatus orderStatus,
        MealBarcodeStatus barcodeStatus,
        bool withDailyOrder = true)
    {
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = restaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = dayStatus
        };
        db.SubscriptionDays.Add(day);

        DateTime? readyAtUtc = null;
        if (withDailyOrder)
        {
            readyAtUtc = orderStatus >= OrderStatus.ReadyForPickup
                ? DateTime.UtcNow.AddMinutes(-30)
                : null;
            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurant.Id,
                SubscriptionDayId = day.Id,
                ServiceDate = day.ServiceDate,
                Status = orderStatus,
                ReadyForPickupAtUtc = readyAtUtc,
                BoxCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "#BX-9001",
            GeneratedAtUtc = DateTime.UtcNow,
            Status = barcodeStatus
        });

        await db.SaveChangesAsync();
        return (restaurant.Id, day, readyAtUtc);
    }

    private static (RestaurantOrderService Service,
        IRestaurantOrderNotifier RestaurantNotifier,
        IBatchDispatchNotifier BatchNotifier) BuildService(AppDbContext db, Guid restaurantId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.RestaurantId.Returns(restaurantId);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Restaurant);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var restaurantNotifier = Substitute.For<IRestaurantOrderNotifier>();
        var batchNotifier = Substitute.For<IBatchDispatchNotifier>();

        var service = new RestaurantOrderService(
            db, currentUser, clock,
            restaurantNotifier: restaurantNotifier,
            batchDispatchNotifier: batchNotifier);

        return (service, restaurantNotifier, batchNotifier);
    }

    [Fact]
    public async Task MarkPrinted_FromRestaurantConfirmed_AdvancesOrderToReadyForPickup()
    {
        // Arrange: the state left behind by the print-only flow (order still confirmed)
        await using var db = CreateDbContext();
        var (restaurantId, day, _) = await SeedAsync(
            db, SubscriptionDayStatus.ConfirmedByRestaurant, OrderStatus.RestaurantConfirmed,
            MealBarcodeStatus.Generated);
        var (service, restaurantNotifier, batchNotifier) = BuildService(db, restaurantId);

        // Act
        var result = await service.MarkOrderBarcodePrintedAsync(
            new MarkOrderBarcodePrintedCommand(day.Id), CancellationToken.None);

        // Assert: full kitchen handoff
        result.IsSuccess.Should().BeTrue();

        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.Preparing);

        var order = await db.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == day.Id);
        order.Status.Should().Be(OrderStatus.ReadyForPickup);
        order.ReadyForPickupAtUtc.Should().NotBeNull();

        var barcode = await db.MealBarcodes.SingleAsync(b => b.SubscriptionDayId == day.Id);
        barcode.Status.Should().Be(MealBarcodeStatus.Printed);
        barcode.PrintedAtUtc.Should().NotBeNull();

        var outbox = await db.OutboxMessages
            .SingleAsync(m => m.EventType == "KitchenOrderReadyForPickup");
        outbox.PayloadJson.Should().Contain(order.Id.ToString());

        await restaurantNotifier.Received(1).NotifyKitchenBatchReadyAsync(
            Arg.Any<Guid>(), day.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await batchNotifier.Received(1).NotifyIfBatchThresholdReachedAsync(
            Arg.Any<Guid>(), day.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPrinted_FromPreparing_AdvancesOrderToReadyForPickup()
    {
        await using var db = CreateDbContext();
        var (restaurantId, day, _) = await SeedAsync(
            db, SubscriptionDayStatus.Preparing, OrderStatus.Preparing,
            MealBarcodeStatus.Generated);
        var (service, restaurantNotifier, batchNotifier) = BuildService(db, restaurantId);

        var result = await service.MarkOrderBarcodePrintedAsync(
            new MarkOrderBarcodePrintedCommand(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var order = await db.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == day.Id);
        order.Status.Should().Be(OrderStatus.ReadyForPickup);
        order.ReadyForPickupAtUtc.Should().NotBeNull();
        (await db.OutboxMessages.CountAsync(m => m.EventType == "KitchenOrderReadyForPickup"))
            .Should().Be(1);
        await restaurantNotifier.Received(1).NotifyKitchenBatchReadyAsync(
            Arg.Any<Guid>(), day.Id, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await batchNotifier.Received(1).NotifyIfBatchThresholdReachedAsync(
            Arg.Any<Guid>(), day.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPrinted_ReprintOfAlreadyReadyOrder_DoesNotFireDuplicateHandoff()
    {
        // Arrange: order already handed to dispatch (recovery path: stuck orders reprinted)
        await using var db = CreateDbContext();
        var (restaurantId, day, readyAtUtc) = await SeedAsync(
            db, SubscriptionDayStatus.Preparing, OrderStatus.ReadyForPickup,
            MealBarcodeStatus.Printed);
        var (service, restaurantNotifier, batchNotifier) = BuildService(db, restaurantId);

        // Act: reprint must be harmless
        var result = await service.MarkOrderBarcodePrintedAsync(
            new MarkOrderBarcodePrintedCommand(day.Id), CancellationToken.None);

        // Assert: no rewind, no duplicate outbox, no repeat notifications
        result.IsSuccess.Should().BeTrue();
        var order = await db.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == day.Id);
        order.Status.Should().Be(OrderStatus.ReadyForPickup);
        order.ReadyForPickupAtUtc.Should().Be(readyAtUtc);
        (await db.OutboxMessages.CountAsync(m => m.EventType == "KitchenOrderReadyForPickup"))
            .Should().Be(0);
        await restaurantNotifier.DidNotReceive().NotifyKitchenBatchReadyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await batchNotifier.DidNotReceive().NotifyIfBatchThresholdReachedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPrinted_WithoutDailyOrder_CreatesReadyOrder()
    {
        await using var db = CreateDbContext();
        var (restaurantId, day, _) = await SeedAsync(
            db, SubscriptionDayStatus.ConfirmedByRestaurant, OrderStatus.RestaurantConfirmed,
            MealBarcodeStatus.Generated, withDailyOrder: false);
        var (service, _, batchNotifier) = BuildService(db, restaurantId);

        var result = await service.MarkOrderBarcodePrintedAsync(
            new MarkOrderBarcodePrintedCommand(day.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var order = await db.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == day.Id);
        order.Status.Should().Be(OrderStatus.ReadyForPickup);
        order.ReadyForPickupAtUtc.Should().NotBeNull();
        var reloadedDay = await db.SubscriptionDays.FindAsync(day.Id);
        reloadedDay!.Status.Should().Be(SubscriptionDayStatus.Preparing);
        (await db.OutboxMessages.CountAsync(m => m.EventType == "KitchenOrderReadyForPickup"))
            .Should().Be(1);
        await batchNotifier.Received(1).NotifyIfBatchThresholdReachedAsync(
            Arg.Any<Guid>(), day.Id, Arg.Any<CancellationToken>());
    }
}
