using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Regression tests for the P1 "canEdit=false from birth" defect. They run the REAL
/// SubscriptionFulfillmentService.TryFulfillAsync (not a hand-built calendar fixture) and then
/// the REAL CustomerCalendarEngine, asserting that payment confirmation is not a day-selection
/// confirmation and that the approved first-day payment+24h window is the stored deadline.
/// </summary>
public sealed class FulfillmentCalendarEditabilityTests
{
    // Payment day Thu 2026-10-08; subscription start / first delivery Sun 2026-10-11.
    private static readonly DateOnly FirstDelivery = new(2026, 10, 11);
    private static readonly DateOnly SecondDelivery = new(2026, 10, 12);
    private static readonly DateTime PaymentVerifiedAtUtc = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FulfillmentAtUtc = new(2026, 10, 8, 9, 5, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Fulfillment_creates_open_unconfirmed_days_that_the_calendar_allows_editing()
    {
        var fx = await SeedAsync();
        var fulfillment = CreateFulfillment(fx);

        var result = await fulfillment.TryFulfillAsync(fx.Subscription.Id);
        Assert.True(result.IsSuccess, result.ErrorCode ?? "fulfillment failed");

        var days = await fx.Db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        Assert.Equal(2, days.Count);
        Assert.All(days, day =>
        {
            Assert.Equal(SubscriptionDayStatus.Open, day.Status);
            Assert.Null(day.RestaurantId);
            Assert.Null(day.ConfirmedAtUtc);
            Assert.Null(day.LockAtUtc);
        });

        // Approved first-day exception: payment + 24h, still before first delivery.
        Assert.Equal(PaymentVerifiedAtUtc.AddHours(24), days[0].SelectionDeadlineUtc);
        // Later days keep the standard Kuwait cutoff (first delivery midnight − 72h).
        Assert.Equal(new DateTime(2026, 10, 8, 21, 0, 0, DateTimeKind.Utc), days[1].SelectionDeadlineUtc);

        var engine = CreateEngine(fx, FulfillmentAtUtc);
        var calendar = await engine.GetCalendarAsync(fx.Subscription.Id, default);
        Assert.True(calendar.IsSuccess);
        Assert.All(calendar.Value.Days, day =>
        {
            Assert.True(day.CanEdit);
            Assert.Null(day.EditBlockReason);
            Assert.True(day.LockRemainingSeconds is null or > 0);
        });

        // The editability is real: the customer can pick a restaurant on the first day right
        // after payment — waiting for the start date is never required.
        var select = await engine.SelectRestaurantAsync(fx.Subscription.Id, FirstDelivery, fx.RestaurantId, default);
        Assert.True(select.IsSuccess, select.Error?.Code ?? "select failed");
    }

    [Fact]
    public async Task Fulfillment_retry_after_customer_selection_preserves_state_and_does_not_duplicate_days()
    {
        var fx = await SeedAsync();
        var fulfillment = CreateFulfillment(fx);
        Assert.True((await fulfillment.TryFulfillAsync(fx.Subscription.Id)).IsSuccess);

        var engine = CreateEngine(fx, FulfillmentAtUtc);
        Assert.True((await engine.SelectRestaurantAsync(fx.Subscription.Id, FirstDelivery, fx.RestaurantId, default)).IsSuccess);
        var day1 = await fx.Db.SubscriptionDays.AsNoTracking().SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        Assert.Equal(fx.RestaurantId, day1.RestaurantId);

        // Webhook/checkout duplicate fulfillment run.
        var retry = await fulfillment.TryFulfillAsync(fx.Subscription.Id);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.IsAlreadyComplete);

        var dayCount = await fx.Db.SubscriptionDays.CountAsync(x => x.SubscriptionId == fx.Subscription.Id);
        Assert.Equal(2, dayCount);
        var day1After = await fx.Db.SubscriptionDays.AsNoTracking().SingleAsync(x => x.SubscriptionId == fx.Subscription.Id && x.ServiceDate == FirstDelivery);
        Assert.Equal(fx.RestaurantId, day1After.RestaurantId);
        Assert.Null(day1After.ConfirmedAtUtc);
        // The frozen day snapshot for the chosen restaurant is not replaced, and one snapshot
        // per service day still exists.
        var snapshot = await fx.Db.SubscriptionDayMealSnapshots.AsNoTracking()
            .SingleAsync(x => x.SubscriptionDayId == day1After.Id);
        Assert.Equal(fx.RestaurantId, snapshot.RestaurantId);
        Assert.Equal(2, await fx.Db.SubscriptionDayMealSnapshots.CountAsync(x => x.SubscriptionId == fx.Subscription.Id));
    }

    [Fact]
    public async Task First_day_selection_rejected_exactly_at_the_payment_window_deadline()
    {
        var fx = await SeedAsync();
        var fulfillment = CreateFulfillment(fx);
        Assert.True((await fulfillment.TryFulfillAsync(fx.Subscription.Id)).IsSuccess);

        var deadline = PaymentVerifiedAtUtc.AddHours(24);
        var before = CreateEngine(fx, deadline.AddSeconds(-1));
        var early = await before.CompleteBoxAsync(fx.Subscription.Id, FirstDelivery, idempotencyKey: null, default);
        // Box incomplete is the expected rejection this close to payment; selection-lock is not.
        Assert.Equal(MessageCodes.Calendar.BoxIncomplete, early.Error?.Code);

        var atDeadline = CreateEngine(fx, deadline);
        var locked = await atDeadline.SelectRestaurantAsync(fx.Subscription.Id, FirstDelivery, fx.RestaurantId, default);
        Assert.False(locked.IsSuccess);
        Assert.Equal(MessageCodes.Calendar.SelectionLocked, locked.Error?.Code);

        var after = CreateEngine(fx, deadline.AddMinutes(1));
        var calendar = await after.GetCalendarAsync(fx.Subscription.Id, default);
        Assert.True(calendar.IsSuccess);
        var firstDay = calendar.Value.Days.Single(x => x.ServiceDate == FirstDelivery);
        Assert.False(firstDay.CanEdit);
        Assert.Equal(CalendarEditBlockReasons.SelectionDeadlinePassed, firstDay.EditBlockReason);
    }

    [Fact]
    public async Task Late_payment_without_valid_first_day_window_keeps_standard_cutoff_and_late_path()
    {
        var fx = await SeedAsync(paymentVerifiedAtUtc: new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc));
        var fulfillment = CreateFulfillment(fx);
        var result = await fulfillment.TryFulfillAsync(fx.Subscription.Id);
        Assert.True(result.IsSuccess, result.ErrorCode ?? "fulfillment failed");

        var days = await fx.Db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == fx.Subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        // Payment+24h would land after first delivery; no alternative window is invented — the
        // standard cutoff (already past) applies and the late-payment path handles dispatch.
        Assert.Equal(new DateTime(2026, 10, 7, 21, 0, 0, DateTimeKind.Utc), days[0].SelectionDeadlineUtc);

        var engine = CreateEngine(fx, new DateTime(2026, 10, 10, 16, 0, 0, DateTimeKind.Utc));
        var calendar = await engine.GetCalendarAsync(fx.Subscription.Id, default);
        Assert.True(calendar.IsSuccess);
        var firstDay = calendar.Value.Days.Single(x => x.ServiceDate == FirstDelivery);
        Assert.False(firstDay.CanEdit);
        Assert.Equal(CalendarEditBlockReasons.SelectionDeadlinePassed, firstDay.EditBlockReason);
    }

    // ---- helpers ----

    private sealed record Fixture(
        AppDbContext Db,
        IDateTimeProvider Clock,
        Subscription Subscription,
        Guid RestaurantId);

    private static SubscriptionFulfillmentService CreateFulfillment(Fixture fx)
        => new(fx.Db, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(fx.Clock), fx.Clock);

    private static CustomerCalendarEngine CreateEngine(Fixture fx, DateTime nowUtc)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(fx.Subscription.CustomerUserId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(nowUtc);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(nowUtc));
        var dispatch = new SubscriptionDayDispatchService(
            fx.Db, clock, new MealMate.Application.Features.IdentityAccess.Customers.CustomerDeliveryAddressService(fx.Db, clock));
        return new CustomerCalendarEngine(fx.Db, user, clock, new RestaurantDiversificationCalculator(), dispatch);
    }

    private static async Task<Fixture> SeedAsync(DateTime? paymentVerifiedAtUtc = null)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var program = new Program { Code = "PROG_FUL", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_FUL", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
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
            PaymentId = "PAY-FUL",
            PaymentVerifiedAtUtc = paymentVerifiedAtUtc ?? PaymentVerifiedAtUtc,
        };
        db.AddRange(program, bundle, subscription);
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "Payzah", EventKey = "PAY-FUL", PaymentId = "PAY-FUL", SubscriptionId = subscription.Id,
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
        const string meals = "[{\"mealId\":\"00000000-0000-0000-0000-000000000001\",\"nameAr\":\"وجبة\",\"nameEn\":\"Meal\",\"slot\":\"lunch\",\"sortOrder\":0,\"presentAllergenCodes\":[],\"mayContainAllergenCodes\":[],\"dietaryPreferenceCodes\":[],\"ingredients\":[]}]";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slots + "\n" + meals))).ToLowerInvariant();
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
        return new Fixture(db, clock, subscription, restaurantId);
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
