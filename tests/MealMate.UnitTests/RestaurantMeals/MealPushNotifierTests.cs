using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public class MealPushNotifierTests
{
    private readonly AppDbContext _db;
    private readonly IFcmMessagingClient _fcm = Substitute.For<IFcmMessagingClient>();
    private readonly IRegistrationReviewNotifier _realtime = Substitute.For<IRegistrationReviewNotifier>();

    public MealPushNotifierTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _db = new AppDbContext(options, dispatcher);
        _fcm.IsConfigured.Returns(true);
    }

    [Fact]
    public async Task NotifyAdminsMealSubmittedAsync_PersistsAdminNotification_AndSendsFcmToAdminTokens()
    {
        _db.AdminDeviceTokens.Add(new AdminDeviceToken
        {
            UserId = Guid.NewGuid(),
            Token = "admin_token_1",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var notifier = new MealPushNotifier(_db, _fcm, _realtime);
        var mealId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        await notifier.NotifyAdminsMealSubmittedAsync(mealId, restaurantId, "وجبة سمك", "Fish Meal", CancellationToken.None);

        var adminNotif = await _db.AdminNotifications.FirstOrDefaultAsync();
        adminNotif.Should().NotBeNull();
        adminNotif!.TitleAr.Should().Contain("اعتماد وجبة");
        adminNotif.BodyAr.Should().Contain("وجبة سمك");
        adminNotif.PayloadJson.Should().Contain($"/admin/operations/menus?mealId={mealId}");

        await _fcm.Received(1).SendAsync(
            "admin_token_1",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["mealId"] == mealId.ToString() && d["deepLink"] == $"/admin/operations/menus?mealId={mealId}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyRestaurantMealApprovedAsync_PersistsRestaurantNotification_AndSendsFcmToRestaurantTokens()
    {
        var ownerUserId = Guid.NewGuid();
        var profile = new RestaurantProfile
        {
            UserId = ownerUserId,
            LegalNameAr = "مطعم البحر",
            LegalNameEn = "Sea Restaurant",
            TradeNameAr = "مطعم البحر",
            TradeNameEn = "Sea Restaurant"
        };
        _db.RestaurantProfiles.Add(profile);
        var restaurantId = profile.Id;

        _db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            UserId = ownerUserId,
            RestaurantId = restaurantId,
            Token = "rest_token_1",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var notifier = new MealPushNotifier(_db, _fcm, _realtime);
        var mealId = Guid.NewGuid();

        await notifier.NotifyRestaurantMealApprovedAsync(mealId, restaurantId, "وجبة روبيان", "Shrimp Meal", CancellationToken.None);

        var notif = await _db.RestaurantNotifications.FirstOrDefaultAsync(x => x.UserId == ownerUserId);
        notif.Should().NotBeNull();
        notif!.Type.Should().Be("meal.approved");
        notif.TitleAr.Should().Contain("تم اعتماد الوجبة");
        notif.BodyAr.Should().Contain("وجبة روبيان");
        notif.DeepLink.Should().Be($"/restaurant/operations/menu/{mealId}");

        await _fcm.Received(1).SendAsync(
            "rest_token_1",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["mealId"] == mealId.ToString() && d["deepLink"] == $"/restaurant/operations/menu/{mealId}"),
            Arg.Any<CancellationToken>());

        await _realtime.Received(1).NotifyRegistrationUpdated(
            ownerUserId,
            mealId,
            "meal.approved",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyRestaurantMealRejectedAsync_PersistsRestaurantNotification_AndSendsFcmToRestaurantTokens()
    {
        var ownerUserId = Guid.NewGuid();
        var profile = new RestaurantProfile
        {
            UserId = ownerUserId,
            LegalNameAr = "مطعم البحر",
            LegalNameEn = "Sea Restaurant",
            TradeNameAr = "مطعم البحر",
            TradeNameEn = "Sea Restaurant"
        };
        _db.RestaurantProfiles.Add(profile);
        var restaurantId = profile.Id;

        _db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            UserId = ownerUserId,
            RestaurantId = restaurantId,
            Token = "rest_token_1",
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var notifier = new MealPushNotifier(_db, _fcm, _realtime);
        var mealId = Guid.NewGuid();
        var reason = "Invalid ingredient allergens";

        await notifier.NotifyRestaurantMealRejectedAsync(mealId, restaurantId, "وجبة روبيان", "Shrimp Meal", reason, CancellationToken.None);

        var notif = await _db.RestaurantNotifications.FirstOrDefaultAsync(x => x.UserId == ownerUserId);
        notif.Should().NotBeNull();
        notif!.Type.Should().Be("meal.rejected");
        notif.BodyAr.Should().Contain(reason);
        notif.DeepLink.Should().Be($"/restaurant/operations/menu/{mealId}");

        await _fcm.Received(1).SendAsync(
            "rest_token_1",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["reason"] == reason && d["deepLink"] == $"/restaurant/operations/menu/{mealId}"),
            Arg.Any<CancellationToken>());

        await _realtime.Received(1).NotifyRegistrationUpdated(
            ownerUserId,
            mealId,
            "meal.rejected",
            Arg.Any<CancellationToken>());
    }
}
