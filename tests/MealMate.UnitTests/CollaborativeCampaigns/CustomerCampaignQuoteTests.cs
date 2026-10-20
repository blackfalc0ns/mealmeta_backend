using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using CampaignStatus = MealMate.Domain.Enums.CampaignStatus;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CustomerCampaignQuoteTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly SubscriptionScheduleService _schedule;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly CreateCustomerCampaignQuoteCommandHandler _handler;

    public CustomerCampaignQuoteTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);
        _schedule = new SubscriptionScheduleService(_clock);

        _evaluation = new CustomerCampaignEvaluationService(
            _context, _clock, new CampaignEligibilityEngine(),
            new CustomerDeliveryAddressService(_context, _clock),
            _schedule);
        _handler = new CreateCustomerCampaignQuoteCommandHandler(
            _context, _clock, _evaluation, new SubscriptionFlowCouponService(_context, new OfferEligibilityEngine(), _clock));
    }

    private async Task<(Campaign Campaign, Guid CampaignBundleId, Guid CatalogBundleId, Guid ProgramId)> SeedAsync(
        bool withApprovedPolicy = true,
        decimal headline = 0.30m)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة كيتو",
            NameEn = "Keto Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = Now.AddHours(-2),
            EndsAtUtc = Now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(headline),
            MealMateShareRate = Percentage.FromFraction(0.15m),
            RestaurantShareRate = Percentage.FromFraction(0.15m)
        };
        _context.Campaigns.Add(campaign);

        var program = new Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج",
            NameEn = "Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Programs.Add(program);

        var catalogBundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة ٢٦",
            NameEn = "26 Bundle",
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

        return (campaign, campaignBundle.Id, catalogBundle.Id, program.Id);
    }

    private async Task<Guid> SeedReadyCustomerAsync()
    {
        var customerId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = customerId,
            Label = "المنزل",
            AddressLine = "شارع 1",
            RegionId = Guid.NewGuid(),
            Governorate = "العاصمة",
            IsDefault = true
        };
        _context.CustomerAddresses.Add(address);
        _context.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = Now.AddMinutes(-1),
            DietaryPreferencesCompletedAtUtc = Now.AddMinutes(-1),
            ActiveDeliveryAddressId = address.Id,
            ActiveDeliveryRegionId = address.RegionId
        });
        await _context.SaveChangesAsync();
        return customerId;
    }

    private CreateCustomerCampaignQuoteCommand BuildCommand(
        Campaign campaign, Guid campaignBundleId, Guid programId, Guid customerId,
        DateOnly? startDate = null, string? promoCode = null, Guid? addressId = null)
    {
        var resolvedAddressId = addressId
            ?? _context.CustomerAddresses.Where(a => a.UserId == customerId).Select(a => a.Id).Cast<Guid?>().FirstOrDefault()
            ?? Guid.Empty;
        return new CreateCustomerCampaignQuoteCommand(
            customerId, campaign.Id, programId, campaignBundleId, 26,
            startDate ?? DateOnly.FromDateTime(Now.Date.AddDays(2)),
            resolvedAddressId, promoCode);
    }

    [Fact]
    public async Task Handle_WhenReady_PersistsQuoteWithServerDerivedScheduleAndPrice()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();

        var result = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.QuoteId.Should().NotBe(Guid.Empty);
        result.Value.CanCheckout.Should().BeTrue();
        result.Value.BlockingReasons.Should().BeEmpty();
        result.Value.Price.FinalPrice.Should().Be(109.200m); // 156 - round(156*0.30)
        result.Value.Price.ReferencePrice.Should().Be(156.000m);
        result.Value.Price.CustomerDiscountPercent.Should().Be(30.000m);
        result.Value.ExpiresAtUtc.Should().Be(Now.AddMinutes(15));

        var entity = await _context.CustomerCampaignQuotes.SingleAsync(q => q.Id == result.Value.QuoteId);
        entity.CustomerId.Should().Be(customerId);
        entity.CatalogBundleId.Should().NotBe(campaignBundleId);
        entity.PricingPolicyVersion.Should().Be(1);
        entity.CampaignSnapshotVersion.Should().Be(campaign.CurrentSnapshotVersion);
        entity.ServiceDates.Should().HaveCount(26);
        entity.ServiceDates.Should().NotContain(d => d.DayOfWeek == DayOfWeek.Friday);
        entity.ServiceDates.Min().Should().Be(result.Value.Selection.StartDate);
    }

    [Fact]
    public async Task Handle_WhenNotReady_ReturnsBlockersWithoutPersistingQuote()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = Guid.NewGuid(); // no profile → gates unmet

        var result = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeFalse();
        result.Value.QuoteId.Should().Be(Guid.Empty);
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "profile.address_missing");
        (await _context.CustomerCampaignQuotes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenPolicyMissing_ReturnsBlockerWithoutFabricatedReferencePrice()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync(withApprovedPolicy: false);
        var customerId = await SeedReadyCustomerAsync();

        var result = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId), CancellationToken.None);

        result.Value.CanCheckout.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(b => b.Code == "campaign.pricing_policy_not_approved");
        result.Value.Price.ReferencePrice.Should().BeNull();
        result.Value.Price.CustomerDiscountPercent.Should().BeNull();
        result.Value.Price.CanShowReferencePrice.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenDurationUnpriced_ReturnsNotFound()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();
        var command = BuildCommand(campaign, campaignBundleId, programId, customerId) with { DurationDays = 14 };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.PricingNotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenAddressNotOwned_ReturnsForbidden()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();
        var strangerAddress = new CustomerAddress
        {
            UserId = Guid.NewGuid(), Label = "غريب", AddressLine = "شارع 9", RegionId = Guid.NewGuid()
        };
        _context.CustomerAddresses.Add(strangerAddress);
        await _context.SaveChangesAsync();

        var command = BuildCommand(campaign, campaignBundleId, programId, customerId) with { AddressId = strangerAddress.Id };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.AddressNotOwned");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_WhenStartDateInPast_ReturnsValidation()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();
        var command = BuildCommand(campaign, campaignBundleId, programId, customerId) with { StartDate = DateOnly.FromDateTime(Now.Date.AddDays(-1)) };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.InvalidStartDate");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_WhenProgramDoesNotMatchCampaign_ReturnsValidation()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();
        var command = BuildCommand(campaign, campaignBundleId, programId, customerId) with { ProgramId = Guid.NewGuid() };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.SelectionMismatch");
    }

    [Fact]
    public async Task Handle_WhenPolicyVersionChanges_NewQuoteRecordsNewVersion()
    {
        var (campaign, campaignBundleId, _, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();

        var first = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId), CancellationToken.None);

        var policyV2 = CampaignPricingPolicy.Create(
            2, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero,
            "{\"restaurantShare\":0.5}", "{\"stacking\":\"exclusive\"}", "{\"basis\":\"undeliveredDays\"}").Value;
        policyV2.Approve(Guid.NewGuid(), Now);
        _context.CampaignPricingPolicies.Add(policyV2);
        await _context.SaveChangesAsync();

        var second = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        (await _context.CustomerCampaignQuotes.FindAsync(first.Value.QuoteId))!.PricingPolicyVersion.Should().Be(1);
        (await _context.CustomerCampaignQuotes.FindAsync(second.Value.QuoteId))!.PricingPolicyVersion.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithValidCoupon_KeepsCodeButRefusesStackingWithoutDiscount()
    {
        var (campaign, campaignBundleId, catalogBundleId, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();

        _context.PromoCodes.Add(new PromoCode
        {
            Code = "WELCOME10",
            DiscountPercent = Percentage.FromFraction(0.10m),
            IsActive = true,
            StartsAtUtc = Now.AddDays(-1)
        });
        await _context.SaveChangesAsync();

        var command = BuildCommand(campaign, campaignBundleId, programId, customerId, promoCode: "WELCOME10");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeTrue();
        result.Value.Coupon.Should().NotBeNull();
        result.Value.Coupon!.Code.Should().Be("WELCOME10");
        result.Value.Coupon.Status.Should().Be(SubscriptionCouponStatuses.StackingConflict);
        result.Value.Coupon.DiscountAmount.Should().Be(0m);
        result.Value.Price.FinalPrice.Should().Be(109.200m); // campaign price stands

        var entity = await _context.CustomerCampaignQuotes.SingleAsync(q => q.Id == result.Value.QuoteId);
        entity.CouponDiscountAmount.Should().Be(0m);
        entity.PromoCode.Should().Be("WELCOME10");
    }

    [Fact]
    public async Task Handle_CouponStatuses_MapThroughCurrentEvaluator()
    {
        var (campaign, campaignBundleId, catalogBundleId, programId) = await SeedAsync();
        var customerId = await SeedReadyCustomerAsync();

        // usage exhausted: MaxUses=1 with UsedCount=1
        _context.PromoCodes.Add(new PromoCode
        {
            Code = "USEDMAX",
            DiscountPercent = Percentage.FromFraction(0.10m),
            IsActive = true,
            StartsAtUtc = Now.AddDays(-1),
            MaxUses = 1,
            UsedCount = 1
        });
        // expired
        _context.PromoCodes.Add(new PromoCode
        {
            Code = "OLDCODE",
            DiscountPercent = Percentage.FromFraction(0.10m),
            IsActive = true,
            StartsAtUtc = Now.AddDays(-10),
            EndsAtUtc = Now.AddDays(-1)
        });
        // ineligible via targeting
        _context.PromoCodes.Add(new PromoCode
        {
            Code = "OTHERPROGRAM",
            DiscountPercent = Percentage.FromFraction(0.10m),
            IsActive = true,
            StartsAtUtc = Now.AddDays(-1),
            TargetingJson = "{\"ProgramId\":\"" + Guid.NewGuid() + "\"}"
        });
        await _context.SaveChangesAsync();

        var exhausted = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId, promoCode: "USEDMAX"), CancellationToken.None);
        var expired = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId, promoCode: "OLDCODE"), CancellationToken.None);
        var ineligible = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId, promoCode: "OTHERPROGRAM"), CancellationToken.None);
        var unknown = await _handler.Handle(BuildCommand(campaign, campaignBundleId, programId, customerId, promoCode: "GHOSTCODE"), CancellationToken.None);

        exhausted.Value.Coupon!.Status.Should().Be(SubscriptionCouponStatuses.UsageExhausted);
        expired.Value.Coupon!.Status.Should().Be(SubscriptionCouponStatuses.Expired);
        ineligible.Value.Coupon!.Status.Should().Be(SubscriptionCouponStatuses.Ineligible);
        unknown.Value.Coupon!.Status.Should().Be(SubscriptionCouponStatuses.NotFound);
    }
}
