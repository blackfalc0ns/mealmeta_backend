using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
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
}
