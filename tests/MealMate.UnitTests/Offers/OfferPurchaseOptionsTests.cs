using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferPurchaseOptionsTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    private void SeedSystemSettings(AppDbContext db)
    {
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(SubscriptionPricingSettingsDefaults.Create()),
            IsActive = true
        });
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenOfferIsExpiredOrInactive()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var expiredOffer = new Offer
        {
            NameAr = "منتهي",
            NameEn = "Expired",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-10),
            EndsAtUtc = now.AddDays(-1)
        };
        db.Offers.Add(expiredOffer);
        await db.SaveChangesAsync();

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            Substitute.For<ISubscriptionScheduleService>(),
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ICustomerDeliveryAddressService>());

        var result = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(expiredOffer.Id, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("offers.customer.unavailable");
    }

    [Fact]
    public async Task Handle_ReturnsFamilyOnlyOffer_WithExplicitDestinationAndBlocker()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = now,
            DietaryPreferencesCompletedAtUtc = now
        });

        var familyOffer = new Offer
        {
            NameAr = "عرض عائلي",
            NameEn = "Family Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Family",
                    ProgramId = Guid.NewGuid(),
                    BundleId = Guid.NewGuid(),
                    Tier = RestaurantTier.Platinum,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(familyOffer);
        await db.SaveChangesAsync();

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            Substitute.For<ISubscriptionScheduleService>(),
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ICustomerDeliveryAddressService>());

        var result = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(familyOffer.Id, customerId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubscriptionType.Should().Be("Family");
        result.Value.Options.Should().BeEmpty();
        result.Value.FamilyFlowDestination.Should().Be($"/family-subscription?offerId={familyOffer.Id}");
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "offers.family_only");
    }

    [Fact]
    public async Task Handle_ReturnsTargetedOptions_WithExactDiscountAndNullableImages()
    {
        using var db = CreateDbContext();
        SeedSystemSettings(db);
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = now,
            DietaryPreferencesCompletedAtUtc = now
        });

        var regionId = Guid.NewGuid();
        var addressService = Substitute.For<ICustomerDeliveryAddressService>();
        addressService.ResolveActiveDeliveryAddressAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new CustomerActiveDeliveryAddress(
                Guid.NewGuid(), regionId, "Home", "Kuwait City", "Capital", "Sharq", "1"));

        // Restaurant delivering to this region
        var restaurantId = Guid.NewGuid();
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = restaurantId,
            RegionId = regionId,
            IsActive = true
        });

        var program = new Program
        {
            Code = "PROG1",
            NameAr = "برنامج الرشاقة",
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
            MealsPerDay = 2,
            LunchCount = 1,
            DinnerCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
            ImageStorageKey = null // Null image test
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            RestaurantId = restaurantId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            IsActive = true
        });

        // Add pricing rule version
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            IsActive = true,
            RMax = Percentage.FromFraction(0.25m),
            RMin = Percentage.FromFraction(0.10m),
            DShort = 6,
            DLong = 26,
            Gamma = 1.0m,
            RoundingStepKwd = 0.05m
        });

        // Add restaurant pricing for 26 days
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = restaurantId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            ReferencePrice26 = new Money(130m, "KWD"), // 5 KWD/day
            LockStatus = PriceLockStatus.Approved,
            EffectiveFromUtc = now.AddDays(-5)
        });

        var offer = new Offer
        {
            NameAr = "خصم 20%",
            NameEn = "20% Off",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Platinum,
                    AllowedDurationDays = [26]
                }
            ],
            EligibleRestaurants = [new OfferRestaurant { RestaurantId = restaurantId }]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 10, 25);
        var dates = Enumerable.Range(0, 26).Select(i => startDate.AddDays(i)).ToList();
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(startDate, 26, false).Returns(dates);
        schedule.CountFridays(dates).Returns(0);

        var eligibleRestaurant = new RestaurantEligibilityDto(restaurantId, "Restaurant", "Restaurant", true, [], []);
        var availableReport = new IndividualRestaurantAvailabilityReport(
            new IndividualRestaurantAvailabilityDto(IndividualRestaurantAvailabilityStatus.Available, 1, 1, now, startDate, null),
            [eligibleRestaurant],
            new Dictionary<Guid, RestaurantTier>(),
            regionId);
        var unavailableReport = availableReport with
        {
            Availability = new IndividualRestaurantAvailabilityDto(
                IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, 1, 0, now, startDate,
                SubscriptionCheckoutGateCodes.NoRestaurantsForSelection),
            EligibleRestaurants = []
        };
        var availability = Substitute.For<ISubscriptionRestaurantAvailabilityService>();
        availability.EvaluateDetailedAsync(customerId, program.Id, bundle.Id, RestaurantTier.Platinum, 26, false, startDate, Arg.Any<CancellationToken>())
            .Returns(availableReport);

        var pricingResult = new PricingEngineResult(5m, 26, 1m, 130m, 26m, 0m, 0m, 104m, 0m, 104m, 0m, 0m, 0m, false);
        var sender = Substitute.For<ISender>();
        var individualGate = Substitute.For<ICustomerIndividualSubscriptionGate>();
        individualGate.HasBlockingAsync(customerId, Arg.Any<CancellationToken>()).Returns(false);
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var pricingQuery = call.ArgAt<PreviewCustomerPricingQuery>(0);
                pricingQuery.ProgramId.Should().Be(program.Id);
                pricingQuery.BundleId.Should().Be(bundle.Id);
                pricingQuery.Tier.Should().Be(RestaurantTier.Platinum);
                pricingQuery.DurationDays.Should().Be(26);
                pricingQuery.OfferId.Should().Be(offer.Id);
                pricingQuery.CustomerId.Should().Be(customerId);
                pricingQuery.CustomerSubscriptionType.Should().Be("Individual");
                return Result<PricingEngineResult>.Success(pricingResult);
            });

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            schedule,
            new AllowedSubscriptionDurationService(db),
            addressService,
            individualGate: individualGate,
            availability: availability,
            sender: sender);

        var result = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Options.Should().HaveCount(1);

        var opt = result.Value.Options[0];
        opt.ProgramId.Should().Be(program.Id);
        opt.BundleId.Should().Be(bundle.Id);
        opt.ImageUrl.Should().BeNull();
        opt.MealsPerDay.Should().Be(2);
        opt.Tier.Should().Be(RestaurantTier.Platinum);
        opt.DurationDays.Should().Be(26);
        opt.BasePrice.Should().NotBeNull();
        opt.FinalPrice.Should().NotBeNull();
        opt.FinalPrice.Should().Be(104m);
        opt.GatewayFee.Should().NotBeNull();
        opt.Discount.Should().Be(opt.CampaignDiscount);
        opt.CanCheckout.Should().BeTrue();

        individualGate.HasBlockingFamilyGroupAsync(customerId, Arg.Any<CancellationToken>()).Returns(true);
        var familyBlocked = await handler.Handle(new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId), CancellationToken.None);
        familyBlocked.Value.Options[0].CanCheckout.Should().BeFalse();
        familyBlocked.Value.Options[0].FinalPrice.Should().BeNull();
        familyBlocked.Value.Options[0].BlockingReasons.Should().Contain(b => b.Code == "checkout.existing_subscription");
        individualGate.HasBlockingFamilyGroupAsync(customerId, Arg.Any<CancellationToken>()).Returns(false);

        availability.EvaluateDetailedAsync(customerId, program.Id, bundle.Id, RestaurantTier.Platinum, 26, false, startDate, Arg.Any<CancellationToken>())
            .Returns(unavailableReport);
        var blockedResult = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);

        blockedResult.IsSuccess.Should().BeTrue();
        blockedResult.Value!.Options.Should().ContainSingle();
        blockedResult.Value.Options[0].CanCheckout.Should().BeFalse();
        blockedResult.Value.Options[0].BasePrice.Should().BeNull();
        blockedResult.Value.Options[0].FinalPrice.Should().BeNull();
        blockedResult.Value.Options[0].BlockingReasons.Should().Contain(b => b.Code == MessageCodes.OfferPurchaseOptions.PeriodUnavailable);

        availability.EvaluateDetailedAsync(customerId, program.Id, bundle.Id, RestaurantTier.Platinum, 26, false, startDate, Arg.Any<CancellationToken>())
            .Returns(availableReport with
            {
                EligibleRestaurants = [eligibleRestaurant with { RestaurantId = Guid.NewGuid() }]
            });
        var outOfScopeResult = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);
        outOfScopeResult.Value!.Options[0].CanCheckout.Should().BeFalse();
        outOfScopeResult.Value.Options[0].FinalPrice.Should().BeNull();
        outOfScopeResult.Value.Options[0].BlockingReasons.Should().Contain(b => b.Code == MessageCodes.OfferPurchaseOptions.PeriodUnavailable);

        availability.EvaluateDetailedAsync(customerId, program.Id, bundle.Id, RestaurantTier.Platinum, 26, false, startDate, Arg.Any<CancellationToken>())
            .Returns(availableReport);
        offer.MinOrderAmount = new Money(500m, "KWD");
        await db.SaveChangesAsync();
        var belowMinimumResult = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);
        belowMinimumResult.Value!.Options[0].CanCheckout.Should().BeFalse();
        belowMinimumResult.Value.Options[0].FinalPrice.Should().BeNull();
        belowMinimumResult.Value.Options[0].BlockingReasons.Should().Contain(b => b.Code == "checkout.selection_not_eligible");

        offer.MinOrderAmount = null;
        offer.AllowedGovernoratesJson = "[\"Hawalli\"]";
        await db.SaveChangesAsync();
        var excludedGovernorateResult = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);
        excludedGovernorateResult.Value!.Options[0].CanCheckout.Should().BeFalse();
        excludedGovernorateResult.Value.Options[0].FinalPrice.Should().BeNull();
        excludedGovernorateResult.Value.Options[0].BlockingReasons.Should().Contain(b => b.Code == "checkout.selection_not_eligible");

        offer.AllowedGovernoratesJson = null;
        await db.SaveChangesAsync();
        pricingResult = pricingResult with { FinalCustomerPrice = 0m };
        var zeroPriceResult = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId),
            CancellationToken.None);
        var zeroPriceOption = zeroPriceResult.Value!.Options[0];
        zeroPriceOption.CanCheckout.Should().BeFalse();
        zeroPriceOption.BasePrice.Should().BeNull();
        zeroPriceOption.CampaignDiscount.Should().BeNull();
        zeroPriceOption.PromoDiscount.Should().BeNull();
        zeroPriceOption.WalletCredit.Should().BeNull();
        zeroPriceOption.GatewayFee.Should().BeNull();
        zeroPriceOption.FinalPrice.Should().BeNull();
        zeroPriceOption.BlockingReasons.Should().Contain(b => b.Code == "checkout.coupon_full_discount_unsupported");

        await sender.Received(4).Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsMissingAddressBlocker_AndNullablePrices()
    {
        using var db = CreateDbContext();
        SeedSystemSettings(db);
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = now,
            DietaryPreferencesCompletedAtUtc = now
        });

        var addressService = Substitute.For<ICustomerDeliveryAddressService>();
        addressService.ResolveActiveDeliveryAddressAsync(customerId, Arg.Any<CancellationToken>())
            .Returns((CustomerActiveDeliveryAddress?)null);

        var program = new Program { Code = "PROG1", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { Program = program, Code = "BUN1", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        var offer = new Offer
        {
            NameAr = "عرض",
            NameEn = "Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Basic,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 10, 25);
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(startDate, Arg.Any<int>(), false)
            .Returns(x => Enumerable.Range(0, (int)x[1]!).Select(i => startDate.AddDays(i)).ToList());
        schedule.GetServiceDates(startDate, 26, false).Returns(Enumerable.Range(0, 26).Select(i => startDate.AddDays(i)).ToList());

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            schedule,
            new AllowedSubscriptionDurationService(db),
            addressService);

        var result = await handler.Handle(new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Prerequisites.HasDeliveryAddress.Should().BeFalse();
        result.Value.Prerequisites.IsComplete.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "checkout.address_required");

        var opt = result.Value.Options[0];
        opt.BasePrice.Should().BeNull();
        opt.FinalPrice.Should().BeNull();
        opt.CanCheckout.Should().BeFalse();
        opt.BlockingReasons.Should().Contain(b => b.Code == "checkout.address_required");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyOptions_WithMeaningfulBlocker_WhenCatalogIsEmpty()
    {
        using var db = CreateDbContext();
        SeedSystemSettings(db);
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = now,
            DietaryPreferencesCompletedAtUtc = now
        });

        // Offer targets non-existent program and bundle
        var offer = new Offer
        {
            NameAr = "عرض مفقود",
            NameEn = "Missing Catalog Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = Guid.NewGuid(),
                    BundleId = Guid.NewGuid(),
                    Tier = RestaurantTier.Basic,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 10, 25);
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            schedule,
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ICustomerDeliveryAddressService>());

        var result = await handler.Handle(new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Options.Should().BeEmpty();
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "offers.no_eligible_packages");
    }

    [Fact]
    public async Task Handle_DoesNotInventPresetDurations_WhenNoDurationIsConfigured()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        var program = new Program { Code = "PROG1", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            Program = program,
            Code = "BUN1",
            NameAr = "باقة",
            NameEn = "Bundle",
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        var offer = new Offer
        {
            NameAr = "عرض",
            NameEn = "Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Basic
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var durations = Substitute.For<IAllowedSubscriptionDurationService>();
        durations.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new SubscriptionPricingSettingsDocument
        {
            Durations = []
        });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 10, 25);
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(startDate, Arg.Any<int>(), false)
            .Returns(x => Enumerable.Range(0, (int)x[1]!).Select(i => startDate.AddDays(i)).ToList());

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            schedule,
            durations,
            Substitute.For<ICustomerDeliveryAddressService>());

        var result = await handler.Handle(new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Options.Should().BeEmpty();
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "offers.no_eligible_packages");
    }

    [Fact]
    public async Task Handle_DoesNotExposePackage_WhenPersistedMealSlotsAreEmpty()
    {
        using var db = CreateDbContext();
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var customerId = Guid.NewGuid();
        var program = new Program { Code = "PROG1", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { Program = program, Code = "BUN1", NameAr = "باقة", NameEn = "Bundle", Status = ProgramStatus.Active, IsActive = true };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        var offer = new Offer
        {
            NameAr = "عرض",
            NameEn = "Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Basic,
                    AllowedDurationDays = [26]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 10, 25);
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly?>(), Arg.Any<bool>(), out Arg.Any<DateOnly>(), out Arg.Any<string?>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(startDate, 26, false).Returns(Enumerable.Range(0, 26).Select(i => startDate.AddDays(i)).ToList());

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            schedule,
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ICustomerDeliveryAddressService>());

        var result = await handler.Handle(new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Options.Should().BeEmpty();
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "offers.no_eligible_packages");
    }

    [Fact]
    public async Task Handle_UsesScheduleDefaultForMissingStartDate_AndRespectsFridayChoice()
    {
        using var db = CreateDbContext();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc));
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "PROG1", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            Program = program,
            Code = "BUN1",
            NameAr = "باقة",
            NameEn = "Bundle",
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        var offer = new Offer
        {
            NameAr = "عرض",
            NameEn = "Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = clock.UtcNow.AddDays(-1),
            EndsAtUtc = clock.UtcNow.AddDays(5),
            PackageTargets =
            [
                new OfferPackageTarget
                {
                    SubscriptionType = "Individual",
                    ProgramId = program.Id,
                    BundleId = bundle.Id,
                    Tier = RestaurantTier.Basic,
                    AllowedDurationDays = [6]
                }
            ]
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var handler = new GetCustomerOfferPurchaseOptionsQueryHandler(
            db,
            new OfferEligibilityEngine(),
            clock,
            new SubscriptionScheduleService(clock),
            new AllowedSubscriptionDurationService(db),
            Substitute.For<ICustomerDeliveryAddressService>());

        var withFriday = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId, IncludeFriday: true),
            CancellationToken.None);
        var withoutFriday = await handler.Handle(
            new GetCustomerOfferPurchaseOptionsQuery(offer.Id, customerId, IncludeFriday: false),
            CancellationToken.None);

        withFriday.IsSuccess.Should().BeTrue();
        withoutFriday.IsSuccess.Should().BeTrue();
        withFriday.Value!.Options.Should().ContainSingle();
        withoutFriday.Value!.Options.Should().ContainSingle();
        withFriday.Value.Options[0].StartDate.Should().Be(new DateOnly(2026, 10, 23));
        withFriday.Value.Options[0].IncludeFriday.Should().BeTrue();
        withoutFriday.Value.Options[0].StartDate.Should().Be(new DateOnly(2026, 10, 24));
        withoutFriday.Value.Options[0].IncludeFriday.Should().BeFalse();
    }
}
