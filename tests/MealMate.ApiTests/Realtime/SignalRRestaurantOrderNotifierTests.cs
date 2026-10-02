using FluentAssertions;
using MealMate.Api.Realtime;
using MealMate.Api.Resources;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Realtime;

/// <summary>
/// Contract tests for the restaurant "order sent" notifier: per-user in-app rows,
/// realtime payloads that mark auto-selected dispatches, and best-effort FCM push
/// so the order reaches staff even with no dashboard session open.
/// </summary>
public sealed class SignalRRestaurantOrderNotifierTests
{
    /// <summary>Returns the resource key as the localized value, so tests can
    /// assert which wording variant (standard vs auto) was selected.</summary>
    private sealed class KeyLocalizer : IStringLocalizer<Messages>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private sealed record HubCapture(
        IHubContext<RestaurantReviewHub> Hub,
        List<string> Methods,
        List<object[]> ArgSets);

    private static HubCapture MakeHub()
    {
        var hub = Substitute.For<IHubContext<RestaurantReviewHub>>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        var methods = new List<string>();
        var argSets = new List<object[]>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Do<string>(_ => { })).Returns(proxy);
        proxy.SendCoreAsync(
            Arg.Do<string>(m => methods.Add(m)),
            Arg.Do<object[]>(a => argSets.Add(a)),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return new HubCapture(hub, methods, argSets);
    }

    private static (IFcmMessagingClient Fcm, List<string> Tokens, List<IReadOnlyDictionary<string, string>> Payloads) MakeFcm(bool configured)
    {
        var fcm = Substitute.For<IFcmMessagingClient>();
        var tokens = new List<string>();
        var payloads = new List<IReadOnlyDictionary<string, string>>();
        fcm.IsConfigured.Returns(configured);
        fcm.SendAsync(
                Arg.Do<string>(t => tokens.Add(t)),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, string>>(p => payloads.Add(p)),
                Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());
        return (fcm, tokens, payloads);
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static async Task<(AppDbContext Db, Guid RestaurantId)> SeedRestaurantAsync(Guid ownerUserId, Guid deliveryManagerUserId)
    {
        var db = CreateDb();
        var profile = new RestaurantProfile
        {
            UserId = ownerUserId,
            LegalNameAr = "مطعم اختبار",
            LegalNameEn = "Test Restaurant",
            TradeNameAr = "مطعم اختبار",
            TradeNameEn = "Test Restaurant"
        };
        db.RestaurantProfiles.Add(profile);
        db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration
        {
            RestaurantId = profile.Id,
            ResultingUserId = deliveryManagerUserId,
            IsDeleted = false,
            FullName = "مدير توصيل اختبار",
            Phone = "+96550000000",
            Email = "dm@test.local",
            NationalId = "000000000000"
        });
        await db.SaveChangesAsync();
        return (db, profile.Id);
    }

    private static SignalRRestaurantOrderNotifier MakeNotifier(
        AppDbContext db, HubCapture hub, IFcmMessagingClient? fcm)
        => new(hub.Hub, db, new KeyLocalizer(), dispatcherHub: null, fcm: fcm, logger: null);



    [Fact]
    public async Task CustomerConfirmedOrder_UsesStandardWording_ForEveryResolvedUser()
    {
        var ownerUserId = Guid.NewGuid();
        var dmUserId = Guid.NewGuid();
        var (db, restaurantId) = await SeedRestaurantAsync(ownerUserId, dmUserId);
        var hub = MakeHub();
        var (fcm, tokens, _) = MakeFcm(configured: false);
        var notifier = MakeNotifier(db, hub, fcm);
        var dayId = Guid.NewGuid();

        await notifier.NotifyOrderSentToRestaurantAsync(restaurantId, dayId, new DateOnly(2026, 10, 5), isAutoSelected: false);

        var rows = await db.RestaurantNotifications.Where(n => n.SubjectId == dayId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { ownerUserId, dmUserId });
        rows.Should().OnlyContain(r => r.Type == "order-sent");
        rows.Should().OnlyContain(r => r.TitleAr == "notifications.order.sent.title_ar"
            && r.TitleEn == "notifications.order.sent.title_en"
            && r.BodyAr == string.Format("notifications.order.sent.body_ar", "2026-10-05"));
        rows.Should().OnlyContain(r => r.Section == "orders/pending-confirmation"
            && r.DeepLink == "/restaurant/orders/pending-confirmation" && !r.IsRead);

        hub.Methods.Should().Contain("restaurant-order-sent");
        hub.Methods.Count(m => m == "restaurant-order-sent").Should().Be(2, "one realtime event per resolved user");
        hub.Methods.Should().Contain("registration-review-updated");
    }

    [Fact]
    public async Task AutoSelectedOrder_UsesAutoWording_AndFlagsTheRealtimePayload()
    {
        var ownerUserId = Guid.NewGuid();
        var dmUserId = Guid.NewGuid();
        var (db, restaurantId) = await SeedRestaurantAsync(ownerUserId, dmUserId);
        var hub = MakeHub();
        var (fcm, _, _) = MakeFcm(configured: false);
        var notifier = MakeNotifier(db, hub, fcm);
        var dayId = Guid.NewGuid();

        await notifier.NotifyOrderSentToRestaurantAsync(restaurantId, dayId, new DateOnly(2026, 10, 5), isAutoSelected: true);

        var rows = await db.RestaurantNotifications.Where(n => n.SubjectId == dayId).ToListAsync();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.TitleAr == "notifications.order.sent.auto.title_ar"
            && r.BodyAr == string.Format("notifications.order.sent.auto.body_ar", "2026-10-05"));

        var eventNameIndex = hub.Methods.IndexOf("restaurant-order-sent");
        eventNameIndex.Should().BeGreaterThanOrEqualTo(0);
        var payload = hub.ArgSets[eventNameIndex][0];
        payload.GetType().GetProperty("isAutoSelected")!.GetValue(payload).Should().Be(true,
            "the realtime payload must let the dashboard distinguish auto-selected dispatches");
        payload.GetType().GetProperty("subscriptionDayId")!.GetValue(payload).Should().Be(dayId);
    }

    [Fact]
    public async Task FcmConfigured_SendsOnePushPerDistinctActiveToken_ForResolvedUsersAndRestaurant()
    {
        var ownerUserId = Guid.NewGuid();
        var dmUserId = Guid.NewGuid();
        var (db, restaurantId) = await SeedRestaurantAsync(ownerUserId, dmUserId);
        var unrelatedUserId = Guid.NewGuid();
        db.RestaurantDeviceTokens.AddRange(
            new RestaurantDeviceToken { UserId = ownerUserId, Token = "token-owner", IsActive = true },
            new RestaurantDeviceToken { UserId = dmUserId, Token = "token-dm", IsActive = true },
            new RestaurantDeviceToken { UserId = unrelatedUserId, RestaurantId = restaurantId, Token = "token-restaurant", IsActive = true },
            new RestaurantDeviceToken { UserId = unrelatedUserId, RestaurantId = restaurantId, Token = "token-inactive", IsActive = false },
            new RestaurantDeviceToken { UserId = unrelatedUserId, Token = "token-unrelated", IsActive = true });
        await db.SaveChangesAsync();

        var hub = MakeHub();
        var (fcm, tokens, payloads) = MakeFcm(configured: true);
        var notifier = MakeNotifier(db, hub, fcm);
        var dayId = Guid.NewGuid();

        await notifier.NotifyOrderSentToRestaurantAsync(restaurantId, dayId, new DateOnly(2026, 10, 5), isAutoSelected: true);

        tokens.Should().BeEquivalentTo(new[] { "token-owner", "token-dm", "token-restaurant" },
            "active tokens of resolved staff users plus restaurant-keyed tokens, deduplicated; inactive and unrelated tokens are skipped");
        payloads.Should().OnlyContain(p =>
            p["eventKey"] == "order.sent"
            && p["subscriptionDayId"] == dayId.ToString()
            && p["isAutoSelected"] == "true"
            && p["deepLink"] == "/restaurant/orders/pending-confirmation");
    }

    [Fact]
    public async Task FcmFailure_DoesNotBreakThePersistedNotification()
    {
        var ownerUserId = Guid.NewGuid();
        var dmUserId = Guid.NewGuid();
        var (db, restaurantId) = await SeedRestaurantAsync(ownerUserId, dmUserId);
        db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken { UserId = ownerUserId, Token = "token-owner", IsActive = true });
        await db.SaveChangesAsync();

        var hub = MakeHub();
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(true);
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FcmSendResult>(new Exception("fcm down")));
        var notifier = MakeNotifier(db, hub, fcm);
        var dayId = Guid.NewGuid();

        await notifier.NotifyOrderSentToRestaurantAsync(restaurantId, dayId, new DateOnly(2026, 10, 5));

        (await db.RestaurantNotifications.CountAsync(n => n.SubjectId == dayId))
            .Should().Be(2, "in-app notifications are the source of truth and must survive push failures");
    }
}
