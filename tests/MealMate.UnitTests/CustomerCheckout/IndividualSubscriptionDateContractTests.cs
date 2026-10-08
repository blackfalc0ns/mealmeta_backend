using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MediatR;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Date contracts for the individual subscription flow. The worked example from the product
/// decision: payment happens on Thu 2026-10-08; subscription start and first delivery are on
/// Sun 2026-10-11 (Friday excluded shortens the lead from 4 to 3 days). The anchor (chosen
/// start day, Oct 8) is stored separately from the first delivery date (Oct 11) and resuming
/// a valid checkout must never re-derive Oct 11 into Oct 15.
/// </summary>
public sealed class IndividualSubscriptionDateContractTests
{
    private static readonly DateOnly PaymentDay = new(2026, 10, 8);        // Thursday — payment day only
    private static readonly DateOnly FirstDeliveryDay = new(2026, 10, 11); // Sunday — subscription start + first delivery

    [Fact]
    public void Example_payment_day_oct8_resolves_first_delivery_oct11_when_friday_excluded()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        Assert.Equal(FirstDeliveryDay, schedule.ResolveFirstDeliveryDate(PaymentDay, includeFriday: false));
        // The payment day itself is never the subscription start.
        Assert.NotEqual(PaymentDay, schedule.ResolveFirstDeliveryDate(PaymentDay, includeFriday: false));
    }

    [Fact]
    public void Example_with_friday_included_keeps_four_day_lead()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        // Window Oct 9..11 contains a Friday but Friday is included, so the full 4-day lead applies.
        Assert.Equal(new DateOnly(2026, 10, 12), schedule.ResolveFirstDeliveryDate(PaymentDay, includeFriday: true));
    }

    [Fact]
    public void Friday_window_table_october_2026_matches_single_source_calculation()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        // Wed Oct 7 chosen: window Thu 8 / Fri 9 / Sat 10 contains Friday -> +3 -> Sat Oct 10.
        Assert.Equal(new DateOnly(2026, 10, 10), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 7), includeFriday: false));
        // Tue Oct 6 chosen: +3 lands on Fri Oct 9 -> Friday skipped -> Sat Oct 10.
        Assert.Equal(new DateOnly(2026, 10, 10), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 6), includeFriday: false));
        // Mon Oct 5 chosen: window Tue..Thu has no Friday -> +4 lands on Fri Oct 9 -> skipped -> Sat Oct 10.
        Assert.Equal(new DateOnly(2026, 10, 10), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 5), includeFriday: false));
        // Mon Oct 5 with Friday included: +4 -> Fri Oct 9 stays.
        Assert.Equal(new DateOnly(2026, 10, 9), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 5), includeFriday: true));
        // Sun Oct 4 chosen: no Friday in Mon..Wed -> +4 -> Thu Oct 8 in both modes.
        Assert.Equal(new DateOnly(2026, 10, 8), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 4), includeFriday: false));
        Assert.Equal(new DateOnly(2026, 10, 8), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 10, 4), includeFriday: true));
    }

    [Fact]
    public void TryResolveCheckoutStart_uses_kuwait_midnight_not_utc()
    {
        // 2026-10-07 20:59 UTC = 2026-10-07 23:59 Kuwait -> chosen Oct 7 still allowed.
        var beforeMidnight = new SubscriptionScheduleService(ClockAt(new DateTime(2026, 10, 7, 20, 59, 0, DateTimeKind.Utc)));
        Assert.True(beforeMidnight.TryResolveCheckoutStart(new DateOnly(2026, 10, 7), false, out var wednesdayDelivery, out _));
        Assert.Equal(new DateOnly(2026, 10, 10), wednesdayDelivery);
        Assert.False(beforeMidnight.TryResolveCheckoutStart(new DateOnly(2026, 10, 6), false, out _, out var pastError));
        Assert.Equal("checkout.start_date_invalid", pastError);

        // 2026-10-07 21:00 UTC = 2026-10-08 00:00 Kuwait -> payment day Oct 8 is "today";
        // the example resolves to first delivery Oct 11, not the payment day itself.
        var atMidnight = new SubscriptionScheduleService(ClockAt(new DateTime(2026, 10, 7, 21, 0, 0, DateTimeKind.Utc)));
        Assert.False(atMidnight.TryResolveCheckoutStart(new DateOnly(2026, 10, 7), false, out _, out var dayLateError));
        Assert.Equal("checkout.start_date_invalid", dayLateError);
        Assert.True(atMidnight.TryResolveCheckoutStart(PaymentDay, false, out var firstDelivery, out _));
        Assert.Equal(FirstDeliveryDay, firstDelivery);
    }

    [Fact]
    public void Service_dates_from_first_delivery_skip_excluded_fridays_and_set_end_date_once()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        var dates = schedule.GetServiceDates(FirstDeliveryDay, 6, includeFriday: false);

        Assert.Equal(6, dates.Count);
        Assert.DoesNotContain(dates, x => x.DayOfWeek == DayOfWeek.Friday);
        Assert.Equal(FirstDeliveryDay, dates[0]);
        Assert.Equal(new DateOnly(2026, 10, 17), schedule.GetEndDate(FirstDeliveryDay, 6, includeFriday: false));
    }

    /// <summary>
    /// Regression for the P1 resume defect: a customer who paid for a checkout with first
    /// delivery Oct 11 and resumes the pending payment (valid capacity hold, no new start
    /// date) must keep Oct 11. The old path re-ran TryResolveCheckoutStart on the stored
    /// first-delivery date and silently shifted it to Oct 15.
    /// </summary>
    [Fact]
    public async Task Resume_with_valid_hold_preserves_computed_first_delivery_date()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var subscription = SeedPendingSubscription(db, customerId, firstDelivery: FirstDeliveryDay, durationDays: 6);
        SeedActiveHolds(db, subscription, expiresAtUtc: clock.UtcNow.AddMinutes(15));
        await db.SaveChangesAsync();

        var service = CreateCheckoutService(db, clock);
        var result = await service.ResumeCheckoutAsync(subscription.Id, new ResumeCheckoutRequest(), customerId);

        Assert.True(result.IsSuccess, result.Error?.Code ?? "resume failed");
        await db.Entry(subscription).ReloadAsync();
        Assert.Equal(FirstDeliveryDay, subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 17), subscription.EndDate);
        Assert.Equal(FirstDeliveryDay, result.Value.StartDate);
        Assert.Equal(FirstDeliveryDay, result.Value.ChosenStartDate);
        // Service dates still start at the preserved first delivery date.
        Assert.Equal(FirstDeliveryDay, result.Value.MenuPlanVersionsByDate![0].ServiceDate);
    }

    [Fact]
    public async Task Resume_with_explicit_new_chosen_date_derives_fresh_first_delivery_once()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var subscription = SeedPendingSubscription(db, customerId, firstDelivery: FirstDeliveryDay, durationDays: 6);
        SeedActiveHolds(db, subscription, expiresAtUtc: clock.UtcNow.AddMinutes(15));
        await db.SaveChangesAsync();

        var service = CreateCheckoutService(db, clock);
        var result = await service.ResumeCheckoutAsync(subscription.Id, new ResumeCheckoutRequest(StartDate: new DateOnly(2026, 10, 10)), customerId);

        Assert.True(result.IsSuccess, result.Error?.Code ?? "resume failed");
        await db.Entry(subscription).ReloadAsync();
        // Sat Oct 10 chosen: no Friday in the 3-day window -> +4 -> Wed Oct 14, applied once.
        Assert.Equal(new DateOnly(2026, 10, 14), subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 10), result.Value.ChosenStartDate);
        Assert.Equal(new DateOnly(2026, 10, 14), result.Value.StartDate);
    }

    [Fact]
    public async Task Resume_preview_reports_can_pay_directly_with_original_dates()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var subscription = SeedPendingSubscription(db, customerId, firstDelivery: FirstDeliveryDay, durationDays: 6);
        SeedActiveHolds(db, subscription, expiresAtUtc: clock.UtcNow.AddMinutes(15));
        await db.SaveChangesAsync();

        var service = CreateCheckoutService(db, clock);
        var preview = await service.GetResumePreviewAsync(subscription.Id, customerId);

        Assert.True(preview.IsSuccess, preview.Error?.Code ?? "ok");
        Assert.True(preview.Value.CanPayDirectly);
        Assert.Null(preview.Value.Reason);
        Assert.Equal(FirstDeliveryDay, preview.Value.StartDate);
        Assert.Equal(FirstDeliveryDay, preview.Value.CheckoutStartDate);
    }

    [Fact]
    public async Task Resume_preview_with_expired_hold_suggests_fresh_offer_without_touching_dates()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var subscription = SeedPendingSubscription(db, customerId, firstDelivery: FirstDeliveryDay, durationDays: 6);
        SeedActiveHolds(db, subscription, expiresAtUtc: clock.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();

        var service = CreateCheckoutService(db, clock);
        var preview = await service.GetResumePreviewAsync(subscription.Id, customerId);

        Assert.True(preview.IsSuccess, preview.Error?.Code ?? "ok");
        Assert.False(preview.Value.CanPayDirectly);
        Assert.Equal("capacity_hold_expired", preview.Value.Reason);
        Assert.Equal(FirstDeliveryDay, preview.Value.StartDate);
        // A fresh offer is suggested from the current work day, never stored onto the subscription.
        Assert.True(preview.Value.SuggestedStartDate > clock.TodayUtc);
    }

    [Fact]
    public async Task Resume_past_first_delivery_requires_a_new_chosen_date()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var subscription = SeedPendingSubscription(db, customerId, firstDelivery: FirstDeliveryDay, durationDays: 6);
        SeedActiveHolds(db, subscription, expiresAtUtc: clock.UtcNow.AddMinutes(15));
        await db.SaveChangesAsync();

        var service = CreateCheckoutService(db, clock);
        var result = await service.ResumeCheckoutAsync(subscription.Id, new ResumeCheckoutRequest(), customerId);

        // The stored first delivery has passed; the customer must choose a new start day
        // instead of the server silently shifting the date.
        Assert.False(result.IsSuccess);
        Assert.Equal("checkout.start_date_invalid", result.Error?.Code);
        await db.Entry(subscription).ReloadAsync();
        Assert.Equal(FirstDeliveryDay, subscription.StartDate);
    }

    /// <summary>
    /// Anchor contract for newly created subscriptions: StartDate stays the computed first
    /// delivery date (Oct 11) while the checkout anchor (chosen day, Oct 8) is persisted
    /// separately and is never re-derived for legacy rows.
    /// </summary>
    [Fact]
    public async Task CreateSubscription_persists_first_delivery_and_checkout_anchor_separately()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        await SeedPricingContext(db);
        var handler = new SubscriptionCommandHandlers(db, Substitute.For<IAuditLogger>(), clock);

        var result = await handler.Handle(new CreateSubscriptionCommand(
            Guid.NewGuid(), db.Programs.First().Id, db.Bundles.First().Id, RestaurantTier.Basic, 6,
            FirstDeliveryDay, CheckoutAnchorDate: PaymentDay), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code ?? "create failed");
        var subscription = await db.Subscriptions.SingleAsync(x => x.Id == result.Value.Id);
        Assert.Equal(FirstDeliveryDay, subscription.StartDate);
        Assert.Equal(PaymentDay, subscription.CheckoutAnchorDate);
        Assert.Equal(new DateOnly(2026, 10, 17), subscription.EndDate);
    }

    [Fact]
    public async Task CreateSubscription_without_anchor_leaves_anchor_null_for_legacy_callers()
    {
        await using var db = CreateDb();
        var clock = ClockAt(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
        await SeedPricingContext(db);
        var handler = new SubscriptionCommandHandlers(db, Substitute.For<IAuditLogger>(), clock);

        var result = await handler.Handle(new CreateSubscriptionCommand(
            Guid.NewGuid(), db.Programs.First().Id, db.Bundles.First().Id, RestaurantTier.Basic, 6,
            FirstDeliveryDay), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code ?? "create failed");
        var subscription = await db.Subscriptions.SingleAsync(x => x.Id == result.Value.Id);
        Assert.Null(subscription.CheckoutAnchorDate);
        Assert.Equal(FirstDeliveryDay, subscription.StartDate);
    }

    // ---- helpers ----

    private static IDateTimeProvider ClockAt(DateTime utcNow)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(utcNow));
        return clock;
    }

    private static IDateTimeProvider FixedClock() => ClockAt(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static Subscription SeedPendingSubscription(AppDbContext db, Guid customerId, DateOnly firstDelivery, int durationDays)
    {
        // Program/Bundle rows are required: preview/detail readers include these navigations.
        var program = new Program { Code = "PROG_RES", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_RES", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        db.AddRange(program, bundle);
        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = durationDays,
            IncludeFriday = false,
            StartDate = firstDelivery,
            EndDate = firstDelivery.AddDays(durationDays - 1),
            Status = SubscriptionStatus.PendingPayment,
            RegionId = Guid.NewGuid(),
            PaidAmount = new Money(25m, "KWD"),
        };
        db.Subscriptions.Add(subscription);
        return subscription;
    }

    private static void SeedActiveHolds(AppDbContext db, Subscription subscription, DateTime expiresAtUtc)
    {
        var restaurantId = Guid.NewGuid();
        for (var index = 0; index < subscription.DurationDays; index++)
        {
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id,
                RestaurantId = restaurantId,
                ServiceDate = subscription.StartDate.AddDays(index),
                MenuPlanVersionId = Guid.NewGuid(),
                Quantity = 1,
                Status = CapacityHoldStatus.PendingPayment,
                ExpiresAtUtc = expiresAtUtc,
            });
        }
    }

    private static CustomerCheckoutService CreateCheckoutService(AppDbContext db, IDateTimeProvider clock)
    {
        var restaurantId = Guid.NewGuid();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<RestaurantEligibilityRequest>();
                var dates = new SubscriptionScheduleService(clock).GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
                var versions = dates.Select(date => new EligibleMenuDateDto(date, Guid.NewGuid())).ToList();
                return new List<RestaurantEligibilityDto>
                {
                    new(restaurantId, "مطعم", "Restaurant", true, [], versions)
                };
            });

        var payments = Substitute.For<IPaymentGatewayService>();
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentCreationReceipt("PAY-RESUME", "https://payzah.test/hosted", "TRACK-1"));

        var pool = Substitute.For<IRestaurantPoolSnapshotService>();
        pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));

        var sender = new PricingOnlySender(new PricingEngineResult(4m, 6, 1m, 24m, 0m, 0m, 0m, 24m, 0m, 25m, 18m, 4m, 0.15m, false));

        return new CustomerCheckoutService(
            db,
            payments,
            clock,
            UserAccounts(),
            eligibility,
            new NoOpCapacityLock(),
            new InlineTransactionExecutor(),
            new SubscriptionScheduleService(clock),
            pool,
            configuration: null,
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>(),
            sender,
            Substitute.For<IRestaurantCapacityReader>());
    }

    private static IUserAccountService UserAccounts()
    {
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new UserAccountDto(callInfo.Arg<Guid>(), "c@example.com", "+96551000000", "عميل", UserType.Customer, AccountStatus.Active, null, true, true, DateTime.UtcNow, null));
        return users;
    }

    private static async Task SeedPricingContext(AppDbContext db)
    {
        var program = new Program { Code = "PROG_DATE", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_DATE", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        db.AddRange(program, bundle);
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = Guid.NewGuid(),
            ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            IsActive = true,
            RMax = Percentage.FromFraction(.3m),
            RMin = Percentage.FromFraction(.05m),
            DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m,
        });
        await db.SaveChangesAsync();
    }

    private sealed class PricingOnlySender(PricingEngineResult pricing) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PreviewCustomerPricingQuery)
                return Task.FromResult((TResponse)(object)Result<PricingEngineResult>.Success(pricing));
            return Task.FromException<TResponse>(new NotSupportedException(request.GetType().Name));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => Task.FromException(new NotSupportedException(request?.GetType().Name));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromException<object?>(new NotSupportedException(request.GetType().Name));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
