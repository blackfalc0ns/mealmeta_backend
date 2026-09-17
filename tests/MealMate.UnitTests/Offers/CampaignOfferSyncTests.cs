using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.EventHandlers;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.CollaborativeCampaigns.Events;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class CampaignOfferSyncTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task SyncCampaignToOfferAsync_CreatesNewOffer_MatchingCampaignProperties()
    {
        using var db = CreateDbContext();
        var syncService = new CampaignOfferSyncService(db);

        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        var campaign = new Campaign
        {
            NameAr = "حملة الصيف المشتركة",
            NameEn = "Summer Joint Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(14),
            TotalDiscountRate = Percentage.FromFraction(0.25m),
            MealMateShareRate = Percentage.FromFraction(0.15m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            FundingMode = CampaignFundingMode.Shared,
            MaxSubscribers = 100,
            CurrentSubscribers = 5
        };

        campaign.Programs.Add(new CampaignProgram { CampaignId = campaign.Id, ProgramId = programId });
        campaign.Bundles.Add(new CampaignBundle { CampaignId = campaign.Id, BundleId = bundleId });
        campaign.RestaurantEnrollments.Add(new CampaignRestaurantEnrollment
        {
            CampaignId = campaign.Id,
            RestaurantId = restaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted
        });

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var offer = await syncService.SyncCampaignToOfferAsync(campaign.Id);

        offer.Should().NotBeNull();
        offer!.CampaignId.Should().Be(campaign.Id);
        offer.NameAr.Should().Be("حملة الصيف المشتركة");
        offer.NameEn.Should().Be("Summer Joint Campaign");
        offer.Status.Should().Be(OfferStatus.Active);
        offer.DiscountRate!.Value.Should().Be(0.25m);
        offer.PlatformShareRate!.Value.Should().Be(0.15m);
        offer.RestaurantShareRate!.Value.Should().Be(0.10m);
        offer.TotalUsageLimit.Should().Be(100);
        offer.CurrentUsageCount.Should().Be(5);
        offer.EligiblePrograms.Should().ContainSingle(p => p.ProgramId == programId);
        offer.EligibleBundles.Should().ContainSingle(b => b.BundleId == bundleId);
        offer.EligibleRestaurants.Should().ContainSingle(r => r.RestaurantId == restaurantId);
    }

    [Fact]
    public async Task SyncCampaignOfferOnStatusChangedHandler_UpdatesOfferStatus_WhenCampaignChangesStatus()
    {
        using var db = CreateDbContext();
        var syncService = new CampaignOfferSyncService(db);
        var handler = new SyncCampaignOfferOnStatusChangedHandler(syncService);

        var campaign = new Campaign
        {
            NameAr = "حملة اختبار",
            NameEn = "Test Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-2),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            FundingMode = CampaignFundingMode.Shared
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        // Initial sync to Active
        await syncService.SyncCampaignToOfferAsync(campaign.Id);

        var offerBefore = await db.Offers.FirstOrDefaultAsync(o => o.CampaignId == campaign.Id);
        offerBefore!.Status.Should().Be(OfferStatus.Active);

        // Campaign stopped -> triggers Stopped status
        campaign.Status = CampaignStatus.Stopped;
        await db.SaveChangesAsync();

        var domainEvent = new CampaignStatusChangedDomainEvent(
            campaign.Id,
            CampaignStatus.Active,
            CampaignStatus.Stopped,
            "admin-1",
            "Emergency stop");

        var notification = new DomainEventNotification<CampaignStatusChangedDomainEvent>(domainEvent);
        await handler.Handle(notification, CancellationToken.None);

        var offerAfter = await db.Offers.FirstOrDefaultAsync(o => o.CampaignId == campaign.Id);
        offerAfter!.Status.Should().Be(OfferStatus.Paused);
    }

    [Fact]
    public async Task GetCustomerAvailableOffersQuery_AutomaticallyReturnsActiveCampaign_AsCustomerOffer()
    {
        using var db = CreateDbContext();
        var eligibilityEngine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);

        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();

        // Add Active Campaign to database
        var activeCampaign = new Campaign
        {
            NameAr = "خصم اشتراك سبتمبر",
            NameEn = "September Subscription Promo",
            Status = CampaignStatus.Active,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.30m),
            MealMateShareRate = Percentage.FromFraction(0.20m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            FundingMode = CampaignFundingMode.Shared,
            MaxSubscribers = 50,
            CurrentSubscribers = 10
        };
        activeCampaign.Programs.Add(new CampaignProgram { CampaignId = activeCampaign.Id, ProgramId = programId });
        activeCampaign.Bundles.Add(new CampaignBundle { CampaignId = activeCampaign.Id, BundleId = bundleId });

        db.Campaigns.Add(activeCampaign);
        await db.SaveChangesAsync();

        var queryHandler = new GetCustomerAvailableOffersQueryHandler(db, eligibilityEngine, clock);
        var customerId = Guid.NewGuid();

        var result = await queryHandler.Handle(new GetCustomerAvailableOffersQuery(
            customerId,
            CartAmount: 50.0m,
            DurationDays: 14,
            ProgramId: programId,
            BundleId: bundleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Should().ContainSingle();

        var offer = result.Value[0];
        offer.CampaignId.Should().Be(activeCampaign.Id);
        offer.NameAr.Should().Be("خصم اشتراك سبتمبر");
        offer.NameEn.Should().Be("September Subscription Promo");
        offer.DiscountRate.Should().Be(0.30m);
    }

    [Fact]
    public void OfferEligibilityEngine_RestrictsByProgramAndBundle()
    {
        var engine = new OfferEligibilityEngine();
        var program1 = Guid.NewGuid();
        var program2 = Guid.NewGuid();
        var bundle1 = Guid.NewGuid();

        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            CustomerSegment = CustomerSegmentFilter.All
        };
        offer.EligiblePrograms.Add(new OfferProgram { OfferId = offer.Id, ProgramId = program1 });
        offer.EligibleBundles.Add(new OfferBundle { OfferId = offer.Id, BundleId = bundle1 });

        var now = DateTime.UtcNow;

        // Context with matching program and bundle
        var validCtx = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, 100m, 14, null, program1, bundle1, null);
        var validResult = engine.Evaluate(offer, validCtx, now);
        validResult.IsEligible.Should().BeTrue();
        validResult.CalculatedDiscountAmount.Should().Be(20.000m);

        // Context with wrong program
        var invalidProgramCtx = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, 100m, 14, null, program2, bundle1, null);
        var invalidResult = engine.Evaluate(offer, invalidProgramCtx, now);
        invalidResult.IsEligible.Should().BeFalse();
        invalidResult.IneligibilityReasons.Should().Contain("Offer is not applicable to the selected program.");
    }

    [Fact]
    public void OfferEligibilityEngine_PreventsPromoCodeStacking_WhenDisabled()
    {
        var engine = new OfferEligibilityEngine();
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            AllowPromoCodeStacking = false
        };

        var ctxWithPromo = new OfferEligibilityContext(
            Guid.NewGuid(), 0, 0, 100m, 14, null, null, null, null, HasPromoCode: true);

        var result = engine.Evaluate(offer, ctxWithPromo, DateTime.UtcNow);
        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain("Offer does not allow stacking with additional promo codes.");
    }

    [Fact]
    public async Task EvaluateOfferDiscountQuery_ReturnsEvaluation_WithoutCommittingUsage()
    {
        using var db = CreateDbContext();
        var engine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var offer = new Offer
        {
            NameAr = "عرض 20%",
            NameEn = "20% Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.20m),
            TotalUsageLimit = 10,
            CurrentUsageCount = 2,
            AllowPromoCodeStacking = false
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var handler = new EvaluateOfferDiscountQueryHandler(db, engine, clock);
        var customerId = Guid.NewGuid();

        var query = new EvaluateOfferDiscountQuery(
            offer.Id, customerId, CartAmount: 100m, DurationDays: 14);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.IsEligible.Should().BeTrue();
        result.Value.CalculatedDiscountAmount.Should().Be(20.000m);
        result.Value.AllowPromoCodeStacking.Should().BeFalse();

        // Crucial test: Usage count MUST NOT change on preview/evaluate!
        var offerInDb = await db.Offers.FindAsync(offer.Id);
        offerInDb!.CurrentUsageCount.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateOfferDiscountQuery_WithPromoCode_FindsOfferAndReturnsEvaluation()
    {
        using var db = CreateDbContext();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

        var offer = new Offer
        {
            NameAr = "خصم 20% على الباقات الشهرية",
            NameEn = "20% Monthly Discount",
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            FundingMode = OfferFundingMode.PlatformFull,
            DiscountRate = Percentage.FromFraction(0.20m),
            StartsAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc),
            MinSubscriptionDays = 26,
            PromoCode = "SAVE20",
            TermsAndConditionsAr = "يسري العرض على باقات شهرية فقط ، لا يمكن دمجه مع عروض أخرى"
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var engine = new OfferEligibilityEngine();
        var handler = new EvaluateOfferDiscountQueryHandler(db, engine, clock);

        var query = new EvaluateOfferDiscountQuery(
            PromoCode: "save20", // Case-insensitive
            CustomerId: Guid.NewGuid(),
            CartAmount: 52m,
            DurationDays: 26);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.IsEligible.Should().BeTrue();
        result.Value.OfferId.Should().Be(offer.Id);
        result.Value.OfferNameAr.Should().Be("خصم 20% على الباقات الشهرية");
        result.Value.PromoCode.Should().Be("SAVE20");
        result.Value.CalculatedDiscountAmount.Should().Be(10.400m);
    }
}

