using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

/// <summary>Status classification of the unified subscription-flow coupon evaluation.</summary>
public sealed class SubscriptionFlowCouponServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid ProgramId = Guid.NewGuid();
    private static readonly Guid BundleId = Guid.NewGuid();

    [Fact]
    public async Task ValidOffer_EvaluatesEligibleWithDiscount()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, o =>
        {
            o.DiscountType = DiscountType.FixedAmount;
            o.DiscountAmount = new Money(15m, "KWD");
            o.AllowPromoCodeStacking = true;
        });
        await db.SaveChangesAsync();

        var evaluation = await EvaluateAsync(db, "SAVE15");

        evaluation.IsValid.Should().BeTrue();
        evaluation.Status.Should().Be(SubscriptionCouponStatuses.Valid);
        evaluation.OfferId.Should().Be(offer.Id);
        evaluation.DiscountAmount.Should().Be(15m);
    }

    [Fact]
    public async Task UnknownCode_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var evaluation = await EvaluateAsync(db, "MISSING");
        evaluation.Status.Should().Be(SubscriptionCouponStatuses.NotFound);
        evaluation.OfferId.Should().BeNull();
    }

    [Fact]
    public async Task InactiveOffer_ReturnsInactive()
    {
        await using var db = CreateDb();
        SeedOffer(db, o =>
        {
            o.Status = OfferStatus.Paused;
            o.AllowPromoCodeStacking = true;
        });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.Inactive);
    }

    [Fact]
    public async Task FutureOffer_ReturnsNotStarted()
    {
        await using var db = CreateDb();
        SeedOffer(db, o =>
        {
            o.StartsAtUtc = Now.AddDays(1);
            o.AllowPromoCodeStacking = true;
        });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.NotStarted);
    }

    [Fact]
    public async Task PastOffer_ReturnsExpired()
    {
        await using var db = CreateDb();
        SeedOffer(db, o => o.EndsAtUtc = Now.AddDays(-1));
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.Expired);
    }

    [Fact]
    public async Task ReachedTotalUsageLimit_ReturnsUsageExhausted()
    {
        await using var db = CreateDb();
        SeedOffer(db, o =>
        {
            o.TotalUsageLimit = 1;
            o.CurrentUsageCount = 1;
            o.AllowPromoCodeStacking = true;
        });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.UsageExhausted);
    }

    [Fact]
    public async Task ReachedPerCustomerLimit_ReturnsUsageExhausted()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, o =>
        {
            o.PerCustomerLimit = 1;
            o.AllowPromoCodeStacking = true;
        });
        db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = offer.Id, CustomerId = CustomerId, SubscriptionId = Guid.NewGuid(),
            DiscountAppliedAmount = new Money(5m, "KWD"),
            PlatformFundedAmount = new Money(5m, "KWD"),
            RestaurantFundedAmount = new Money(0m, "KWD"),
            RedeemedAtUtc = Now,
        });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.UsageExhausted);
    }

    [Fact]
    public async Task OfferDisallowingStacking_ReturnsStackingConflict()
    {
        await using var db = CreateDb();
        SeedOffer(db, o => o.AllowPromoCodeStacking = false);
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.StackingConflict);
    }

    [Fact]
    public async Task OfferScopedToAnotherProgram_ReturnsIneligible()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, o => o.AllowPromoCodeStacking = true);
        offer.EligiblePrograms.Add(new OfferProgram { OfferId = offer.Id, ProgramId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.Ineligible);
    }

    [Fact]
    public async Task CartBelowMinimum_ReturnsIneligible()
    {
        await using var db = CreateDb();
        SeedOffer(db, o =>
        {
            o.MinOrderAmount = new Money(500m, "KWD");
            o.AllowPromoCodeStacking = true;
        });
        await db.SaveChangesAsync();

        (await EvaluateAsync(db, "SAVE15")).Status.Should().Be(SubscriptionCouponStatuses.Ineligible);
    }

    [Fact]
    public async Task Evaluation_IsReadOnly()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, o => o.AllowPromoCodeStacking = true);
        await db.SaveChangesAsync();

        await EvaluateAsync(db, "SAVE15");

        (await db.OfferRedemptions.ToListAsync()).Should().BeEmpty();
        (await db.Offers.AsNoTracking().SingleAsync(x => x.Id == offer.Id)).CurrentUsageCount.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseCheckoutRedemption_RemovesHoldAndRestoresUsage()
    {
        await using var db = CreateDb();
        var offer = SeedOffer(db, o => o.AllowPromoCodeStacking = true);
        var subscriptionId = Guid.NewGuid();
        db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = offer.Id, CustomerId = CustomerId, SubscriptionId = subscriptionId,
            DiscountAppliedAmount = new Money(5m, "KWD"),
            PlatformFundedAmount = new Money(5m, "KWD"),
            RestaurantFundedAmount = new Money(0m, "KWD"),
            RedeemedAtUtc = Now,
        });
        offer.CurrentUsageCount = 1;
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.ReleaseCheckoutRedemptionAsync(subscriptionId);

        (await db.OfferRedemptions.ToListAsync()).Should().BeEmpty();
        offer.CurrentUsageCount.Should().Be(0);
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static Offer SeedOffer(AppDbContext db, Action<Offer> configure)
    {
        var offer = new Offer
        {
            NameAr = "عرض", NameEn = "Offer",
            Status = OfferStatus.Active,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.10m),
            StartsAtUtc = Now.AddDays(-1),
            EndsAtUtc = Now.AddDays(30),
            PromoCode = "SAVE15",
            PerCustomerLimit = 1,
        };
        configure(offer);
        db.Offers.Add(offer);
        return offer;
    }

    private static async Task<SubscriptionCouponEvaluation> EvaluateAsync(AppDbContext db, string code)
        => await CreateService(db).EvaluateAsync(code, CustomerId, ProgramId, BundleId,
            durationDays: 26, grossCartAmount: 200m);

    private static SubscriptionFlowCouponService CreateService(AppDbContext db)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new SubscriptionFlowCouponService(db, new OfferEligibilityEngine(), clock);
    }
}
