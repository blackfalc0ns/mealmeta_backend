using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminOrders;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.AdminOrders;

public class GetConfirmationOverdueOrdersQueryTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task GetConfirmationOverdue_ReturnsOnlyOrders_Exceeding30MinuteSla()
    {
        // Arrange
        await using var db = CreateDb();
        var nowUtc = new DateTime(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);

        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم الشاطئ",
            TradeNameEn = "Beach Restaurant",
            LegalNameAr = "شركة الشاطئ",
            LegalNameEn = "Beach Co"
        };
        db.RestaurantProfiles.Add(restaurant);
        db.SystemSettings.Add(new SystemSetting
        {
            Key = OperationalPolicyService.SettingKey,
            IsActive = true,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(new OperationalPolicySettings
            {
                Cutoff = new CutoffPolicySettings(KitchenConfirmSlaMinutes: 30)
            }, OperationalPolicyRules.JsonOptions)
        });

        // 1. Overdue order: created 45 minutes ago, awaiting confirmation
        var overdueOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(nowUtc),
            Status = OrderStatus.AwaitingRestaurantConfirmation,
            BoxCount = 3,
            CreatedAtUtc = nowUtc.AddMinutes(-45)
        };
        db.RestaurantDailyOrders.Add(overdueOrder);

        // 2. Fresh order: created 10 minutes ago, awaiting confirmation
        var freshOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(nowUtc),
            Status = OrderStatus.AwaitingRestaurantConfirmation,
            BoxCount = 1,
            CreatedAtUtc = nowUtc.AddMinutes(-10)
        };
        db.RestaurantDailyOrders.Add(freshOrder);

        // 3. Old order: created 60 minutes ago, but already confirmed
        var confirmedOrder = new RestaurantDailyOrder
        {
            RestaurantId = restaurant.Id,
            SubscriptionDayId = Guid.NewGuid(),
            ServiceDate = DateOnly.FromDateTime(nowUtc),
            Status = OrderStatus.RestaurantConfirmed,
            BoxCount = 2,
            CreatedAtUtc = nowUtc.AddMinutes(-60)
        };
        db.RestaurantDailyOrders.Add(confirmedOrder);

        await db.SaveChangesAsync();

        var handler = new GetConfirmationOverdueOrdersQueryHandler(db, clock);

        // Act
        var result = await handler.Handle(new GetConfirmationOverdueOrdersQuery(1, 20), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var payload = result.Value;
        payload.TotalCount.Should().Be(1);
        payload.Items.Should().HaveCount(1);

        var item = payload.Items.First();
        item.OrderId.Should().Be(overdueOrder.Id);
        item.RestaurantNameAr.Should().Be("مطعم الشاطئ");
        item.RestaurantNameEn.Should().Be("Beach Restaurant");
        item.BoxCount.Should().Be(3);
        item.OverdueMinutes.Should().Be(45);
    }

    [Fact]
    public async Task GetConfirmationOverdue_WhenNoOrdersOverdue_ReturnsEmptyList()
    {
        // Arrange
        await using var db = CreateDb();
        var nowUtc = new DateTime(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);

        var handler = new GetConfirmationOverdueOrdersQueryHandler(db, clock);

        // Act
        var result = await handler.Handle(new GetConfirmationOverdueOrdersQuery(1, 20), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }
}
