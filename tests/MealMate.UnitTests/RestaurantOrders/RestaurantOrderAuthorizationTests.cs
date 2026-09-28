using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
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

public class RestaurantOrderAuthorizationTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task GetOrderDetail_WhenUserHasNoRestaurantClaimOrProfile_DoesNotFallbackToFirstRestaurantInDb()
    {
        // Arrange: Seed database with existing restaurant profile and order
        await using var db = CreateDb();
        var existingRestaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(existingRestaurant);

        var day = new SubscriptionDay
        {
            RestaurantId = existingRestaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        // Caller is completely unauthenticated / has no restaurant identity
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.RestaurantId.Returns((Guid?)null);
        currentUser.UserId.Returns((Guid?)null);
        currentUser.UserType.Returns((UserType?)null);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var service = new RestaurantOrderService(db, currentUser, clock);

        // Act
        var result = await service.GetOrderDetailAsync(new GetRestaurantOrderDetailQuery(day.Id.ToString()), CancellationToken.None);

        // Assert: Must NOT return data from existingRestaurant; must be Failure
        result.IsFailure.Should().BeTrue("Unauthenticated user without restaurant profile must not access orders");
        result.Error.Code.Should().Be("restaurant.not_found");
    }

    [Fact]
    public async Task ConfirmOrder_WhenUserHasNoRestaurantClaimOrProfile_DoesNotConfirmArbitraryRestaurantOrder()
    {
        await using var db = CreateDb();
        var existingRestaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل",
            LegalNameEn = "Aseel Company"
        };
        db.RestaurantProfiles.Add(existingRestaurant);

        var day = new SubscriptionDay
        {
            RestaurantId = existingRestaurant.Id,
            ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = SubscriptionDayStatus.SentToRestaurant
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.RestaurantId.Returns((Guid?)null);
        currentUser.UserId.Returns((Guid?)null);
        currentUser.UserType.Returns((UserType?)null);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var service = new RestaurantOrderService(db, currentUser, clock);

        // Act
        var result = await service.ConfirmOrderAsync(new ConfirmRestaurantOrderCommand(day.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("Unauthenticated user cannot confirm an order");
        day.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant, "Status must not change on unauthorized request");
    }
}
