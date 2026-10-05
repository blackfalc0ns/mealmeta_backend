using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerNotifications;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.UnitTests.CustomerPush;

/// <summary>
/// Customer push + inbox wiring: the inbox entry is stored for every real business
/// event regardless of FCM state, while push delivery logs are written only after a
/// real device success. No simulated sends.
/// </summary>
public sealed class FirebaseCustomerPushNotifierTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ServiceDay = new(2026, 10, 10);

    [Fact]
    public async Task Notify_WithoutTokensOrFcm_StoresInboxAndNoDeliveryLog()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        var dayId = await fx.SeedDayAsync();

        await fx.Notifier.NotifyOrderLockedAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId)).Should().Be(1);
        (await fx.Db.CustomerPushDeliveryLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Notify_Repeated_StoresExactlyOneInboxRow()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        var dayId = await fx.SeedDayAsync();

        await fx.Notifier.NotifyOrderLockedAsync(fx.CustomerId, dayId, ServiceDay);
        await fx.Notifier.NotifyOrderLockedAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId && x.EventKey == CustomerPushEventKeys.OrderLocked72h))
            .Should().Be(1);
    }

    [Fact]
    public async Task Notify_FcmFailureThenSuccess_InboxStaysOneAndLogAppearsOnce()
    {
        var fx = await CreateAsync(fcmConfigured: true);
        var dayId = await fx.SeedDayAsync();
        var token = await fx.SeedDeviceTokenAsync();
        fx.Fcm.SendAsync(token, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Failed("backend down"));

        await fx.Notifier.NotifyOrderConfirmedByRestaurantAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId)).Should().Be(1);
        (await fx.Db.CustomerPushDeliveryLogs.CountAsync()).Should().Be(0);

        fx.Fcm.SendAsync(token, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());
        await fx.Notifier.NotifyOrderConfirmedByRestaurantAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId)).Should().Be(1);
        (await fx.Db.CustomerPushDeliveryLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Notify_AlreadySent_DoesNotExceedUnreadOrDuplicateLog()
    {
        var fx = await CreateAsync(fcmConfigured: true);
        var dayId = await fx.SeedDayAsync();
        var token = await fx.SeedDeviceTokenAsync();
        fx.Fcm.SendAsync(token, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.Success());

        await fx.Notifier.NotifyOrderDeliveredAsync(fx.CustomerId, dayId, ServiceDay);
        await fx.Notifier.NotifyOrderDeliveredAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId)).Should().Be(1);
        (await fx.Db.CustomerPushDeliveryLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Notify_InvalidToken_DeactivatesDeviceWithoutDeliveryLog()
    {
        var fx = await CreateAsync(fcmConfigured: true);
        var dayId = await fx.SeedDayAsync();
        var token = await fx.SeedDeviceTokenAsync();
        fx.Fcm.SendAsync(token, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(FcmSendResult.InvalidToken("unregistered"));

        await fx.Notifier.NotifyOrderOutForDeliveryAsync(fx.CustomerId, dayId, ServiceDay);

        (await fx.Db.CustomerPushDeliveryLogs.CountAsync()).Should().Be(0);
        (await fx.Db.CustomerDeviceTokens.SingleAsync(x => x.Token == token)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Writer_StoresBilingualCopyAndSubscriptionScope()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        var subscriptionId = Guid.NewGuid();
        var dayId = await fx.SeedDayAsync(subscriptionId);

        await fx.Writer.EnsureCreatedAsync(fx.CustomerId, CustomerPushEventKeys.OrderLocked72h, dayId);

        var row = await fx.Db.CustomerNotifications.SingleAsync(x => x.UserId == fx.CustomerId);
        row.SubscriptionId.Should().Be(subscriptionId);
        row.TitleAr.Should().Be("طلبك قيد المعالجة");
        row.TitleEn.Should().Be("Order locked");
        row.BodyAr.Should().Contain("2026-10-10");
        row.BodyEn.Should().Contain("2026-10-10");
        row.DeepLink.Should().Be("mealmate://calendar");
        row.ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Writer_RejectsUnsupportedEventKey()
    {
        var fx = await CreateAsync(fcmConfigured: false);

        var act = () => fx.Writer.EnsureCreatedAsync(fx.CustomerId, "fabricated.event", Guid.NewGuid());

        await act.Should().ThrowAsync<NotSupportedException>();
        (await fx.Db.CustomerNotifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Writer_SkipsWhenBusinessEntityMissing()
    {
        var fx = await CreateAsync(fcmConfigured: false);

        await fx.Writer.EnsureCreatedAsync(fx.CustomerId, CustomerPushEventKeys.OrderDelivered, Guid.NewGuid());

        (await fx.Db.CustomerNotifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Writer_InfluencerEvent_DerivesProfileData()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        fx.Db.InfluencerProfiles.Add(new InfluencerProfile
        {
            UserId = fx.CustomerId,
            DisplayName = "Test Influencer",
            InfluencerCode = "INF-77",
            DefaultCommissionRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(5m),
        });
        await fx.Db.SaveChangesAsync();

        await fx.Writer.EnsureCreatedAsync(fx.CustomerId, CustomerPushEventKeys.InfluencerPromoted, fx.CustomerId);

        var row = await fx.Db.CustomerNotifications.SingleAsync();
        row.SubscriptionId.Should().BeNull();
        row.TitleEn.Should().Contain("Influencer");
        row.BodyEn.Should().Contain("INF-77");
        row.BodyEn.Should().Contain("5%");
        row.BodyAr.Should().Contain("INF-77");
    }

    [Fact]
    public async Task Writer_ReplacedEvent_UsesSnapshotRestaurantName()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        var (subscriptionId, dayId, restaurantId) = await fx.SeedDayWithRestaurantAsync("مطعم البديل", "Replacement Restaurant");

        await fx.Writer.EnsureCreatedAsync(fx.CustomerId, CustomerPushEventKeys.OrderReplaced, dayId);

        var row = await fx.Db.CustomerNotifications.SingleAsync();
        row.SubscriptionId.Should().Be(subscriptionId);
        row.BodyAr.Should().Contain("مطعم البديل");
        row.BodyEn.Should().Contain("Replacement Restaurant");
        row.DeepLink.Should().Be($"/orders/{dayId}");
    }

    [Fact]
    public async Task Writer_PaymentEvent_VerifiesSubscriptionAndScopesIt()
    {
        var fx = await CreateAsync(fcmConfigured: false);
        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = fx.CustomerId,
        };
        fx.Db.Subscriptions.Add(subscription);
        await fx.Db.SaveChangesAsync();

        await fx.Writer.EnsureCreatedAsync(fx.CustomerId, CustomerPushEventKeys.SubscriptionPaymentConfirmed, subscription.Id);

        var row = await fx.Db.CustomerNotifications.SingleAsync();
        row.SubscriptionId.Should().Be(subscription.Id);
        row.TitleAr.Should().Be("أهلاً بك في MealMate");
    }

    private static async Task<Fixture> CreateAsync(bool fcmConfigured)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var fcm = Substitute.For<IFcmMessagingClient>();
        fcm.IsConfigured.Returns(fcmConfigured);
        var writer = new CustomerNotificationWriter(db, clock, NullLogger<CustomerNotificationWriter>.Instance);
        var notifier = new FirebaseCustomerPushNotifier(
            db, fcm, writer, clock, NullLogger<FirebaseCustomerPushNotifier>.Instance);
        return new Fixture(db, fcm, writer, notifier, clock);
    }

    private sealed record Fixture(
        AppDbContext Db,
        IFcmMessagingClient Fcm,
        CustomerNotificationWriter Writer,
        FirebaseCustomerPushNotifier Notifier,
        IDateTimeProvider Clock)
    {
        public Guid CustomerId { get; } = Guid.NewGuid();

        public async Task<Guid> SeedDayAsync(Guid? subscriptionId = null)
        {
            var subId = subscriptionId ?? Guid.NewGuid();
            var day = new SubscriptionDay
            {
                
                SubscriptionId = subId,
                ServiceDate = ServiceDay,
                Status = SubscriptionDayStatus.SentToRestaurant,
            };
            Db.SubscriptionDays.Add(day);
            await Db.SaveChangesAsync();
            return day.Id;
        }

        public async Task<(Guid SubscriptionId, Guid DayId, Guid RestaurantId)> SeedDayWithRestaurantAsync(string nameAr, string nameEn)
        {
            var subscriptionId = Guid.NewGuid();
            var restaurantId = Guid.NewGuid();
            var day = new SubscriptionDay
            {
                
                SubscriptionId = subscriptionId,
                ServiceDate = ServiceDay,
                Status = SubscriptionDayStatus.SentToRestaurant,
                RestaurantId = restaurantId,
            };
            Db.SubscriptionDays.Add(day);
            Db.SubscriptionRestaurantSnapshots.Add(new MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = Guid.NewGuid(),
                SubscriptionId = subscriptionId,
                RestaurantId = restaurantId,
                ProgramId = Guid.NewGuid(),
                BundleId = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                Tier = RestaurantTier.Basic,
                NameAr = nameAr,
                NameEn = nameEn,
                OfferingSnapshotJson = "{}",
                CapturedAtUtc = DateTime.UtcNow,
            });
            await Db.SaveChangesAsync();
            return (subscriptionId, day.Id, restaurantId);
        }

        public async Task<string> SeedDeviceTokenAsync()
        {
            var token = $"token-{Guid.NewGuid():N}";
            Db.CustomerDeviceTokens.Add(new CustomerDeviceToken
            {
                UserId = CustomerId,
                Token = token,
                Platform = MobilePlatform.Android,
                IsActive = true,
            });
            await Db.SaveChangesAsync();
            return token;
        }
    }
}
