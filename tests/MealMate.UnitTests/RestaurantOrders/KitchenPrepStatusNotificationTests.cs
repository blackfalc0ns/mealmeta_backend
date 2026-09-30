using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOrders;

public class KitchenPrepStatusNotificationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task UpdateDailyOrderPrepStatusAsync_TriggersKitchenBatchReadyNotification_WhenStatusIsReady()
    {
        // Arrange
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة الاختبار",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var day = new SubscriptionDay
        {
            RestaurantId = restaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.Preparing,
            BoxCount = 3
        };
        db.RestaurantDailyOrders.Add(dailyOrder);
        db.MealBarcodes.Add(new MealBarcode
        {
            SubscriptionDayId = day.Id,
            OrderId = dailyOrder.Id,
            BarcodeValue = $"MM-{day.Id:N}"[..23],
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        });
        db.MealInvoices.Add(new MealInvoice
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            InvoiceNumber = $"INV-{day.Id:N}"[..24],
            TotalBoxes = 3
        });
        db.MealLabels.Add(new MealLabel
        {
            SubscriptionDayId = day.Id,
            Kind = MealLabelKind.Box,
            LabelPayloadJson = "{}",
            GeneratedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var customerPush = Substitute.For<ICustomerPushNotifier>();
        var restaurantNotifier = Substitute.For<IRestaurantOrderNotifier>();

        var service = new RestaurantOrderService(
            db,
            currentUser,
            clock,
            customerPush,
            restaurantNotifier);

        // Act
        var result = await service.UpdateDailyOrderPrepStatusAsync(
            new UpdateDailyOrderPrepStatusCommand(day.Id, "ready"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        dailyOrder.Status.Should().Be(OrderStatus.ReadyForPickup);

        await restaurantNotifier.Received(1).NotifyKitchenBatchReadyAsync(
            restaurant.Id,
            day.Id,
            3,
            Arg.Any<CancellationToken>());
    }
}
