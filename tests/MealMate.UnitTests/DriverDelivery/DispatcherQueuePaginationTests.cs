using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DispatcherQueuePaginationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Handle_PaginatesUnassignedDays_WithoutHardcoded15Limit()
    {
        // Arrange: 40 unassigned ready days in the restaurant
        await using var db = CreateDb();
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم التجربة",
            TradeNameEn = "Test Restaurant",
            LegalNameAr = "شركة التجربة",
            LegalNameEn = "Test Company"
        };
        db.RestaurantProfiles.Add(restaurant);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 0; i < 40; i++)
        {
            var day = new SubscriptionDay
            {
                RestaurantId = restaurant.Id,
                ServiceDate = today,
                Status = SubscriptionDayStatus.ConfirmedByRestaurant,
                DeliveryAddressLine = $"شارع {i + 1}"
            };
            db.SubscriptionDays.Add(day);

            db.RestaurantDailyOrders.Add(new RestaurantDailyOrder
            {
                RestaurantId = restaurant.Id,
                SubscriptionDayId = day.Id,
                ServiceDate = today,
                Status = OrderStatus.ReadyForPickup,
                BoxCount = 1
            });
        }
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.RestaurantId.Returns(restaurant.Id);
        var messages = NullLocalizedMessageProvider.Instance;
        var recommendationEngine = Substitute.For<IDispatchRecommendationEngine>();

        var handler = new GetDispatcherOrderQueueQueryHandler(db, currentUser, messages, recommendationEngine);

        // Act 1: Request Page 1 with PageSize = 25
        var page1Result = await handler.Handle(
            new GetDispatcherOrderQueueQuery(restaurant.Id, Filter: null, Page: 1, PageSize: 25),
            CancellationToken.None);

        // Assert Page 1
        page1Result.IsSuccess.Should().BeTrue();
        var page1 = page1Result.Value;
        page1.Counts.PendingCount.Should().Be(40);
        page1.Counts.TotalCount.Should().Be(40);
        page1.Boxes.Should().HaveCount(25);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(25);
        page1.TotalBoxes.Should().Be(40);
        page1.TotalPages.Should().Be(2);
        page1.HasNextPage.Should().BeTrue();

        // Act 2: Request Page 2 with PageSize = 25
        var page2Result = await handler.Handle(
            new GetDispatcherOrderQueueQuery(restaurant.Id, Filter: null, Page: 2, PageSize: 25),
            CancellationToken.None);

        // Assert Page 2
        page2Result.IsSuccess.Should().BeTrue();
        var page2 = page2Result.Value;
        page2.Boxes.Should().HaveCount(15);
        page2.Page.Should().Be(2);
        page2.PageSize.Should().Be(25);
        page2.TotalBoxes.Should().Be(40);
        page2.TotalPages.Should().Be(2);
        page2.HasNextPage.Should().BeFalse();
    }
}
