using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record CustomerCampaignEvaluationRequest(
    Guid CampaignId,
    Guid CampaignBundleId,
    int DurationDays,
    Guid? CustomerId = null,
    Guid? AddressId = null,
    DateOnly? StartDate = null);

public sealed record CustomerCampaignEvaluation(
    Campaign Campaign,
    CampaignBundle CampaignBundle,
    Guid CatalogBundleId,
    Guid ProgramId,
    CampaignBundleDurationPrice DurationPrice,
    CampaignPriceCalculationResult Price,
    CampaignPricingPolicy? Policy,
    bool IsCheckoutReady,
    IReadOnlyList<CustomerCampaignBlock> Blocks,
    /// <summary>Engine verdict reasons preserved verbatim for the legacy preview contract.</summary>
    IReadOnlyList<string> LegacyIneligibilityReasons,
    int PriorSubscriptionCount,
    int ActiveCampaignSubscriptionCount,
    CustomerActiveDeliveryAddress? DeliveryAddress,
    IReadOnlyList<DateOnly> ServiceDates)
{
    public bool HasProfileGateBlocks => Blocks.Any(b => b.Code.StartsWith("profile.", StringComparison.Ordinal));
}

public interface ICustomerCampaignEvaluationService
{
    Task<Result<CustomerCampaignEvaluation>> EvaluateAsync(
        CustomerCampaignEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single server-side source for campaign price, eligibility and checkout blockers shared by
/// price preview, quote and checkout. Subscription counts and the delivery address are always
/// read from the authorized customer's data — never trusted from client payloads.
/// </summary>
public sealed class CustomerCampaignEvaluationService : ICustomerCampaignEvaluationService
{
    private static readonly IReadOnlyList<DateOnly> NoServiceDates = Array.Empty<DateOnly>();

    private const int MinDurationDays = 1;
    private const int MaxDurationDays = 365;

    /// <summary>Subscription states that no longer hold a campaign seat for the per-customer limit.</summary>
    private static readonly IReadOnlyList<SubscriptionStatus> SeatReleasingStatuses = new[]
    {
        SubscriptionStatus.Draft,
        SubscriptionStatus.Completed,
        SubscriptionStatus.Cancelled,
        SubscriptionStatus.Upgraded,
        SubscriptionStatus.Expired,
        SubscriptionStatus.PaymentFailed
    };

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignEligibilityEngine _eligibilityEngine;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly ISubscriptionScheduleService? _schedule;

    public CustomerCampaignEvaluationService(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignEligibilityEngine eligibilityEngine,
        ICustomerDeliveryAddressService deliveryAddresses,
        ISubscriptionScheduleService? schedule = null)
    {
        _context = context;
        _clock = clock;
        _eligibilityEngine = eligibilityEngine;
        _deliveryAddresses = deliveryAddresses;
        _schedule = schedule;
    }

    public async Task<Result<CustomerCampaignEvaluation>> EvaluateAsync(
        CustomerCampaignEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DurationDays <= 0)
        {
            return Result.Failure<CustomerCampaignEvaluation>(
                Error.Validation("Campaign.InvalidDuration", "Duration days must be greater than zero."));
        }

        var campaign = await _context.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<CustomerCampaignEvaluation>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        // The bundle belongs to this campaign; a bundle of another campaign is not addressable here.
        var campaignBundle = await _context.CampaignBundles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.CampaignBundleId && b.CampaignId == campaign.Id, cancellationToken);
        if (campaignBundle is null)
        {
            return Result.Failure<CustomerCampaignEvaluation>(
                Error.NotFound("Campaign.BundleNotInCampaign",
                    $"Bundle '{request.CampaignBundleId}' is not part of campaign '{request.CampaignId}'."));
        }

        var pricing = await _context.CampaignBundleDurationPrices.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CampaignBundleId == campaignBundle.Id && p.DurationDays == request.DurationDays, cancellationToken);
        if (pricing is null)
        {
            return Result.Failure<CustomerCampaignEvaluation>(
                Error.NotFound("Campaign.PricingNotFound",
                    $"No reference price found for Bundle '{campaignBundle.Id}' with duration {request.DurationDays} days."));
        }

        var catalogBundle = await _context.Bundles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == campaignBundle.BundleId, cancellationToken);
        if (catalogBundle is null)
        {
            return Result.Failure<CustomerCampaignEvaluation>(
                Error.NotFound("campaign.payment.catalog_missing",
                    $"Catalog bundle '{campaignBundle.BundleId}' referenced by the campaign no longer exists."));
        }

        // Pricing requires an explicitly approved policy covering the bundle currency.
        var policy = await _context.CampaignPricingPolicies.AsNoTracking()
            .Where(p => p.ApprovedByUserId != null && p.CurrencyCode == pricing.ReferenceCustomerPrice.CurrencyCode)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        var discountSplit = Domain.CollaborativeCampaigns.ValueObjects.CampaignDiscountSplit.Of(
            Percentage.FromFraction(campaign.TotalDiscountRate.Value),
            Percentage.FromFraction(campaign.RestaurantShareRate.Value),
            Percentage.FromFraction(campaign.MealMateShareRate.Value));
        var price = new CampaignPricingCalculator().Calculate(pricing.ReferenceCustomerPrice, discountSplit, request.DurationDays);

        var now = _clock.UtcNow;
        var customerId = request.CustomerId is { } id && id != Guid.Empty ? id : (Guid?)null;

        CustomerActiveDeliveryAddress? address = null;
        int priorSubscriptions = 0;
        int activeCampaignSubscriptions = 0;
        bool hasAllergies = false;
        bool hasDietary = false;

        if (customerId.HasValue)
        {
            if (request.AddressId is { } addressId && addressId != Guid.Empty)
            {
                address = await ResolveOwnedAddressAsync(customerId.Value, addressId, cancellationToken);
                if (address is null)
                {
                    return Result.Failure<CustomerCampaignEvaluation>(
                        Error.Forbidden("Campaign.AddressNotOwned",
                            $"Address '{addressId}' is not a delivery address of this customer."));
                }
            }
            else
            {
                address = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerId.Value, cancellationToken);
            }

            priorSubscriptions = await _context.Subscriptions.AsNoTracking()
                .CountAsync(s => s.CustomerUserId == customerId.Value, cancellationToken);

            activeCampaignSubscriptions = await _context.Subscriptions.AsNoTracking()
                .CountAsync(s => s.CustomerUserId == customerId.Value
                    && s.CampaignId != null
                    && !SeatReleasingStatuses.Contains(s.Status), cancellationToken);

            var profileFlags = await _context.CustomerProfiles.AsNoTracking()
                .Where(x => x.UserId == customerId.Value)
                .Select(x => new { x.AllergiesCompletedAtUtc, x.DietaryPreferencesCompletedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            hasAllergies = profileFlags?.AllergiesCompletedAtUtc is not null;
            hasDietary = profileFlags?.DietaryPreferencesCompletedAtUtc is not null;
        }

        var profile = new CustomerEligibilityProfile(
            customerId ?? Guid.Empty,
            PriorSubscriptionCount: priorSubscriptions,
            ActiveCampaignSubscriptionCount: activeCampaignSubscriptions,
            DeliveryGovernorate: address?.Governorate,
            RequestedDurationDays: request.DurationDays);

        var engineResult = _eligibilityEngine.Evaluate(
            campaign,
            profile,
            new CampaignEligibilityCriteria(MinDurationDays: MinDurationDays, MaxDurationDays: MaxDurationDays),
            now);

        var blocks = BuildBlocks(
            campaign, now, engineResult, activeCampaignSubscriptions,
            policy is null, customerId.HasValue, address, hasAllergies, hasDietary,
            request.DurationDays);

        var serviceDates = NoServiceDates;
        if (request.StartDate.HasValue && _schedule is not null)
        {
            serviceDates = _schedule.GetServiceDates(request.StartDate.Value, request.DurationDays, includeFriday: false);
        }

        return Result.Success(new CustomerCampaignEvaluation(
            campaign,
            campaignBundle,
            catalogBundle.Id,
            catalogBundle.ProgramId,
            pricing,
            price,
            policy,
            IsCheckoutReady: blocks.Count == 0,
            blocks,
            engineResult.IneligibilityReasons,
            priorSubscriptions,
            activeCampaignSubscriptions,
            address,
            serviceDates));
    }

    private async Task<CustomerActiveDeliveryAddress?> ResolveOwnedAddressAsync(
        Guid customerId, Guid addressId, CancellationToken cancellationToken)
    {
        var address = await _context.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == customerId, cancellationToken);
        return address is null ? null : new CustomerActiveDeliveryAddress(
            address.Id,
            address.RegionId ?? Guid.Empty,
            address.Label,
            address.AddressLine,
            address.Governorate,
            address.Area,
            address.Block,
            address.BlockRegionId);
    }

    private static IReadOnlyList<CustomerCampaignBlock> BuildBlocks(
        Campaign campaign,
        DateTime nowUtc,
        CampaignEligibilityResult engineResult,
        int activeCampaignSubscriptions,
        bool policyMissing,
        bool hasCustomer,
        CustomerActiveDeliveryAddress? address,
        bool hasAllergies,
        bool hasDietary,
        int requestedDurationDays)
    {
        var blocks = new List<CustomerCampaignBlock>();

        if (campaign.Status != CampaignStatus.Active)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.not_active",
                "هذه الحملة غير متاحة حاليًا.",
                "This campaign is not available right now.",
                null));
        }

        if (nowUtc < campaign.StartsAtUtc)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.not_started",
                "لم تبدأ هذه الحملة بعد.",
                "This campaign has not started yet.",
                null));
        }
        else if (nowUtc > campaign.EndsAtUtc)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.ended",
                "انتهت هذه الحملة.",
                "This campaign has ended.",
                null));
        }

        if (activeCampaignSubscriptions >= 1)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.already_subscribed",
                "لديك اشتراك نشط في هذه الحملة.",
                "You already have an active subscription in this campaign.",
                "subscriptions"));
        }

        if (requestedDurationDays < MinDurationDays || requestedDurationDays > MaxDurationDays)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.duration_out_of_bounds",
                "مدة الاشتراك المطلوبة غير متاحة في هذه الحملة.",
                "The requested subscription duration is not available for this campaign.",
                null));
        }

        if (hasCustomer)
        {
            if (address is null)
            {
                blocks.Add(new CustomerCampaignBlock(
                    "profile.address_missing",
                    "أضف عنوان التوصيل أولًا.",
                    "Add a delivery address first.",
                    "addresses"));
            }

            if (!hasAllergies)
            {
                blocks.Add(new CustomerCampaignBlock(
                    "profile.allergies_missing",
                    "أكمل معلومات الحساسية أولًا.",
                    "Complete your allergy information first.",
                    "food-preferences"));
            }

            if (!hasDietary)
            {
                blocks.Add(new CustomerCampaignBlock(
                    "profile.dietary_missing",
                    "أكمل تفضيلاتك الغذائية أولًا.",
                    "Complete your dietary preferences first.",
                    "food-preferences"));
            }
        }

        if (policyMissing)
        {
            blocks.Add(new CustomerCampaignBlock(
                "campaign.pricing_policy_not_approved",
                "التسعير غير معتمد بعد لهذه الحملة.",
                "Pricing for this campaign is not approved yet.",
                null));
        }

        return blocks;
    }
}
