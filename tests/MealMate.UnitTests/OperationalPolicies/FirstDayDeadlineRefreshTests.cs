using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.CustomerCheckout.BackgroundJobs;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;

/// <summary>
/// Regression tests for the P1 "published policy wipes the first-day payment+window deadline"
/// defect. OperationalDeadlineRules.RefreshAsync must keep the approved first-day exception
/// (payment time + FirstDaySelectionWindowHours) anchored to the stored PaymentVerifiedAtUtc and
/// the subscription's true first service date, across calendar reads, edits, retries and the
/// SubscriptionDayLockJob; later days and non-mutable days stay on the standard cutoff.
/// </summary>
public sealed class FirstDayDeadlineRefreshTests
{
    // Payment Thu 2026-10-08 09:00Z; first delivery Sun 2026-10-11, second Mon 2026-10-12.
    private static readonly DateOnly FirstDelivery = new(2026, 10, 11);
    private static readonly DateOnly SecondDelivery = new(2026, 10, 12);
    private static readonly DateTime PaymentVerifiedAtUtc = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FulfillmentAtUtc = new(2026, 10, 8, 9, 5, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstDayWindowEnd = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondDayStandardCutoff = new(2026, 10, 8, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Refresh_after_published_policy_preserves_first_day_payment_window()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        var days = await fx.Db.SubscriptionDays
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        Assert.Equal(2, days.Count);
        Assert.Equal(FirstDayWindowEnd, days[0].SelectionDeadlineUtc);

        await OperationalDeadlineRules.RefreshAsync(fx.Db, days, CreatePolicies(fx), allowTrial: false, default);
        await fx.Db.SaveChangesAsync();

        // The first service day keeps the payment+24h window; a published 72h lead must not
        // pull it back to the already-past 2026-10-07T21:00Z cutoff.
        var stored = await fx.Db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        Assert.Equal(FirstDayWindowEnd, stored[0].SelectionDeadlineUtc);
        // Later days stay on the standard cutoff for their own date.
        Assert.Equal(SecondDayStandardCutoff, stored[1].SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Refresh_receiving_only_the_second_day_does_not_treat_it_as_first_day()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        var secondDay = await fx.Db.SubscriptionDays
            .SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == SecondDelivery);

        await OperationalDeadlineRules.RefreshAsync(fx.Db, new[] { secondDay }, CreatePolicies(fx), allowTrial: false, default);

        // A partial batch containing only a later day must not grant it a first-day window;
        // it keeps the standard cutoff computed from its own service date.
        Assert.Equal(SecondDayStandardCutoff, secondDay.SelectionDeadlineUtc);
        Assert.NotEqual(FirstDayWindowEnd, secondDay.SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Refresh_after_fulfillment_retry_keeps_original_payment_window()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        var days = await fx.Db.SubscriptionDays
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        await OperationalDeadlineRules.RefreshAsync(fx.Db, days, CreatePolicies(fx), allowTrial: false, default);
        Assert.Equal(FirstDayWindowEnd, days[0].SelectionDeadlineUtc);

        // Webhook/duplicate fulfillment run must not restart the payment window from "now".
        var fulfillment = CreateFulfillment(fx);
        var retry = await fulfillment.TryFulfillAsync(fx.Subscription.Id);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.IsAlreadyComplete);
        Assert.Equal(PaymentVerifiedAtUtc, fx.Subscription.PaymentVerifiedAtUtc);

        var daysAfterRetry = await fx.Db.SubscriptionDays
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        await OperationalDeadlineRules.RefreshAsync(fx.Db, daysAfterRetry, CreatePolicies(fx), allowTrial: false, default);
        Assert.Equal(FirstDayWindowEnd, daysAfterRetry[0].SelectionDeadlineUtc);
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.Preparing)]
    [InlineData(SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Frozen)]
    [InlineData(SubscriptionDayStatus.Cancelled)]
    public async Task Refresh_does_not_reopen_non_mutable_days(SubscriptionDayStatus status)
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        var day = await fx.Db.SubscriptionDays
            .SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        day.Status = status;
        day.SelectionDeadlineUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        day.LockAtUtc = status == SubscriptionDayStatus.Preparing ? FulfillmentAtUtc : null;
        await fx.Db.SaveChangesAsync();

        await OperationalDeadlineRules.RefreshAsync(fx.Db, new[] { day }, CreatePolicies(fx), allowTrial: false, default);

        // Operational locks are never unwound by a refresh, and their stored deadline stays.
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), day.SelectionDeadlineUtc);
        Assert.Equal(status, day.Status);
    }

    [Fact]
    public async Task Calendar_read_and_select_keep_first_day_editable_before_its_deadline()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        // Clock is 23h after payment: before the payment+24h deadline, but AFTER the standard
        // 72h-lead cutoff the bug would stamp on refresh (2026-10-07T21:00Z).
        var engine = CreateEngine(fx, new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc));
        var calendar = await engine.GetCalendarAsync(fx.Subscription.Id, default);
        Assert.True(calendar.IsSuccess);
        var firstDay = calendar.Value.Days.Single(x => x.ServiceDate == FirstDelivery);
        Assert.True(firstDay.CanEdit);
        Assert.Null(firstDay.EditBlockReason);
        Assert.Equal(FirstDayWindowEnd, firstDay.SelectionDeadlineUtc);

        var select = await engine.SelectRestaurantAsync(fx.Subscription.Id, FirstDelivery, fx.RestaurantId, default);
        Assert.True(select.IsSuccess, select.Error?.Code ?? "select failed");

        // At exactly the deadline the day is no longer editable, and the stored deadline was
        // not moved by the reads and edits above.
        var atDeadline = CreateEngine(fx, FirstDayWindowEnd);
        var locked = await atDeadline.SelectRestaurantAsync(fx.Subscription.Id, FirstDelivery, fx.RestaurantId, default);
        Assert.False(locked.IsSuccess);
        Assert.Equal(MessageCodes.Calendar.SelectionLocked, locked.Error?.Code);

        var stored = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        Assert.Equal(FirstDayWindowEnd, stored.SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Lock_job_before_the_payment_window_leaves_the_first_day_open()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        // 2026-10-08T08:00Z: the buggy standard cutoff (2026-10-07T21:00Z) would already be
        // past and the job would send the first day; the payment window (2026-10-09T09:00Z) is not.
        var job = await RunLockJobAsync(fx, new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(0, job.Result.DueCount);

        var stored = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        Assert.Equal(SubscriptionDayStatus.Open, stored.Status);
        Assert.Null(stored.LockAtUtc);
        Assert.Equal(FirstDayWindowEnd, stored.SelectionDeadlineUtc);
    }

    [Fact]
    public async Task Lock_job_locks_the_first_day_exactly_at_the_payment_window_deadline()
    {
        var fx = await SeedFulfilledAsync();
        PublishPolicy(fx);

        var (result, _) = await RunLockJobAsync(fx, FirstDayWindowEnd);
        // Both days are due at that instant; neither fails and neither is escalated.
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.EscalatedCount);

        var stored = await fx.Db.SubscriptionDays.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        Assert.Equal(SubscriptionDayStatus.SentToRestaurant, stored.Status);
        // Locked at the payment window deadline, not at the wiped standard cutoff.
        Assert.Equal(FirstDayWindowEnd, stored.SelectionDeadlineUtc);
    }

    // ---- helpers ----

    private sealed record Fixture(
        AppDbContext Db,
        IDateTimeProvider Clock,
        Subscription Subscription,
        Guid RestaurantId);

    private static SubscriptionFulfillmentService CreateFulfillment(Fixture fx)
        => new(fx.Db, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(fx.Clock), fx.Clock);

    private static IOperationalPolicyService CreatePolicies(Fixture fx)
        => new OperationalPolicyService(fx.Db, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IAuditLogger>(), fx.Clock);

    private static void PublishPolicy(Fixture fx)
    {
        // Default settings: 72h standard lead, 24h first-day window — same values the audit
        // probe used to reproduce the wipe.
        fx.Db.SystemSettings.Add(new SystemSetting
        {
            Key = OperationalPolicyService.SettingKey,
            ValueJson = "{}",
            IsActive = true,
        });
        fx.Db.SaveChanges();
    }

    private static CustomerCalendarEngine CreateEngine(Fixture fx, DateTime nowUtc, IOperationalPolicyService? policies = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(fx.Subscription.CustomerUserId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(nowUtc));
        var dispatch = new SubscriptionDayDispatchService(
            fx.Db, clock, new CustomerDeliveryAddressService(fx.Db, clock));
        return new CustomerCalendarEngine(fx.Db, user, clock, new RestaurantDiversificationCalculator(), dispatch, policies: policies);
    }

    private static async Task<(SubscriptionDayLockResult Result, IServiceProvider Provider)> RunLockJobAsync(Fixture fx, DateTime nowUtc)
    {
        var policies = CreatePolicies(fx);
        var engine = CreateEngine(fx, nowUtc, policies);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(nowUtc));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAppDbContext)).Returns(fx.Db);
        serviceProvider.GetService(typeof(IDateTimeProvider)).Returns(clock);
        serviceProvider.GetService(typeof(IOperationalPolicyService)).Returns(policies);
        serviceProvider.GetService(typeof(CustomerCalendarEngine)).Returns(engine);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var job = new SubscriptionDayLockJob(scopeFactory, Substitute.For<ILogger<SubscriptionDayLockJob>>());
        var result = await job.ProcessDueDaysAsync(serviceProvider, CancellationToken.None);
        return (result, serviceProvider);
    }

    private static async Task<Fixture> SeedFulfilledAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        db.RestaurantProfiles.Add(new RestaurantProfile
        {
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            LegalNameAr = "شركة المطعم",
            LegalNameEn = "Restaurant Company",
        });

        var program = new Program { Code = "PROG_FDR", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_FDR", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 2,
            IncludeFriday = false,
            StartDate = FirstDelivery,
            EndDate = SecondDelivery,
            Status = SubscriptionStatus.Paid,
            PaidAmount = new Money(25m, "KWD"),
            PaymentId = "PAY-FDR",
            PaymentVerifiedAtUtc = PaymentVerifiedAtUtc,
        };
        db.AddRange(program, bundle, subscription);
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "Payzah", EventKey = "PAY-FDR", PaymentId = "PAY-FDR", SubscriptionId = subscription.Id,
            Status = "PaymentVerified", ProcessedAtUtc = PaymentVerifiedAtUtc,
        });

        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscription.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId, BundleId = subscription.BundleId, RegionId = Guid.NewGuid(), Tier = subscription.SelectedTier,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow,
        };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);

        const string slots = "[{\"slot\":\"lunch\",\"count\":1}]";
        const string meals = "[{\"mealId\":\"00000000-0000-0000-0000-000000000001\",\"nameAr\":\"وجبة\",\"nameEn\":\"Meal\",\"slot\":\"lunch\",\"sortOrder\":0,\"calories\":500,\"proteinG\":30,\"carbsG\":40,\"fatG\":15,\"presentAllergenCodes\":[],\"mayContainAllergenCodes\":[],\"dietaryPreferenceCodes\":[],\"ingredients\":[]}]";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(slots + "\n" + meals))).ToLowerInvariant();
        foreach (var date in new[] { FirstDelivery, SecondDelivery })
        {
            var versionId = Guid.NewGuid();
            db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
                ServiceDate = date, MenuPlanVersionId = versionId, SlotRequirementsJson = slots, MealsJson = meals,
                ContentHash = hash, CapturedAtUtc = DateTime.UtcNow,
            });
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = date, MenuPlanVersionId = versionId,
                Quantity = 1, Status = CapacityHoldStatus.PaymentConfirmed, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            });
        }

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FulfillmentAtUtc);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(FulfillmentAtUtc));
        var fx = new Fixture(db, clock, subscription, restaurantId);

        var fulfillment = CreateFulfillment(fx);
        var result = await fulfillment.TryFulfillAsync(subscription.Id);
        Assert.True(result.IsSuccess, result.ErrorCode ?? "fulfillment failed");
        return fx;
    }

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class NoOpCapacityLock : ICapacityConcurrencyLock
    {
        public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
