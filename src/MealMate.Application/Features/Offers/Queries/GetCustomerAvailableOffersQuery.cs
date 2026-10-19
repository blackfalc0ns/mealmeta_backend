using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record GetCustomerAvailableOffersQuery(
    Guid CustomerId = default,
    decimal CartAmount = 0m,
    int DurationDays = 0,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null
) : IQuery<IReadOnlyList<OfferDto>>;

public sealed class GetCustomerAvailableOffersQueryHandler : IQueryHandler<GetCustomerAvailableOffersQuery, IReadOnlyList<OfferDto>>
{
    private readonly IAppDbContext _context;
    private readonly IOfferEligibilityEngine _eligibilityEngine;
    private readonly IDateTimeProvider _clock;

    public GetCustomerAvailableOffersQueryHandler(
        IAppDbContext context,
        IOfferEligibilityEngine eligibilityEngine,
        IDateTimeProvider clock)
    {
        _context = context;
        _eligibilityEngine = eligibilityEngine;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(GetCustomerAvailableOffersQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var activeOffers = await _context.Offers
            .AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.PackageTargets)
            .Include(o => o.EligibleRestaurants)
            .Where(o => o.Status == OfferStatus.Active && o.StartsAtUtc <= now && o.EndsAtUtc >= now)
            .ToListAsync(cancellationToken);

        // Seamless unification: also include any active campaigns not yet in the offers table
        var activeCampaigns = await _context.Campaigns
            .AsNoTracking()
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .Include(c => c.RestaurantEnrollments)
            .Where(c => c.Status == CampaignStatus.Active && c.StartsAtUtc <= now && c.EndsAtUtc >= now)
            .ToListAsync(cancellationToken);

        var existingCampaignIds = activeOffers.Where(o => o.CampaignId.HasValue).Select(o => o.CampaignId!.Value).ToHashSet();
        foreach (var campaign in activeCampaigns)
        {
            if (!existingCampaignIds.Contains(campaign.Id))
            {
                var virtualOffer = new Offer
                {
                    CampaignId = campaign.Id,
                    NameAr = campaign.NameAr,
                    NameEn = campaign.NameEn,
                    Status = OfferStatus.Active,
                    Type = OfferType.StandardDiscount,
                    DiscountType = DiscountType.Percentage,
                    FundingMode = (OfferFundingMode)(int)campaign.FundingMode,
                    DiscountRate = campaign.TotalDiscountRate,
                    PlatformShareRate = campaign.MealMateShareRate,
                    RestaurantShareRate = campaign.RestaurantShareRate,
                    StartsAtUtc = campaign.StartsAtUtc,
                    EndsAtUtc = campaign.EndsAtUtc,
                    TotalUsageLimit = campaign.MaxSubscribers,
                    CurrentUsageCount = campaign.CurrentSubscribers,
                    PerCustomerLimit = 1,
                    CustomerSegment = CustomerSegmentFilter.All,
                    EligiblePrograms = campaign.Programs.Select(p => new OfferProgram { ProgramId = p.ProgramId }).ToList(),
                    EligibleBundles = campaign.Bundles.Select(b => new OfferBundle { BundleId = b.BundleId }).ToList(),
                    EligibleRestaurants = campaign.RestaurantEnrollments
                        .Where(e => e.ParticipationStatus is MealMate.Domain.CollaborativeCampaigns.Enums.CampaignRestaurantParticipationStatus.Accepted or MealMate.Domain.CollaborativeCampaigns.Enums.CampaignRestaurantParticipationStatus.Locked)
                        .Select(e => new OfferRestaurant { RestaurantId = e.RestaurantId }).ToList()
                };

                activeOffers.Add(virtualOffer);
            }
        }

        if (activeOffers.Count == 0)
        {
            return Result.Success<IReadOnlyList<OfferDto>>(Array.Empty<OfferDto>());
        }

        var priorSubsCount = await _context.Subscriptions
            .CountAsync(s => s.CustomerUserId == request.CustomerId, cancellationToken);

        var customerOfferIds = activeOffers.Select(o => o.Id).ToList();
        var redemptionCounts = await _context.OfferRedemptions
            .Where(r => customerOfferIds.Contains(r.OfferId) && r.CustomerId == request.CustomerId)
            .GroupBy(r => r.OfferId)
            .Select(g => new { OfferId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.Count, cancellationToken);

        var savedOfferIds = await CustomerSavedOfferMapping.LoadSavedOfferIdsAsync(_context, request.CustomerId, cancellationToken);

        var eligibleOffers = new List<OfferDto>();

        foreach (var offer in activeOffers)
        {
            var userRedemptions = redemptionCounts.TryGetValue(offer.Id, out var count) ? count : 0;
            var ctx = new OfferEligibilityContext(
                request.CustomerId,
                priorSubsCount,
                userRedemptions,
                request.CartAmount,
                request.DurationDays,
                request.DeliveryGovernorate,
                request.ProgramId,
                request.BundleId,
                request.RestaurantId,
                Purpose: OfferEvaluationPurpose.Discovery);

            var eval = _eligibilityEngine.Evaluate(offer, ctx, now);
            if (eval.IsEligible)
            {
                var dto = CustomerSavedOfferMapping.MapCustomerOffer(offer, savedOfferIds.Contains(offer.Id));
                // A campaign without a synchronized offer is a read projection, not a new offer identity.
                if (offer.CampaignId is { } campaignId && !existingCampaignIds.Contains(campaignId))
                    dto = dto with { Id = campaignId };
                eligibleOffers.Add(dto);
            }
        }

        return Result.Success<IReadOnlyList<OfferDto>>(eligibleOffers);
    }
}
