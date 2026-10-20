using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using CampaignStatus = MealMate.Domain.Enums.CampaignStatus;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CustomerCampaignDiscoveryTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly CustomerCampaignOptionProjector _projector;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly GetCustomerCampaignsQueryHandler _listHandler;
    private readonly GetCustomerCampaignDetailsQueryHandler _detailsHandler;

    public CustomerCampaignDiscoveryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);

        _projector = new CustomerCampaignOptionProjector(_context);
        _evaluation = new CustomerCampaignEvaluationService(
            _context, _clock, new CampaignEligibilityEngine(),
            new CustomerDeliveryAddressService(_context, _clock));
        _listHandler = new GetCustomerCampaignsQueryHandler(_context, _clock, _projector);
        _detailsHandler = new GetCustomerCampaignDetailsQueryHandler(_context, _clock, _evaluation, _projector);
    }

    private async Task<Campaign> SeedCampaignAsync(
        string nameEn,
        CampaignStatus status = CampaignStatus.Active,
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        int? maxSubscribers = 10,
        int currentSubscribers = 0,
        bool withApprovedPolicy = true)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة " + nameEn,
            NameEn = nameEn,
            Status = status,
            StartsAtUtc = startsAt ?? Now.AddHours(-2),
            EndsAtUtc = endsAt ?? Now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.30m),
            MealMateShareRate = Percentage.FromFraction(0.15m),
            RestaurantShareRate = Percentage.FromFraction(0.15m),
            MaxSubscribers = maxSubscribers,
            CurrentSubscribers = currentSubscribers
        };
        _context.Campaigns.Add(campaign);

        var program = new Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج كيتو",
            NameEn = "Keto Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Programs.Add(program);

        var catalogBundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة ٢٦ يوم",
            NameEn = "26-Day Bundle",
            DescriptionAr = "وصف الباقة",
            DescriptionEn = "Bundle description",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Bundles.Add(catalogBundle);

        var campaignBundle = new CampaignBundle { CampaignId = campaign.Id, BundleId = catalogBundle.Id };
        _context.CampaignBundles.Add(campaignBundle);
        await _context.SaveChangesAsync();

        _context.CampaignBundleDurationPrices.Add(CampaignBundleDurationPrice.Create(
            campaignBundle.Id, 26, new Money(156.000m, "KWD"), "System", 1, Now).Value);
        await _context.SaveChangesAsync();

        if (withApprovedPolicy)
        {
            var policy = CampaignPricingPolicy.Create(
                1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
                CampaignPricingPolicy.RoundingAwayFromZero,
                "{\"restaurantShare\":0.5}", "{\"stacking\":\"exclusive\"}", "{\"basis\":\"undeliveredDays\"}").Value;
            policy.Approve(Guid.NewGuid(), Now);
            _context.CampaignPricingPolicies.Add(policy);
            await _context.SaveChangesAsync();
        }

        return campaign;
    }

    private async Task SeedParticipantWithCapacityAsync(
        Campaign campaign,
        params DateOnly[] serviceDates)
    {
        var restaurantId = Guid.NewGuid();
        _context.CampaignRestaurantEnrollments.Add(new CampaignRestaurantEnrollment
        {
            CampaignId = campaign.Id,
            RestaurantId = restaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            RequestedAtUtc = Now
        });
        foreach (var date in serviceDates)
        {
            _context.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
            {
                CampaignId = campaign.Id,
                RestaurantId = restaurantId,
                ServiceDate = date,
                MaxBoxes = 5,
                ReservedBoxes = 0
            });
        }
        await _context.SaveChangesAsync();
    }

    private static DateOnly D(int daysFromNow) => DateOnly.FromDateTime(Now.Date.AddDays(daysFromNow));

    [Fact]
    public async Task List_ReturnsOnlyActiveCampaignsInsideWindow_WithConfirmableMinPrice()
    {
        var visible = await SeedCampaignAsync("Visible");
        await SeedParticipantWithCapacityAsync(visible, Enumerable.Range(1, 30).Select(D).ToArray());

        await SeedCampaignAsync("Stopped", status: CampaignStatus.Stopped);
        await SeedCampaignAsync("Ended", status: CampaignStatus.Ended);
        await SeedCampaignAsync("Draft", status: CampaignStatus.Draft);
        await SeedCampaignAsync("Scheduled", status: CampaignStatus.Scheduled);
        await SeedCampaignAsync("NotStartedYet", startsAt: Now.AddHours(2));
        await SeedCampaignAsync("WindowJustClosed", endsAt: Now); // startsAt <= now < endsAt excludes endsAt == now

        var result = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(i => i.CampaignId == visible.Id);
        result.Value.Items.Should().OnlyContain(i => i.NameAr.StartsWith("حملة"));
        var item = result.Value.Items.Single();
        item.MinPrice.Should().Be(109.200m); // 156 - round(156*0.30) under the approved policy
        item.CurrencyCode.Should().Be("KWD");
        item.MinPriceDurationDays.Should().Be(26);
        item.MinPriceCampaignBundleId.Should().NotBeNull();
        item.FullSequenceAvailable.Should().BeTrue();
        result.Value.ServerNowUtc.Should().Be(Now);
    }

    [Fact]
    public async Task List_ExcludesCapCompleteCampaigns_CountingLiveReservations()
    {
        var fullByCommitted = await SeedCampaignAsync("FullCommitted", currentSubscribers: 10);
        await SeedParticipantWithCapacityAsync(fullByCommitted, Enumerable.Range(1, 30).Select(D).ToArray());

        // A single live reservation occupies the only remaining subscriber slot.
        var fullByReserved = await SeedCampaignAsync("FullReserved", maxSubscribers: 1);
        await SeedParticipantWithCapacityAsync(fullByReserved, Enumerable.Range(1, 30).Select(D).ToArray());
        _context.CampaignCheckoutSessions.Add(new CampaignCheckoutSession(Guid.NewGuid())
        {
            CampaignId = fullByReserved.Id,
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending,
            ExpiresAtUtc = Now.AddMinutes(10)
        });
        await _context.SaveChangesAsync();

        // An expired reservation session does not hold a slot.
        _context.CampaignCheckoutSessions.Add(new CampaignCheckoutSession(Guid.NewGuid())
        {
            CampaignId = fullByReserved.Id,
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = Now.AddMinutes(-1)
        });
        await _context.SaveChangesAsync();

        var result = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotContain(i => i.CampaignId == fullByCommitted.Id);
        result.Value.Items.Should().NotContain(i => i.CampaignId == fullByReserved.Id);
    }

    [Fact]
    public async Task List_ExcludesCampaignsWithoutApprovedParticipants()
    {
        var campaign = await SeedCampaignAsync("NoParticipants");
        // capacity rows exist but nobody is an approved participant
        _context.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = Guid.NewGuid(),
            ServiceDate = D(1),
            MaxBoxes = 5,
            ReservedBoxes = 0
        });
        await _context.SaveChangesAsync();

        var result = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid()), CancellationToken.None);

        result.Value.Items.Should().NotContain(i => i.CampaignId == campaign.Id);
    }

    [Fact]
    public async Task List_WithholdsMinPrice_WhenSingleDayCapacityCannotServeFullDuration()
    {
        var campaign = await SeedCampaignAsync("PartialCapacity");
        await SeedParticipantWithCapacityAsync(campaign, D(1)); // one servable day only, 26-day duration

        var result = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid()), CancellationToken.None);

        result.Value.Items.Should().ContainSingle(i => i.CampaignId == campaign.Id);
        var item = result.Value.Items.Single();
        item.MinPrice.Should().BeNull();
        item.CurrencyCode.Should().BeNull();
        item.FullSequenceAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task List_WithholdsMinPrice_WhenPricingPolicyNotApproved()
    {
        var campaign = await SeedCampaignAsync("NoPolicy", withApprovedPolicy: false);
        await SeedParticipantWithCapacityAsync(campaign, Enumerable.Range(1, 30).Select(D).ToArray());

        var result = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid()), CancellationToken.None);

        var item = result.Value.Items.Single(i => i.CampaignId == campaign.Id);
        item.MinPrice.Should().BeNull();
        item.CurrencyCode.Should().BeNull();
    }

    [Fact]
    public async Task List_KeysetPagination_IsStableAndRejectsGarbageCursor()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = await SeedCampaignAsync($"Paged{i}", startsAt: Now.AddHours(-2).AddMinutes(i));
            await SeedParticipantWithCapacityAsync(c, Enumerable.Range(1, 30).Select(D).ToArray());
        }

        var page1 = await _listHandler.Handle(new GetCustomerCampaignsQuery(Guid.NewGuid(), PageSize: 2), CancellationToken.None);
        page1.Value.Items.Should().HaveCount(2);
        page1.Value.NextCursor.Should().NotBeNullOrEmpty();

        var page2 = await _listHandler.Handle(
            new GetCustomerCampaignsQuery(Guid.NewGuid(), PageSize: 2, Cursor: page1.Value.NextCursor),
            CancellationToken.None);
        page2.Value.Items.Should().ContainSingle();
        page1.Value.Items.Select(i => i.CampaignId).Should().NotIntersectWith(page2.Value.Items.Select(i => i.CampaignId));

        var bad = await _listHandler.Handle(
            new GetCustomerCampaignsQuery(Guid.NewGuid(), Cursor: "not-a-cursor"),
            CancellationToken.None);
        bad.IsFailure.Should().BeTrue();
        bad.Error.Code.Should().Be("Campaign.InvalidCursor");
    }

    [Fact]
    public async Task Details_ReturnsOptionsWithPrices_ButNeverFundingContributions()
    {
        var campaign = await SeedCampaignAsync("Detailed");
        await SeedParticipantWithCapacityAsync(campaign, Enumerable.Range(1, 30).Select(D).ToArray());
        var customerId = Guid.NewGuid();

        var result = await _detailsHandler.Handle(
            new GetCustomerCampaignDetailsQuery(customerId, campaign.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.NameAr.Should().StartWith("حملة");
        dto.NameEn.Should().Be("Detailed");
        dto.Options.Should().ContainSingle();
        var option = dto.Options.Single();
        option.CampaignBundleId.Should().NotBe(option.CatalogBundleId);
        option.ProgramNameEn.Should().Be("Keto Program");
        option.BundleNameAr.Should().Be("باقة ٢٦ يوم");
        option.FinalPrice.Should().Be(109.200m);
        option.CanShowReferencePrice.Should().BeTrue();
        option.ReferencePrice.Should().Be(156.000m);
        option.CustomerDiscountPercent.Should().Be(30.000m); // percent scale in the new contract
        option.FullSequenceAvailable.Should().BeTrue();

        // Privacy: funding contributions are projection-forbidden, not merely hidden in Flutter.
        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("Contribution");
        json.Should().NotContain("ShareRate");
        json.Should().NotContain("FundingMode");
    }

    [Fact]
    public async Task Details_ShowsProfileBlockers_WithoutBlockingBrowsing()
    {
        var campaign = await SeedCampaignAsync("Gated");
        await SeedParticipantWithCapacityAsync(campaign, Enumerable.Range(1, 30).Select(D).ToArray());
        var customerId = Guid.NewGuid(); // no profile at all

        var result = await _detailsHandler.Handle(
            new GetCustomerCampaignDetailsQuery(customerId, campaign.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeFalse();
        var codes = result.Value.BlockingReasons.Select(b => b.Code).ToList();
        codes.Should().Contain("profile.address_missing");
        codes.Should().Contain("profile.allergies_missing");
        codes.Should().Contain("profile.dietary_missing");
        result.Value.BlockingReasons.Should().OnlyContain(b => b.MessageAr.Length > 0 && b.MessageEn.Length > 0);
    }

    [Fact]
    public async Task Details_UnknownCampaign_Is404_AndNeverPublishedStatesAreInvisible()
    {
        var draft = await SeedCampaignAsync("HiddenDraft", status: CampaignStatus.Draft);
        var scheduled = await SeedCampaignAsync("HiddenScheduled", status: CampaignStatus.Scheduled);

        (await _detailsHandler.Handle(new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Error.Code.Should().Be("Campaign.NotFound");
        (await _detailsHandler.Handle(new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), draft.Id), CancellationToken.None))
            .Error.Code.Should().Be("Campaign.NotFound");
        (await _detailsHandler.Handle(new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), scheduled.Id), CancellationToken.None))
            .Error.Code.Should().Be("Campaign.NotFound");
    }

    [Fact]
    public async Task Details_StoppedOrEndedPreviouslyPublishedCampaign_IsGone()
    {
        var stopped = await SeedCampaignAsync("StoppedVisible", status: CampaignStatus.Stopped);
        var ended = await SeedCampaignAsync("EndedVisible", status: CampaignStatus.Ended);

        (await _detailsHandler.Handle(new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), stopped.Id), CancellationToken.None))
            .Error.Code.Should().Be("Campaign.NoLongerAvailable");
        (await _detailsHandler.Handle(new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), ended.Id), CancellationToken.None))
            .Error.Code.Should().Be("Campaign.NoLongerAvailable");
    }

    [Fact]
    public async Task Details_ExplicitExhaustedOption_IsConflict()
    {
        var campaign = await SeedCampaignAsync("OptionConflict");
        await SeedParticipantWithCapacityAsync(campaign, D(1)); // one day only; 26-day option cannot be served

        var campaignBundleId = await _context.CampaignBundles
            .Where(b => b.CampaignId == campaign.Id).Select(b => b.Id).SingleAsync();

        var result = await _detailsHandler.Handle(
            new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), campaign.Id, campaignBundleId, 26),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.OptionCapacityUnavailable");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Details_CapCompleteCampaign_ShowsCapacityBlocker()
    {
        var campaign = await SeedCampaignAsync("CapFull", maxSubscribers: 5, currentSubscribers: 5);
        await SeedParticipantWithCapacityAsync(campaign, Enumerable.Range(1, 30).Select(D).ToArray());

        var result = await _detailsHandler.Handle(
            new GetCustomerCampaignDetailsQuery(Guid.NewGuid(), campaign.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RemainingSubscriberSlots.Should().Be(0);
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "campaign.capacity_full");
        result.Value.CanCheckout.Should().BeFalse();
    }

    [Fact]
    public async Task OffersQuery_WithCampaignSeparation_ReturnsNoCampaigns_AndDefaultDeduplicates()
    {
        var campaign = await SeedCampaignAsync("MergedCampaign");
        await SeedParticipantWithCapacityAsync(campaign, Enumerable.Range(1, 30).Select(D).ToArray());

        // A synchronized Offer row already references this campaign: no virtual duplicate.
        _context.Offers.Add(new Offer
        {
            NameAr = "حملة مزامنة",
            NameEn = "Synced Campaign",
            CampaignId = campaign.Id,
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = DiscountType.Percentage,
            StartsAtUtc = Now.AddHours(-2),
            EndsAtUtc = Now.AddDays(10)
        });
        await _context.SaveChangesAsync();

        var handler = new GetCustomerAvailableOffersQueryHandler(_context, new OfferEligibilityEngine(), _clock);

        var unified = await handler.Handle(new GetCustomerAvailableOffersQuery(Guid.NewGuid()), CancellationToken.None);
        unified.Value.Count(o => o.CampaignId == campaign.Id).Should().Be(1); // no virtual duplicate

        var separated = await handler.Handle(new GetCustomerAvailableOffersQuery(Guid.NewGuid(), IncludeCampaigns: false), CancellationToken.None);
        separated.Value.Should().NotContain(o => o.CampaignId == campaign.Id);
    }
}
