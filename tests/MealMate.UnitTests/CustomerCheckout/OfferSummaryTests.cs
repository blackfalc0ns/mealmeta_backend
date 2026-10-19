using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class OfferSummaryTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OfferSummaryTests_{Guid.NewGuid()}")
            .Options;
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task Summary_WithEligibleOffer_AppliesOfferDiscount_AndPassesOfferIdToPricing()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        var offer = new Offer
        {
            NameAr = "خصم العرض",
            NameEn = "Offer Discount",
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Platinum,
                    AllowedDurationDays = [26],
                    SubscriptionType = "Individual"
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                query.OfferId.Should().Be(offer.Id);
                query.CustomerId.Should().Be(customerId);
                query.CustomerSubscriptionType.Should().Be("Individual");

                return Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    MeanReferenceDailyPrice: 5m,
                    DurationDays: query.DurationDays,
                    CampaignDiscountKwd: 26m,
                    DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m,
                    GatewayFeeFixedKwd: 0m,
                    RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays,
                    FridayRatePct: 0m)));
            });

        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);

        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Platinum,
            DurationDays: 26,
            StartDate: startDate,
            OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsSuccess.Should().BeTrue();
        var summary = result.Value!;
        summary.BasePrice.Should().Be(130m);
        summary.CampaignDiscount.Should().Be(26m);
        summary.PromoDiscount.Should().Be(0m);
        summary.FinalPrice.Should().Be(104m);
        summary.CanCheckout.Should().BeTrue();
    }

    [Fact]
    public async Task Summary_WithIneligibleOfferScope_ReturnsSelectionNotEligible()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        // Offer is targeted to duration 26 days only
        var offer = new Offer
        {
            NameAr = "عرض بلاتيني",
            NameEn = "Platinum Offer",
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.15m),
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Platinum,
                    AllowedDurationDays = [26],
                    SubscriptionType = "Individual"
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);

        // Customer requests duration 6 days which is NOT targeted by this offer
        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Platinum,
            DurationDays: 6,
            StartDate: startDate,
            OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.selection_not_eligible");
    }

    [Fact]
    public async Task Summary_WithMinimumOrderAboveAuthoritativePrice_ReturnsSelectionNotEligible()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض بحد أدنى",
            NameEn = "Offer with minimum order",
            Status = OfferStatus.Active,
            MinOrderAmount = new Money(200m, "KWD"),
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                MeanReferenceDailyPrice: 5m,
                DurationDays: 26,
                CampaignDiscountKwd: 0m,
                DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m,
                GatewayFeeFixedKwd: 0m,
                RoundingStepKwd: 0.001m,
                FridayDays: 0,
                FridayRatePct: 0m))));

        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);
        var request = new SubscriptionSummaryRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, DurationDays: 26,
            StartDate: startDate, OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.selection_not_eligible");
    }

    [Fact]
    public async Task Summary_WithNoOfferEligibleRestaurantInCoverage_ReturnsSelectionNotEligible()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();
        var coveredRestaurantId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض مطعم محدد",
            NameEn = "Restaurant-scoped offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            EligibleRestaurants = [new OfferRestaurant { RestaurantId = Guid.NewGuid() }]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                MeanReferenceDailyPrice: 5m,
                DurationDays: 26,
                CampaignDiscountKwd: 0m,
                DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m,
                GatewayFeeFixedKwd: 0m,
                RoundingStepKwd: 0.001m,
                FridayDays: 0,
                FridayRatePct: 0m))));

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(customerId, program.Id, bundle.Id, RestaurantTier.Basic, 26, false,
                Arg.Any<CancellationToken>(), Arg.Any<DateOnly?>())
            .Returns(Result<SubscriptionCheckoutReadinessDto>.Success(new SubscriptionCheckoutReadinessDto(
                IsComplete: true,
                CanOpenSummary: true,
                CanCheckout: true,
                NextAction: "CHECKOUT",
                WarningAr: "",
                WarningEn: "",
                CheckoutBlockReason: null,
                Program: new SubscriptionReadinessProgramDto(program.Id, program.NameAr, program.NameEn),
                Address: new SubscriptionSummaryAddressDto(Guid.NewGuid(), "Home", "Kuwait City", "Capital", "Area 1", "1", Guid.NewGuid()),
                DeliveringRestaurantCount: 1,
                Items: [],
                Restaurants: [new SubscriptionSummaryRestaurantDto(coveredRestaurantId, "مطعم متاح", "Covered restaurant", null, RestaurantTier.Basic)])));

        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id, readiness);
        var request = new SubscriptionSummaryRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, DurationDays: 26,
            StartDate: startDate, OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.selection_not_eligible");
    }

    [Fact]
    public async Task Summary_WithExpiredOffer_ReturnsUnavailableNotFound()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        var offer = new Offer
        {
            NameAr = "عرض منتهي",
            NameEn = "Expired Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-10),
            EndsAtUtc = now.AddDays(-1)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);

        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            DurationDays: 26,
            StartDate: startDate,
            OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.customer.unavailable");
    }

    [Fact]
    public async Task Summary_WithFamilyOnlyOffer_ReturnsFamilyOnlyValidation()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        var offer = new Offer
        {
            NameAr = "عرض عائلي",
            NameEn = "Family Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Basic,
                    AllowedDurationDays = [26],
                    SubscriptionType = "Family"
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);

        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            DurationDays: 26,
            StartDate: startDate,
            OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.customer.family_only");
    }

    [Fact]
    public async Task Summary_WithOfferNotAllowingPromoStacking_SetsStackingConflict_AndZeroPromoDiscount()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        var offer = new Offer
        {
            NameAr = "عرض بدون دمج",
            NameEn = "No Stacking Offer",
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            AllowPromoCodeStacking = false
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                MeanReferenceDailyPrice: 5m,
                DurationDays: 26,
                CampaignDiscountKwd: 26m,
                DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m,
                GatewayFeeFixedKwd: 0m,
                RoundingStepKwd: 0.001m,
                FridayDays: 0,
                FridayRatePct: 0m))));

        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id);

        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            DurationDays: 26,
            StartDate: startDate,
            PromoCode: "EXTRA10",
            OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsSuccess.Should().BeTrue();
        var summary = result.Value!;
        summary.CampaignDiscount.Should().Be(26m);
        summary.PromoDiscount.Should().Be(0m);
        summary.Coupon.Should().NotBeNull();
        summary.Coupon!.Status.Should().Be(SubscriptionCouponStatuses.StackingConflict);
        summary.Coupon.DiscountAmount.Should().Be(0m);
        summary.CanCheckout.Should().BeFalse();
        summary.QuoteId.Should().BeNull();
    }

    [Fact]
    public async Task Summary_WhenProfilePrerequisitesIncomplete_ReturnsProfileIncompleteConflict()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();

        var sender = Substitute.For<ISender>();
        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(customerId, program.Id, bundle.Id, RestaurantTier.Basic, 26, false, Arg.Any<CancellationToken>(), Arg.Any<DateOnly?>())
            .Returns(Result<SubscriptionCheckoutReadinessDto>.Success(new SubscriptionCheckoutReadinessDto(
                IsComplete: false,
                CanOpenSummary: false,
                CanCheckout: false,
                NextAction: "ADDRESS",
                WarningAr: "العنوان مطلوب",
                WarningEn: "Address required",
                CheckoutBlockReason: "checkout.address_required",
                Program: new SubscriptionReadinessProgramDto(program.Id, program.NameAr, program.NameEn),
                Address: null,
                DeliveringRestaurantCount: 0,
                Items: [],
                Restaurants: [])));

        var (service, startDate) = BuildService(db, clock, sender, customerId, program.Id, bundle.Id, readiness);

        var request = new SubscriptionSummaryRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            DurationDays: 26,
            StartDate: startDate);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SubscriptionCheckoutGateCodes.ProfileIncomplete);
    }

    [Fact]
    public async Task Summary_WhenOfferQuoteCannotBePersisted_ReturnsQuoteCreationError()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض مباشر",
            NameEn = "Direct offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                MeanReferenceDailyPrice: 5m,
                DurationDays: 26,
                CampaignDiscountKwd: 0m,
                DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m,
                GatewayFeeFixedKwd: 0m,
                RoundingStepKwd: 0.001m,
                FridayDays: 0,
                FridayRatePct: 0m))));

        var quoteService = Substitute.For<IOfferCheckoutQuoteService>();
        quoteService.CreateQuoteAsync(Arg.Any<OfferCheckoutQuoteCreationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<OfferCheckoutQuote>(Error.Failure("checkout.quote_storage_unavailable")));

        var (service, startDate) = BuildService(
            db, clock, sender, customerId, program.Id, bundle.Id, quoteService: quoteService);
        var request = new SubscriptionSummaryRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, DurationDays: 26,
            StartDate: startDate, OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.quote_storage_unavailable");
    }

    [Fact]
    public async Task Summary_WhenCheckoutIsBlocked_DoesNotCreateAnOfferQuote()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var (program, bundle) = await SeedCatalogAsync(db);
        var customerId = Guid.NewGuid();
        var offer = new Offer
        {
            NameAr = "عرض مباشر",
            NameEn = "Direct offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(PricingEngine.Calculate(new PricingEngineRequest(
                MeanReferenceDailyPrice: 5m,
                DurationDays: 26,
                CampaignDiscountKwd: 0m,
                DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m,
                GatewayFeeFixedKwd: 0m,
                RoundingStepKwd: 0.001m,
                FridayDays: 0,
                FridayRatePct: 0m))));

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(customerId, program.Id, bundle.Id, RestaurantTier.Basic, 26, false,
                Arg.Any<CancellationToken>(), Arg.Any<DateOnly?>())
            .Returns(Result<SubscriptionCheckoutReadinessDto>.Success(new SubscriptionCheckoutReadinessDto(
                IsComplete: true,
                CanOpenSummary: true,
                CanCheckout: false,
                NextAction: "CHECKOUT",
                WarningAr: "لا توجد تغطية",
                WarningEn: "No coverage",
                CheckoutBlockReason: "checkout.coverage_unavailable",
                Program: new SubscriptionReadinessProgramDto(program.Id, program.NameAr, program.NameEn),
                Address: new SubscriptionSummaryAddressDto(Guid.NewGuid(), "Home", "Kuwait City", "Capital", "Area 1", "1", Guid.NewGuid()),
                DeliveringRestaurantCount: 0,
                Items: [],
                Restaurants: [])));
        var quoteService = Substitute.For<IOfferCheckoutQuoteService>();

        var (service, startDate) = BuildService(
            db, clock, sender, customerId, program.Id, bundle.Id, readiness, quoteService);
        var request = new SubscriptionSummaryRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, DurationDays: 26,
            StartDate: startDate, OfferId: offer.Id);

        var result = await service.GetOrderSummaryAsync(request, customerId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanCheckout.Should().BeFalse();
        result.Value.QuoteId.Should().BeNull();
        await quoteService.DidNotReceive().CreateQuoteAsync(
            Arg.Any<OfferCheckoutQuoteCreationRequest>(), Arg.Any<CancellationToken>());
    }

    private static (SubscriptionFlowQueryService Service, DateOnly StartDate) BuildService(
        AppDbContext db,
        IDateTimeProvider clock,
        ISender sender,
        Guid customerId,
        Guid programId,
        Guid bundleId,
        ISubscriptionCheckoutReadinessService? mockReadiness = null,
        IOfferCheckoutQuoteService? quoteService = null)
    {
        var startDate = new DateOnly(2026, 10, 25);
        var dates = Enumerable.Range(0, 26).Select(i => startDate.AddDays(i)).ToList();

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(dates);
        schedule.CountFridays(Arg.Any<IReadOnlyList<DateOnly>>()).Returns(0);

        var allowedDurations = Substitute.For<IAllowedSubscriptionDurationService>();
        allowedDurations.EvaluateAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AllowedDurationDecision(true, null, null, false, true, null, null));

        var tiers = Substitute.For<IRestaurantTierClassificationService>();
        tiers.ResolveAsync(programId, bundleId, eligibleOnly: true, Arg.Any<CancellationToken>())
            .Returns(new RestaurantTierClassificationResult(
                programId, bundleId, 5m, 0m, 5m, 5m,
                [
                    new RestaurantTierAssignment(Guid.NewGuid(), RestaurantTier.Basic, 130m, 5m, false, null),
                    new RestaurantTierAssignment(Guid.NewGuid(), RestaurantTier.Platinum, 130m, 5m, false, null)
                ]));

        var readiness = mockReadiness ?? Substitute.For<ISubscriptionCheckoutReadinessService>();
        if (mockReadiness is null)
        {
            readiness.EvaluateAsync(customerId, programId, bundleId, Arg.Any<RestaurantTier>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateOnly?>())
                .Returns(Result<SubscriptionCheckoutReadinessDto>.Success(new SubscriptionCheckoutReadinessDto(
                    IsComplete: true,
                    CanOpenSummary: true,
                    CanCheckout: true,
                    NextAction: "CHECKOUT",
                    WarningAr: "",
                    WarningEn: "",
                    CheckoutBlockReason: null,
                    Program: new SubscriptionReadinessProgramDto(programId, "Program", "Program"),
                    Address: new SubscriptionSummaryAddressDto(Guid.NewGuid(), "Home", "Kuwait City", "Capital", "Area 1", "1", Guid.NewGuid()),
                    DeliveringRestaurantCount: 1,
                    Items: [],
                    Restaurants: [])));
        }

        var slots = Substitute.For<IBundleSlotRequirementService>();
        slots.ResolveAsync(programId, bundleId, Arg.Any<CancellationToken>())
            .Returns([new SlotRequirementDto("lunch", 1), new SlotRequirementDto("dinner", 1)]);

        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        var menus = Substitute.For<IMenuPlanResolutionService>();
        var payments = Substitute.For<IPaymentGatewayService>();
        var individualGate = Substitute.For<ICustomerIndividualSubscriptionGate>();
        var diversification = Substitute.For<IRestaurantDiversificationCalculator>();

        var service = new SubscriptionFlowQueryService(
            db,
            eligibility,
            menus,
            schedule,
            slots,
            payments,
            allowedDurations,
            tiers,
            readiness,
            individualGate,
            diversification,
            sender,
            clock: clock,
            offerEngine: new OfferEligibilityEngine(),
            quoteService: quoteService);

        return (service, startDate);
    }

    private static async Task<(MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle)> SeedCatalogAsync(AppDbContext db)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG1",
            NameAr = "برنامج اللياقة",
            NameEn = "Fitness Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new Bundle
        {
            Program = program,
            Code = "BUN1",
            NameAr = "باقة الغداء والعشاء",
            NameEn = "Lunch & Dinner Bundle",
            Status = ProgramStatus.Active,
            IsActive = true,
            MealsPerDay = 2,
            LunchCount = 1,
            DinnerCount = 1
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();

        return (program, bundle);
    }
}
