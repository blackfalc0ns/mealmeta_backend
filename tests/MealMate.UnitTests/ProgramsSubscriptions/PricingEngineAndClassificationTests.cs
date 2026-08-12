using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class PricingEngineAndClassificationTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Theory]
    [InlineData(6, 0.15)]
    [InlineData(26, 0.05)]
    public void PricingEngine_CalculatesDurationMultiplier_CorrectlyForStandardDurations(int durationDays, decimal expectedR)
    {
        var req = new PricingEngineRequest(
            MeanReferenceDailyPrice: 3.000m,
            DurationDays: durationDays);

        var result = PricingEngine.Calculate(req);

        result.DurationMultiplierR.Should().Be(expectedR);
        result.FinalCustomerPrice.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void PricingEngine_AppliesDiscountsInCorrectSequence()
    {
        var req = new PricingEngineRequest(
            MeanReferenceDailyPrice: 4.000m,
            DurationDays: 26,
            CampaignDiscountKwd: 10.000m,
            PromoCodeDiscountKwd: 5.000m,
            WalletPromoCreditKwd: 2.000m);

        var result = PricingEngine.Calculate(req);

        result.CampaignDiscountApplied.Should().Be(10.000m);
        result.PromoCodeDiscountApplied.Should().Be(5.000m);
        result.WalletCreditApplied.Should().Be(2.000m);
        result.FinalCustomerPrice.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void PricingEngine_UsesConfiguredDurationRates_AndAddsFridayDays()
    {
        var rates = new Dictionary<int, decimal> { [6] = 30m, [12] = 22m, [26] = 15m };
        var sixDays = PricingEngine.Calculate(new PricingEngineRequest(3m, 6, DurationRatesPct: rates));
        var twentySixDays = PricingEngine.Calculate(new PricingEngineRequest(3m, 26, DurationRatesPct: rates));
        var withFriday = PricingEngine.Calculate(new PricingEngineRequest(3m, 6, DurationRatesPct: rates, FridayDays: 2));

        sixDays.DurationMultiplierR.Should().Be(0.30m);
        twentySixDays.DurationMultiplierR.Should().Be(0.15m);
        withFriday.FinalCustomerPrice.Should().BeGreaterThan(sixDays.FinalCustomerPrice);
        withFriday.DurationDays.Should().Be(8);
    }

    [Fact]
    public async Task RunClassification_CalculatesMeanStdDev_AndAssignsTiersAndOutliers()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var progId = Guid.NewGuid();
        var bndId = Guid.NewGuid();

        // 3 restaurants with different reference prices
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = Guid.NewGuid(),
            ProgramId = progId,
            BundleId = bndId,
            ReferencePrice26 = new Money(52.000m, "KWD"), // 2.0 KWD / day -> Basic
            ContractedSupplyPrice26 = new Money(40.000m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow,
        });

        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = Guid.NewGuid(),
            ProgramId = progId,
            BundleId = bndId,
            ReferencePrice26 = new Money(78.000m, "KWD"), // 3.0 KWD / day -> Platinum
            ContractedSupplyPrice26 = new Money(60.000m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow,
        });

        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = Guid.NewGuid(),
            ProgramId = progId,
            BundleId = bndId,
            ReferencePrice26 = new Money(104.000m, "KWD"), // 4.0 KWD / day -> Elite
            ContractedSupplyPrice26 = new Money(80.000m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new PricingAndClassificationHandlers(db, audit, clock);
        var command = new RunClassificationCommand(progId, bndId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MeanReferenceDaily.Should().Be(3.000m);
        result.Value.Classifications.Should().HaveCount(3);

        var basic = result.Value.Classifications.First(x => x.ReferenceDailyBox == 2.0m);
        basic.Tier.Should().Be(RestaurantTier.Basic);

        var elite = result.Value.Classifications.First(x => x.ReferenceDailyBox == 4.0m);
        elite.Tier.Should().Be(RestaurantTier.Elite);
    }

    [Fact]
    public async Task SetRestaurantPricing_RejectsPriceAboveTheAdminCeiling()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.Global.MaxRestaurantBoxPrice26 = 80m;
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(settings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var handler = new PricingAndClassificationHandlers(db, audit, clock);

        var result = await handler.Handle(new SetRestaurantPricingCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 80.001m, 60m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("restaurant_pricing.maximum_box_price_exceeded");
    }

    [Fact]
    public async Task GetSubscriptionBundleTierPricings_CalculatesCustomerPricesInBackend()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var program = new Program
        {
            Code = "TEST-PROGRAM",
            NameAr = "برنامج اختبار",
            NameEn = "Test program",
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "TEST-BUNDLE",
            NameAr = "باقة اختبار",
            NameEn = "Test bundle",
            MealsPerDay = 2,
            IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        foreach (var price in new[] { 100m, 130m, 160m })
        {
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                RestaurantId = Guid.NewGuid(),
                ProgramId = program.Id,
                Program = program,
                BundleId = bundle.Id,
                Bundle = bundle,
                ReferencePrice26 = new Money(price, "KWD"),
                ContractedSupplyPrice26 = new Money(price * 0.78m, "KWD"),
                LockStatus = PriceLockStatus.Approved,
                EffectiveFromUtc = DateTime.UtcNow,
            });
        }

        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = Guid.NewGuid(),
            ProgramId = program.Id,
            Program = program,
            BundleId = bundle.Id,
            Bundle = bundle,
            ReferencePrice26 = new Money(1000m, "KWD"),
            ContractedSupplyPrice26 = new Money(780m, "KWD"),
            LockStatus = PriceLockStatus.Approved,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-2),
            EffectiveToUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = Guid.NewGuid(),
            ProgramId = program.Id,
            Program = program,
            BundleId = bundle.Id,
            Bundle = bundle,
            ReferencePrice26 = new Money(130m, "KWD"),
            ContractedSupplyPrice26 = new Money(101.4m, "KWD"),
            LockStatus = PriceLockStatus.Draft,
            EffectiveFromUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var handler = new PricingAndClassificationHandlers(
            db,
            Substitute.For<IAuditLogger>(),
            Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(
            new GetSubscriptionBundleTierPricingsQuery(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Sum(x => x.RestaurantCount).Should().Be(3);
        result.Value.Should().OnlyContain(x => x.CustomerPrice6DaysKwd > 0m);
        result.Value.Should().OnlyContain(x => x.Commission6Percent == 30m);
        result.Value.Should().OnlyContain(x => x.Commission12Percent == 22m);
        result.Value.Should().OnlyContain(x => x.Commission26Percent == 15m);

        var tierPricing = result.Value.First();
        var preview = await handler.Handle(
            new PreviewCustomerPricingQuery(
                tierPricing.ProgramId,
                tierPricing.BundleId,
                tierPricing.Tier,
                12),
            CancellationToken.None);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.FinalCustomerPrice.Should().Be(tierPricing.CustomerPrice12DaysKwd);

        var catalog = await handler.Handle(
            new GetCustomerSubscriptionPricingCatalogQuery(),
            CancellationToken.None);

        catalog.IsSuccess.Should().BeTrue();
        catalog.Value.Should().HaveCount(3);
        catalog.Value.Should().OnlyContain(item => item.CurrencyCode == "KWD");
        catalog.Value.Should().OnlyContain(item => item.Prices.Count == 3);
        catalog.Value
            .Single(item => item.Tier == tierPricing.Tier)
            .Prices.Single(price => price.DurationDays == 12)
            .TotalPriceKwd.Should().Be(preview.Value.FinalCustomerPrice);
    }

    [Theory]
    [InlineData(CampaignFundingMode.MealMateFull, 0.840, 0.000, 6.000, 0.600, 5.400)]
    [InlineData(CampaignFundingMode.RestaurantFull, 0.000, 0.840, 5.067, 0.507, 4.560)]
    [InlineData(CampaignFundingMode.Shared, 0.420, 0.420, 5.533, 0.553, 4.980)]
    public void CampaignBoxEconomics_MatchesAmendment01Examples(
        CampaignFundingMode mode,
        decimal expectedMealMateFunding,
        decimal expectedRestaurantFunding,
        decimal expectedSupplyGross,
        decimal expectedCommission,
        decimal expectedRestaurantNet)
    {
        var result = CampaignBoxEconomicsCalculator.Calculate(new CampaignBoxEconomicsRequest(
            CustomerPriceBeforeDiscount: 8.400m,
            DiscountRate: 0.10m,
            ContractedSupplyDailyGross: 6.000m,
            CommissionRate: 0.10m,
            FundingMode: mode,
            RestaurantShareRate: 0.5m));

        result.CustomerDiscount.Should().Be(0.840m);
        result.CustomerPriceAfterDiscount.Should().Be(7.560m);
        result.MealMateFundingNet.Should().Be(expectedMealMateFunding);
        result.RestaurantFundingNet.Should().Be(expectedRestaurantFunding);
        result.BaseRestaurantNet.Should().Be(5.400m);
        result.CampaignRestaurantNet.Should().Be(expectedRestaurantNet);
        result.CampaignCommission.Should().Be(expectedCommission);

        if (mode == CampaignFundingMode.MealMateFull)
        {
            result.CampaignSupplyGross.Should().BeNull();
            result.BaseSupplyGross.Should().Be(expectedSupplyGross);
        }
        else
        {
            result.CampaignSupplyGross.Should().Be(expectedSupplyGross);
        }
    }

    [Fact]
    public async Task SetRestaurantPricing_OnLockedSupply_OpensChangeRequestWithoutEndingActive()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var restaurantId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var locked = new RestaurantBundlePricing
        {
            RestaurantId = restaurantId,
            ProgramId = programId,
            BundleId = bundleId,
            ReferencePrice26 = new Money(182m, "KWD"),
            ContractedSupplyPrice26 = new Money(156m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-10),
        };
        db.RestaurantBundlePricings.Add(locked);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var handler = new PricingAndClassificationHandlers(db, Substitute.For<IAuditLogger>(), clock);

        var result = await handler.Handle(new SetRestaurantPricingCommand(
            restaurantId, programId, bundleId, 190m, 160m, "Need new supply terms"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LockStatus.Should().Be(PriceLockStatus.ChangeRequested);
        result.Value.ContractedSupplyPrice26.Should().Be(160m);

        var activeStillOpen = await db.RestaurantBundlePricings.SingleAsync(x => x.Id == locked.Id);
        activeStillOpen.EffectiveToUtc.Should().BeNull();
        activeStillOpen.LockStatus.Should().Be(PriceLockStatus.Locked);
        activeStillOpen.ContractedSupplyPrice26.Amount.Should().Be(156m);

        var pending = await db.RestaurantBundlePricings.SingleAsync(x => x.Id == result.Value.Id);
        pending.PreviousPricingId.Should().Be(locked.Id);
    }

    [Fact]
    public void RestaurantReferencePriceGuard_RejectsWhenAboveIqrFence()
    {
        // Four peer 26-day prices clustered around 130–160; inflated 400 must fail.
        var peers = new[] { 130m, 140m, 150m, 160m };
        var auto = RestaurantReferencePriceGuard.Evaluate(peers);

        auto.CanEnforce.Should().BeTrue();
        auto.MaxReferencePrice26.Should().HaveValue();
        auto.MaxReferencePrice26!.Value.Should().BeLessThan(400m);
        auto.Method.Should().Contain("iqr");

        var ceiling = RestaurantReferencePriceGuard.ResolveEffectiveCeiling(0m, auto);
        ceiling.Should().Be(auto.MaxReferencePrice26);
    }

    [Fact]
    public void RestaurantReferencePriceGuard_SkipsWhenSampleTooSmall()
    {
        var auto = RestaurantReferencePriceGuard.Evaluate(new[] { 100m, 110m, 120m });
        auto.CanEnforce.Should().BeFalse();
        RestaurantReferencePriceGuard.ResolveEffectiveCeiling(0m, auto).Should().BeNull();
    }

    [Fact]
    public void RestaurantReferencePriceGuard_ManualCeilingTakesLowerBoundWithAuto()
    {
        var peers = new[] { 100m, 110m, 120m, 130m, 140m };
        var auto = RestaurantReferencePriceGuard.Evaluate(peers);
        auto.CanEnforce.Should().BeTrue();

        var tightManual = 50m;
        var effective = RestaurantReferencePriceGuard.ResolveEffectiveCeiling(tightManual, auto);
        effective.Should().Be(50m);
    }
}
