using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MealMate.ApiTests.CustomerCheckout;

/// <summary>
/// Checkout-cycle contract for coupons: a rejected code aborts before any subscription or payment,
/// a valid code reprices the server-side amount and records a hold redemption, and the hold is
/// released when the payment cannot start. Resume re-validates the existing hold without
/// creating a second redemption.
/// </summary>
public sealed class CheckoutCouponFlowTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FixedStart = new(2026, 10, 2);

    [Fact]
    public async Task StartCheckout_RejectedCoupon_FailsBeforeSubscriptionOrPayment()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, code: "OLD10", discountKwd: 10m);
        offer.EndsAtUtc = FixedNow.AddDays(-1);
        var catalog = SeedCheckoutCatalog(db);
        await db.SaveChangesAsync();

        var (checkout, payments, sender, _) = CreateCheckout(db, catalog);
        var request = StartRequest(catalog, promoCode: "OLD10");

        var result = await checkout.StartCheckoutAsync(request, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.coupon_rejected", result.Error.Code);
        Assert.Empty(await db.Subscriptions.ToListAsync());
        Assert.Empty(await db.OfferRedemptions.ToListAsync());
        await payments.DidNotReceive().ExecutePaymentAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await sender.DidNotReceive().Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCheckout_ValidCoupon_DiscountsPriceAndRecordsCouponHold()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, code: "WELCOME10", discountKwd: 10m);
        var catalog = SeedCheckoutCatalog(db);
        await db.SaveChangesAsync();
        var customerId = Guid.NewGuid();

        var (checkout, payments, sender, _) = CreateCheckout(db, catalog);
        var request = StartRequest(catalog, promoCode: "welcome10");

        var result = await checkout.StartCheckoutAsync(request, customerId);

        Assert.True(result.IsSuccess);
        // The charged amount comes from the real creation handler: the same pricing pipeline as
        // the preview, with the validated coupon discount included.
        var expectedDiscountedPrice = ExpectedCheckoutPrice(catalog, couponDiscountKwd: 10m);
        var expectedUndiscountedPrice = ExpectedCheckoutPrice(catalog, couponDiscountKwd: 0m);
        Assert.True(expectedDiscountedPrice < expectedUndiscountedPrice);
        Assert.Equal(expectedDiscountedPrice, result.Value.FinalPrice);

        var command = sender.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<CreateSubscriptionCommand>()
            .Single();
        Assert.Equal(offer.Id, command.CouponOfferId);
        Assert.Equal(10m, command.CouponDiscountKwd);
        await payments.Received(1).ExecutePaymentAsync(expectedDiscountedPrice,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var redemption = Assert.Single(await db.OfferRedemptions.ToListAsync());
        Assert.Equal(offer.Id, redemption.OfferId);
        Assert.Equal(result.Value.SubscriptionId, redemption.SubscriptionId);
        Assert.Equal(customerId, redemption.CustomerId);
        Assert.Equal(10m, redemption.DiscountAppliedAmount.Amount);
        Assert.Equal(1, (await db.Offers.SingleAsync(x => x.Id == offer.Id)).CurrentUsageCount);
    }

    [Fact]
    public async Task StartCheckout_ZeroPriceAfterDiscount_IsRejectedWithClearError()
    {
        await using var db = CreateDb();
        SeedOffer(db, code: "FREE100", discountKwd: 500m);
        var catalog = SeedCheckoutCatalog(db);
        await db.SaveChangesAsync();

        var (checkout, payments, _, _) = CreateCheckout(db, catalog, promoDiscountKwd: 500m);
        var request = StartRequest(catalog, promoCode: "FREE100");

        var result = await checkout.StartCheckoutAsync(request, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.coupon_full_discount_unsupported", result.Error.Code);
        Assert.Empty(await db.Subscriptions.ToListAsync());
        Assert.Empty(await db.OfferRedemptions.ToListAsync());
    }

    [Fact]
    public async Task StartCheckout_PaymentInitializationFailure_ReleasesCouponHold()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, code: "WELCOME10", discountKwd: 10m);
        var catalog = SeedCheckoutCatalog(db);
        await db.SaveChangesAsync();
        var customerId = Guid.NewGuid();

        var (checkout, payments, _, _) = CreateCheckout(db, catalog);
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("gateway down"));
        var request = StartRequest(catalog, promoCode: "WELCOME10");

        var result = await checkout.StartCheckoutAsync(request, customerId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.payment_init_failed", result.Error.Code);
        Assert.Empty(await db.OfferRedemptions.ToListAsync());
        Assert.Equal(0, (await db.Offers.SingleAsync(x => x.Id == offer.Id)).CurrentUsageCount);
        Assert.Equal(SubscriptionStatus.PaymentFailed,
            await db.Subscriptions.Where(x => x.CustomerUserId == customerId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ResumeCheckout_RevalidatesExistingCouponWithoutCreatingNewRedemption()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, code: "WELCOME10", discountKwd: 10m);
        var catalog = SeedCheckoutCatalog(db);
        var customerId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = customerId, ProgramId = catalog.ProgramId, BundleId = catalog.BundleId,
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, Status = SubscriptionStatus.PendingPayment,
            StartDate = FixedStart, EndDate = FixedStart.AddDays(25), PaidAmount = new Money(224m, "KWD"),
            RegionId = Guid.NewGuid(),
        };
        db.Subscriptions.Add(subscription);
        db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = offer.Id, CustomerId = customerId, SubscriptionId = subscription.Id,
            DiscountAppliedAmount = new Money(10m, "KWD"),
            PlatformFundedAmount = new Money(10m, "KWD"),
            RestaurantFundedAmount = new Money(0m, "KWD"),
            RedeemedAtUtc = FixedNow,
        });
        offer.CurrentUsageCount = 1;
        await db.SaveChangesAsync();

        var (checkout, payments, _, _) = CreateCheckout(db, catalog);
        var request = new ResumeCheckoutRequest(StartDate: FixedStart, CallbackUrl: "https://localhost/checkout/callback", ErrorUrl: "https://localhost/checkout/error");

        var result = await checkout.ResumeCheckoutAsync(subscription.Id, request, customerId);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(224m, result.Value.FinalPrice);
        Assert.Single(await db.OfferRedemptions.ToListAsync());
        Assert.Equal(1, (await db.Offers.SingleAsync(x => x.Id == offer.Id)).CurrentUsageCount);
        await payments.Received(1).ExecutePaymentAsync(224m,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeCheckout_WhenCouponNoLongerValid_RejectsBeforePayment()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, code: "WELCOME10", discountKwd: 10m);
        var catalog = SeedCheckoutCatalog(db);
        var customerId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = customerId, ProgramId = catalog.ProgramId, BundleId = catalog.BundleId,
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, Status = SubscriptionStatus.PendingPayment,
            StartDate = FixedStart, EndDate = FixedStart.AddDays(25), PaidAmount = new Money(224m, "KWD"),
            RegionId = Guid.NewGuid(),
        };
        db.Subscriptions.Add(subscription);
        db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = offer.Id, CustomerId = customerId, SubscriptionId = subscription.Id,
            DiscountAppliedAmount = new Money(10m, "KWD"),
            PlatformFundedAmount = new Money(10m, "KWD"),
            RestaurantFundedAmount = new Money(0m, "KWD"),
            RedeemedAtUtc = FixedNow,
        });
        offer.EndsAtUtc = FixedNow.AddDays(-1);
        await db.SaveChangesAsync();

        var (checkout, payments, _, _) = CreateCheckout(db, catalog);
        var request = new ResumeCheckoutRequest(StartDate: FixedStart, CallbackUrl: "https://localhost/checkout/callback", ErrorUrl: "https://localhost/checkout/error");

        var result = await checkout.ResumeCheckoutAsync(subscription.Id, request, customerId);

        Assert.True(result.IsFailure);
        Assert.Equal("checkout.coupon_no_longer_valid", result.Error.Code);
        await payments.DidNotReceive().ExecutePaymentAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- helpers ----

    private sealed record CheckoutCatalog(Guid ProgramId, Guid BundleId, Guid RestaurantId);

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static Offer SeedOffer(AppDbContext db, string code, decimal discountKwd)
    {
        var offer = new Offer
        {
            NameAr = "عرض", NameEn = "Offer",
            Status = OfferStatus.Active,
            DiscountType = DiscountType.FixedAmount,
            DiscountAmount = new Money(discountKwd, "KWD"),
            StartsAtUtc = FixedNow.AddDays(-1),
            EndsAtUtc = FixedNow.AddDays(30),
            PromoCode = code,
            PerCustomerLimit = 1,
            // A promo-code offer must allow stacking to pass the eligibility engine's
            // promo-code rule — same requirement as the existing pricing preview contract.
            AllowPromoCodeStacking = true,
        };
        db.Offers.Add(offer);
        return offer;
    }

    private static CheckoutCatalog SeedCheckoutCatalog(AppDbContext db)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_COUPON_CHECKOUT", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_COUPON_CHECKOUT", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(104m, "KWD"),
            ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = FixedNow.AddDays(-1),
        });
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromPercent(15m),
            RMin = Percentage.FromPercent(5m),
            DShort = 6,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = 0.25m,
            IsActive = true,
        });
        return new CheckoutCatalog(program.Id, bundle.Id, restaurant.Id);
    }

    private static StartCheckoutRequest StartRequest(CheckoutCatalog catalog, string promoCode)
        => new(catalog.ProgramId, catalog.BundleId, RestaurantTier.Basic, 26,
            "https://localhost/checkout/callback", "https://localhost/checkout/error",
            FixedStart, PromoCode: promoCode);

    /// <summary>The checkout amount the real creation handler computes for the seeded catalog.</summary>
    private static decimal ExpectedCheckoutPrice(CheckoutCatalog catalog, decimal couponDiscountKwd)
    {
        var meanDaily = SubscriptionPackagePricingCalculator.ResolveAverageDailyPrice(
            [104m], RestaurantTier.Basic);
        var settings = SubscriptionPricingSettingsDefaults.Create();
        return PricingEngine.Calculate(new PricingEngineRequest(
            meanDaily, 26,
            CampaignDiscountKwd: couponDiscountKwd,
            DurationRatesPct: new Dictionary<int, decimal> { [26] = settings.Global.ResolveRate(26) },
            FridayDays: 0, FridayRatePct: settings.Global.FridayRate)).FinalCustomerPrice;
    }

    private static (CustomerCheckoutService Checkout, IMyFatoorahPaymentService Payments, ISender Sender, IDateTimeProvider Clock)
        CreateCheckout(AppDbContext db, CheckoutCatalog catalog, decimal promoDiscountKwd = 10m)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        var schedule = new SubscriptionScheduleService(clock);
        var serviceDates = schedule.GetServiceDates(FixedStart, 26, false);

        // The real subscription-creation handler so the discounted PaidAmount and snapshot
        // are produced by production code, not by a test stub.
        var commandHandler = new SubscriptionCommandHandlers(db, Substitute.For<IAuditLogger>(), clock, schedule: schedule);

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("https://localhost/pay?PaymentId=PAY-123");

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RestaurantTier>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SubscriptionCheckoutReadinessDto(
                true, true, true, "", "", "", null,
                new SubscriptionReadinessProgramDto(catalog.ProgramId, "برنامج", "Program"),
                new SubscriptionSummaryAddressDto(Guid.NewGuid(), "المنزل", "السالمية", null, null, null, Guid.NewGuid()),
                1, [], [new SubscriptionSummaryRestaurantDto(catalog.RestaurantId, "مطعم", "Restaurant", null, RestaurantTier.Basic)])));

        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns([
                new RestaurantEligibilityDto(catalog.RestaurantId, "مطعم", "Restaurant", true, [],
                    [new EligibleMenuDateDto(serviceDates[0], Guid.NewGuid())])
            ]);

        var capacityReader = Substitute.For<IRestaurantCapacityReader>();
        capacityReader.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<(Guid, DateOnly)>>(0)
                .ToDictionary(k => k, k => new RestaurantCapacityUnits(k.Item1, k.Item2, 10, 0, 0, 0, 0, 0, 0, 0, 0)));

        var pool = Substitute.For<IRestaurantPoolSnapshotService>();
        pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1, null));

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                var hasPromo = !string.IsNullOrWhiteSpace(query.PromoCode);
                return Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    9m, query.DurationDays,
                    CampaignDiscountKwd: hasPromo ? promoDiscountKwd : 0m,
                    DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays, FridayRatePct: 0m)));
            });
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => commandHandler.Handle(call.ArgAt<CreateSubscriptionCommand>(0), Arg.Any<CancellationToken>()));

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(Guid.NewGuid(), "customer@example.com", "50000000", "Customer",
                UserType.Customer, AccountStatus.Active, null, true, true, FixedNow, null));

        var allowedDurations = Substitute.For<IAllowedSubscriptionDurationService>();
        allowedDurations.EvaluateAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AllowedDurationDecision(true, null, null, false, true, null, null));

        var checkout = new CustomerCheckoutService(
            db, payments, clock, users,
            eligibility, Substitute.For<ICapacityConcurrencyLock>(), new InlineTransaction(),
            schedule, pool, new ConfigurationBuilder().Build(), allowedDurations,
            Substitute.For<ISubscriptionPaymentProcessor>(), readiness, sender,
            capacityReader, sandbox: null);
        return (checkout, payments, sender, clock);
    }

    private sealed class InlineTransaction : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }
}
