using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public class SubscriptionConfirmationComposerTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    private static (AppDbContext Db, SubscriptionsConfirmationComposer Composer) Create()
    {
        var options = CreateInMemoryOptions();
        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://app.trymealmate.com" })
            .Build();
        return (db, new SubscriptionsConfirmationComposer(db, configuration));
    }

    private static async Task<(Program Program, Bundle Bundle, Subscription Subscription)> SeedSubscriptionAsync(
        AppDbContext db, Guid customerUserId, SubscriptionStatus status = SubscriptionStatus.Paid)
    {
        var program = new Program { Code = "PROG_CONFIRM", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active };
        var bundle = new Bundle { ProgramId = program.Id, Code = "BND_CONFIRM", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 3, Status = ProgramStatus.Active };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        var subscription = new Subscription
        {
            CustomerUserId = customerUserId,
            ProgramId = program.Id,
            Program = program,
            BundleId = bundle.Id,
            Bundle = bundle,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            StartDate = new DateOnly(2026, 10, 20),
            EndDate = new DateOnly(2026, 11, 15),
            Status = status,
            PaidAmount = status is SubscriptionStatus.Paid ? new Money(38.5m, "KWD") : new Money(0m, "KWD"),
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return (program, bundle, subscription);
    }

    [Fact]
    public async Task PaidSubscription_WithAdminPromoCode_ReturnsPromoCodeDiscount()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (program, bundle, subscription) = await SeedSubscriptionAsync(db, owner);

        var promo = new PromoCode { Code = "ADMIN50", DiscountAmount = new Money(0.5m, "KWD"), IsActive = true };
        db.PromoCodes.Add(promo);
        subscription.PromoCodeId = promo.Id;
        subscription.PromoCode = promo;
        db.PromoCodeRedemptions.Add(new PromoCodeRedemption
        {
            PromoCodeId = promo.Id,
            PromoCode = promo,
            CustomerId = owner,
            SubscriptionId = subscription.Id,
            DiscountAppliedAmount = new Money(0.5m, "KWD"),
            PlatformFundedAmount = new Money(0.5m, "KWD"),
            RestaurantFundedAmount = new Money(0m, "KWD"),
            HeldAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        var redemption = await db.PromoCodeRedemptions.SingleAsync(x => x.SubscriptionId == subscription.Id);
        redemption.Commit(DateTime.UtcNow);
        await db.SaveChangesAsync();

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, owner), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var payload = result.Value;
        payload.Status.Should().Be("Paid");
        payload.IsPaid.Should().BeTrue();
        payload.IsFailed.Should().BeFalse();
        payload.AmountPaidKwd.Should().Be(38.5m);
        payload.CurrencyCode.Should().Be("KWD");
        payload.ProgramId.Should().Be(program.Id);
        payload.ProgramNameEn.Should().Be("Program");
        payload.BundleNameAr.Should().Be("باقة");
        payload.Tier.Should().Be(RestaurantTier.Platinum);
        payload.TierNameEn.Should().Be("Platinum");
        payload.PaymentVerifiedAtUtc.Should().BeNull();
        payload.Discount.Should().NotBeNull();
        payload.Discount!.Source.Should().Be("PromoCode");
        payload.Discount.PromoCodeId.Should().Be(promo.Id);
        payload.Discount.PromoCode.Should().Be("ADMIN50");
        payload.Discount.OfferId.Should().BeNull();
        payload.Discount.OfferCode.Should().BeNull();
        payload.Discount.DiscountKwd.Should().Be(0.5m);
        payload.ReferralShare.Should().BeNull();
    }

    [Fact]
    public async Task OfferRedemption_TakesPrecedence_AsOfferSource()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (_, _, subscription) = await SeedSubscriptionAsync(db, owner);

        var offer = new Offer
        {
            NameAr = "عرض",
            NameEn = "Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
            PromoCode = "OFFER-CODE-1",
        };
        db.Offers.Add(offer);
        db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = offer.Id,
            Offer = offer,
            CustomerId = owner,
            SubscriptionId = subscription.Id,
            PromoCodeId = null,
            DiscountAppliedAmount = new Money(1.25m, "KWD"),
        });
        await db.SaveChangesAsync();

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, owner), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Discount.Should().NotBeNull();
        result.Value.Discount!.Source.Should().Be("Offer");
        result.Value.Discount.OfferId.Should().Be(offer.Id);
        result.Value.Discount.OfferCode.Should().Be("OFFER-CODE-1");
        result.Value.Discount.PromoCode.Should().Be("OFFER-CODE-1");
        result.Value.Discount.DiscountKwd.Should().Be(1.25m);
    }

    [Fact]
    public async Task ReferralRewardLog_BuildsReferralSource_WithResolvedCodeId()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (program, bundle, subscription) = await SeedSubscriptionAsync(db, owner);

        var referralCode = new ReferralCode { Code = "REFER-1", OwnerUserId = Guid.NewGuid(), RewardPercent = Percentage.FromFraction(0.2m), IsActive = true };
        db.ReferralCodes.Add(referralCode);
        db.DiscountsAndReferralLogs.Add(new DiscountsAndReferralLog
        {
            SubscriptionId = subscription.Id,
            Subscription = subscription,
            DiscountType = "ReferralReward",
            Code = "REFER-1",
            Amount = new Money(1.5m, "KWD"),
            FundingMode = CampaignFundingMode.MealMateFull,
        });
        await db.SaveChangesAsync();

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, owner), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Discount.Should().NotBeNull();
        result.Value.Discount!.Source.Should().Be("Referral");
        result.Value.Discount.PromoCode.Should().Be("REFER-1");
        result.Value.Discount.OfferId.Should().Be(referralCode.Id);
        result.Value.Discount.DiscountKwd.Should().Be(1.5m);
    }

    [Fact]
    public async Task PendingPaymentSubscription_IsNotPaid_AndNoAmount()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (_, _, subscription) = await SeedSubscriptionAsync(db, owner, SubscriptionStatus.PendingPayment);

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, owner), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPaid.Should().BeFalse();
        result.Value.AmountPaidKwd.Should().BeNull();
        result.Value.Status.Should().Be("PendingPayment");
        result.Value.Discount.Should().BeNull();
    }

    [Fact]
    public async Task ForeignSubscription_ReturnsNotFound()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (_, _, subscription) = await SeedSubscriptionAsync(db, owner);

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Common.NotFound);
    }

    [Fact]
    public async Task CallerWithOwnReferralCode_GetsReferralShareMini()
    {
        var (db, composer) = Create();
        var owner = Guid.NewGuid();
        var (program, bundle, subscription) = await SeedSubscriptionAsync(db, owner);
        db.ReferralCodes.Add(new ReferralCode { Code = "MYREF-7", OwnerUserId = owner, RewardPercent = Percentage.FromFraction(0.1m), IsActive = true });
        await db.SaveChangesAsync();

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, owner), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var share = result.Value.ReferralShare;
        share.Should().NotBeNull();
        share!.Code.Should().Be("MYREF-7");
        share.RewardPercent.Should().Be(0.1m);
        share.HasRewardPolicy.Should().BeTrue();
        share.ShareUrl.Should().Be("https://app.trymealmate.com/r/MYREF-7");
    }

    [Fact]
    public async Task FamilyManager_CanReadFamilySubscription()
    {
        var (db, composer) = Create();
        var manager = Guid.NewGuid();
        var (program, bundle, subscription) = await SeedSubscriptionAsync(db, Guid.NewGuid());
        var familyGroup = new FamilyGroup
        {
            ManagerCustomerUserId = manager,
            Name = "عائلة",
            Status = FamilyGroupStatus.Active,
        };
        db.FamilyGroups.Add(familyGroup);
        subscription.FamilyGroupId = familyGroup.Id;
        await db.SaveChangesAsync();

        var result = await composer.Handle(new GetSubscriptionConfirmationQuery(subscription.Id, manager), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SubscriptionId.Should().Be(subscription.Id);
    }
}
