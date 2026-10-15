using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOrders;

public class OrderWorkspaceContractsTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
    private RestaurantOrderService Service(AppDbContext db)
    {
        var user = Substitute.For<ICurrentUser>();
        user.RestaurantId.Returns(_restaurantId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_now);
        return new(db, user, clock);
    }
    private SubscriptionDay Day(DateOnly date, SubscriptionDayStatus status) => new()
    {
        RestaurantId = _restaurantId, SubscriptionId = Guid.NewGuid(),
        ServiceDate = date, Status = status, LockAtUtc = _now.AddHours(-1), CreatedAtUtc = _now.AddHours(-1)
    };

    [Fact]
    public async Task Daily_default_excludes_old_and_future_orders_and_uses_business_date()
    {
        await using var db = CreateDb();
        var today = Day(new(2026, 10, 15), SubscriptionDayStatus.ConfirmedByRestaurant);
        db.SubscriptionDays.AddRange(today, Day(new(2026, 10, 14), SubscriptionDayStatus.Preparing),
            Day(new(2026, 10, 16), SubscriptionDayStatus.ConfirmedByRestaurant));
        await db.SaveChangesAsync();
        var result = await Service(db).GetDailyOrdersAsync(new(), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(today.Id, Assert.Single(result.Value.Orders).Id);
    }

    [Fact]
    public async Task Daily_barcode_readiness_comes_from_nonrevoked_persisted_barcode()
    {
        await using var db = CreateDb();
        var preparing = Day(new(2026, 10, 15), SubscriptionDayStatus.Preparing);
        var readyWithoutBarcode = Day(new(2026, 10, 15), SubscriptionDayStatus.Preparing);
        db.SubscriptionDays.AddRange(preparing, readyWithoutBarcode);
        db.RestaurantDailyOrders.Add(new() { RestaurantId = _restaurantId, SubscriptionDayId = readyWithoutBarcode.Id,
            ServiceDate = readyWithoutBarcode.ServiceDate, Status = OrderStatus.ReadyForPickup });
        db.MealBarcodes.Add(new() { SubscriptionDayId = preparing.Id, BarcodeValue = "persisted-barcode", IsRevoked = false });
        await db.SaveChangesAsync();
        var result = await Service(db).GetDailyOrdersAsync(new(), default);
        Assert.True(result.Value.Orders.Single(x => x.Id == preparing.Id).BarcodeReady);
        Assert.False(result.Value.Orders.Single(x => x.Id == readyWithoutBarcode.Id).BarcodeReady);
    }

    [Fact]
    public async Task Missing_program_and_bundle_are_reported_as_missing_instead_of_fabricated_business_names()
    {
        await using var db = CreateDb();
        db.SubscriptionDays.Add(Day(new(2026, 10, 15), SubscriptionDayStatus.Preparing));
        await db.SaveChangesAsync();
        var result = await Service(db).GetUpcoming24hAsync(new(), default);
        var item = Assert.Single(result.Value.Orders);
        Assert.Equal("Program unavailable", item.ProgramLabel.En);
        Assert.Equal("Bundle unavailable", item.BundleLabel.En);
        Assert.Equal("Meals unavailable", item.MealSummary.En);
    }

    [Fact]
    public async Task Confirmation_rejects_expired_order_before_escalation_job_runs()
    {
        await using var db = CreateDb();
        var day = Day(new(2026, 10, 17), SubscriptionDayStatus.SentToRestaurant);
        day.LockAtUtc = _now.AddHours(-25);
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new() { RestaurantId = _restaurantId, SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate, Status = OrderStatus.AwaitingRestaurantConfirmation, CreatedAtUtc = _now.AddHours(-24) });
        await db.SaveChangesAsync();
        var result = await Service(db).ConfirmOrderAsync(new(day.Id), default);
        Assert.True(result.IsFailure);
        Assert.Equal("orders.confirmation_expired", result.Error.Code);
        Assert.Equal(SubscriptionDayStatus.SentToRestaurant, day.Status);
        Assert.Null(day.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Rejection_cannot_mutate_expired_order_before_escalation()
    {
        await using var db = CreateDb();
        var day = Day(new(2026, 10, 17), SubscriptionDayStatus.SentToRestaurant);
        day.LockAtUtc = _now.AddHours(-24);
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();
        var result = await Service(db).RejectOrderAsync(new(day.Id, "Capacity"), default);
        Assert.True(result.IsFailure);
        Assert.Equal("orders.confirmation_expired", result.Error.Code);
        Assert.Equal(SubscriptionDayStatus.SentToRestaurant, day.Status);
    }

    [Fact]
    public async Task Confirmation_uses_published_minute_SLA_instead_of_legacy_24_hours()
    {
        await using var db = CreateDb();
        db.SystemSettings.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        {
            Key = "operational_policies.settings", IsActive = true,
            ValueJson = """{"cutoff":{"kitchenConfirmSlaMinutes":30},"cancellation":{},"freeze":{}}"""
        });
        var day = Day(new(2026, 10, 17), SubscriptionDayStatus.SentToRestaurant);
        day.LockAtUtc = _now.AddMinutes(-30);
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();
        var result = await Service(db).ConfirmOrderAsync(new(day.Id), default);
        Assert.True(result.IsFailure);
        Assert.Equal("orders.confirmation_expired", result.Error.Code);
    }

    [Fact]
    public async Task Upcoming_kitchen_ready_uses_persisted_daily_status()
    {
        await using var db = CreateDb();
        var day = Day(new(2026, 10, 15), SubscriptionDayStatus.Preparing);
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new() { RestaurantId = _restaurantId, SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate, Status = OrderStatus.ReadyForPickup });
        await db.SaveChangesAsync();
        var result = await Service(db).GetUpcoming24hAsync(new(), default);
        var item = Assert.Single(result.Value.Orders);
        Assert.Equal("ready-for-pickup", item.KitchenStatus);
    }

    [Fact]
    public async Task Pending_exposes_service_date_and_deadline_from_received_order()
    {
        await using var db = CreateDb();
        var day = Day(new(2026, 10, 17), SubscriptionDayStatus.SentToRestaurant);
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new() { RestaurantId = _restaurantId, SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate, Status = OrderStatus.AwaitingRestaurantConfirmation, CreatedAtUtc = _now.AddHours(-2) });
        await db.SaveChangesAsync();
        var result = await Service(db).GetPendingConfirmationAsync(new(), default);
        var item = Assert.Single(result.Value.Orders);
        Assert.Equal(new DateOnly(2026, 10, 17), item.ServiceDate);
        Assert.Equal(new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc), item.ConfirmationDeadlineUtc);
    }

    [Fact]
    public async Task Upcoming_does_not_invent_delivery_time_and_keeps_kitchen_independent_of_print()
    {
        await using var db = CreateDb();
        var day = Day(new(2026, 10, 15), SubscriptionDayStatus.Preparing);
        db.SubscriptionDays.Add(day);
        db.RestaurantDailyOrders.Add(new() { RestaurantId = _restaurantId, SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate, Status = OrderStatus.Preparing });
        db.MealBarcodes.Add(new() { SubscriptionDayId = day.Id, BarcodeValue = "workspace-test", Status = MealBarcodeStatus.Printed });
        await db.SaveChangesAsync();
        var result = await Service(db).GetUpcoming24hAsync(new(), default);
        var item = Assert.Single(result.Value.Orders);
        Assert.Null(item.HoursToDelivery);
        Assert.Null(item.ScheduledDeliveryAtUtc);
        Assert.Equal("preparing", item.KitchenStatus);
        Assert.Equal("partial", item.DocumentsStatus);
        Assert.True(item.IsPrinted);
        Assert.Equal("printed", item.PrepStatus);
    }
}
