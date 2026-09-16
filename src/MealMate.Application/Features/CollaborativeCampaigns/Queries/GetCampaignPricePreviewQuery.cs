using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record CampaignPricePreviewDto(
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    Guid BundleId,
    int DurationDays,
    decimal ReferenceCustomerPrice,
    decimal TotalDiscountPct,
    decimal TotalDiscountAmount,
    decimal RestaurantContributionPct,
    decimal RestaurantContributionAmount,
    decimal PlatformContributionPct,
    decimal PlatformContributionAmount,
    decimal FinalCustomerPrice,
    decimal PricePerDay,
    string Currency,
    bool IsCustomerEligible,
    IReadOnlyList<string> IneligibilityReasons);

public sealed record GetCampaignPricePreviewQuery(
    Guid CampaignId,
    Guid BundleId,
    int DurationDays,
    Guid? CustomerId = null,
    string? Governorate = null) : IQuery<CampaignPricePreviewDto>;

public sealed class GetCampaignPricePreviewQueryHandler : IQueryHandler<GetCampaignPricePreviewQuery, CampaignPricePreviewDto>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignEligibilityEngine _eligibilityEngine;

    public GetCampaignPricePreviewQueryHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignEligibilityEngine eligibilityEngine)
    {
        _context = context;
        _clock = clock;
        _eligibilityEngine = eligibilityEngine;
    }

    public async Task<Result<CampaignPricePreviewDto>> Handle(
        GetCampaignPricePreviewQuery request,
        CancellationToken cancellationToken)
    {
        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Result.Failure<CampaignPricePreviewDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        var pricing = await _context.CampaignBundleDurationPrices
            .FirstOrDefaultAsync(p => p.CampaignBundleId == request.BundleId && p.DurationDays == request.DurationDays, cancellationToken);

        if (pricing == null)
        {
            return Result.Failure<CampaignPricePreviewDto>(
                Error.NotFound("Campaign.PricingNotFound", $"No reference price found for Bundle '{request.BundleId}' with duration {request.DurationDays} days."));
        }

        var refPrice = pricing.ReferenceCustomerPrice.Amount;
        var currency = pricing.ReferenceCustomerPrice.CurrencyCode;

        var totalPct = campaign.TotalDiscountRate?.Value ?? 0m;
        var mealMatePct = campaign.MealMateShareRate?.Value ?? 0m;
        var restaurantPct = campaign.RestaurantShareRate?.Value ?? 0m;

        var totalDiscountAmount = Math.Round(refPrice * totalPct, 3, MidpointRounding.AwayFromZero);
        var restContributionAmount = Math.Round(refPrice * restaurantPct, 3, MidpointRounding.AwayFromZero);
        var platContributionAmount = Math.Round(refPrice * mealMatePct, 3, MidpointRounding.AwayFromZero);
        var finalCustomerPrice = Math.Round(refPrice - totalDiscountAmount, 3, MidpointRounding.AwayFromZero);
        var pricePerDay = request.DurationDays > 0
            ? Math.Round(finalCustomerPrice / request.DurationDays, 3, MidpointRounding.AwayFromZero)
            : 0m;

        var profile = new CustomerEligibilityProfile(
            request.CustomerId ?? Guid.Empty,
            PriorSubscriptionCount: 0,
            ActiveCampaignSubscriptionCount: 0,
            DeliveryGovernorate: request.Governorate,
            RequestedDurationDays: request.DurationDays);

        var eligibilityResult = _eligibilityEngine.Evaluate(
            campaign,
            profile,
            new CampaignEligibilityCriteria(MinDurationDays: 1, MaxDurationDays: 365),
            _clock.UtcNow);

        var dto = new CampaignPricePreviewDto(
            campaign.Id,
            campaign.NameAr,
            campaign.NameEn,
            request.BundleId,
            request.DurationDays,
            refPrice,
            totalPct,
            totalDiscountAmount,
            restaurantPct,
            restContributionAmount,
            mealMatePct,
            platContributionAmount,
            finalCustomerPrice,
            pricePerDay,
            currency,
            eligibilityResult.IsEligible,
            eligibilityResult.IneligibilityReasons);

        return Result.Success(dto);
    }
}
