using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.Offers.Services;

public sealed class CampaignOfferSyncService : ICampaignOfferSyncService
{
    private readonly IAppDbContext _context;
    private readonly ILogger<CampaignOfferSyncService>? _logger;

    public CampaignOfferSyncService(IAppDbContext context, ILogger<CampaignOfferSyncService>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Offer?> SyncCampaignToOfferAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .Include(c => c.RestaurantEnrollments)
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

        if (campaign == null)
        {
            _logger?.LogWarning("Cannot sync non-existent campaign '{CampaignId}' to Offer.", campaignId);
            return null;
        }

        return await SyncCampaignToOfferAsync(campaign, cancellationToken);
    }

    public async Task<Offer> SyncCampaignToOfferAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        var offer = await _context.Offers
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.CampaignId == campaign.Id, cancellationToken);

        var targetStatus = MapCampaignStatus(campaign.Status);
        var fundingMode = (OfferFundingMode)(int)campaign.FundingMode;

        if (offer == null)
        {
            offer = new Offer
            {
                CampaignId = campaign.Id,
                NameAr = campaign.NameAr,
                NameEn = campaign.NameEn,
                DescriptionAr = $"عرض خاص بالحملة: {campaign.NameAr}",
                DescriptionEn = $"Special offer for campaign: {campaign.NameEn}",
                Status = targetStatus,
                Type = OfferType.StandardDiscount,
                DiscountType = DiscountType.Percentage,
                FundingMode = fundingMode,
                DiscountRate = campaign.TotalDiscountRate,
                PlatformShareRate = campaign.MealMateShareRate,
                RestaurantShareRate = campaign.RestaurantShareRate,
                StartsAtUtc = campaign.StartsAtUtc,
                EndsAtUtc = campaign.EndsAtUtc,
                TotalUsageLimit = campaign.MaxSubscribers,
                CurrentUsageCount = campaign.CurrentSubscribers,
                PerCustomerLimit = 1,
                CustomerSegment = CustomerSegmentFilter.All
            };

            _context.Offers.Add(offer);
        }
        else
        {
            offer.NameAr = campaign.NameAr;
            offer.NameEn = campaign.NameEn;
            offer.Status = targetStatus;
            offer.FundingMode = fundingMode;
            offer.DiscountRate = campaign.TotalDiscountRate;
            offer.PlatformShareRate = campaign.MealMateShareRate;
            offer.RestaurantShareRate = campaign.RestaurantShareRate;
            offer.StartsAtUtc = campaign.StartsAtUtc;
            offer.EndsAtUtc = campaign.EndsAtUtc;
            offer.TotalUsageLimit = campaign.MaxSubscribers;
            offer.CurrentUsageCount = campaign.CurrentSubscribers;
        }

        // Sync Programs
        var currentProgramIds = offer.EligiblePrograms.Select(p => p.ProgramId).ToHashSet();
        var desiredProgramIds = campaign.Programs.Select(p => p.ProgramId).ToHashSet();

        var programsToRemove = offer.EligiblePrograms.Where(p => !desiredProgramIds.Contains(p.ProgramId)).ToList();
        foreach (var p in programsToRemove)
        {
            offer.EligiblePrograms.Remove(p);
        }

        foreach (var pId in desiredProgramIds.Where(id => !currentProgramIds.Contains(id)))
        {
            offer.EligiblePrograms.Add(new OfferProgram { OfferId = offer.Id, ProgramId = pId });
        }

        // Sync Bundles
        var currentBundleIds = offer.EligibleBundles.Select(b => b.BundleId).ToHashSet();
        var desiredBundleIds = campaign.Bundles.Select(b => b.BundleId).ToHashSet();

        var bundlesToRemove = offer.EligibleBundles.Where(b => !desiredBundleIds.Contains(b.BundleId)).ToList();
        foreach (var b in bundlesToRemove)
        {
            offer.EligibleBundles.Remove(b);
        }

        foreach (var bId in desiredBundleIds.Where(id => !currentBundleIds.Contains(id)))
        {
            offer.EligibleBundles.Add(new OfferBundle { OfferId = offer.Id, BundleId = bId });
        }

        // Sync Restaurants (Accepted or Locked participations)
        var participatingRestaurantIds = campaign.RestaurantEnrollments
            .Where(e => e.ParticipationStatus is CampaignRestaurantParticipationStatus.Accepted or CampaignRestaurantParticipationStatus.Locked)
            .Select(e => e.RestaurantId)
            .ToHashSet();

        var currentRestaurantIds = offer.EligibleRestaurants.Select(r => r.RestaurantId).ToHashSet();
        var restaurantsToRemove = offer.EligibleRestaurants.Where(r => !participatingRestaurantIds.Contains(r.RestaurantId)).ToList();
        foreach (var r in restaurantsToRemove)
        {
            offer.EligibleRestaurants.Remove(r);
        }

        foreach (var rId in participatingRestaurantIds.Where(id => !currentRestaurantIds.Contains(id)))
        {
            offer.EligibleRestaurants.Add(new OfferRestaurant { OfferId = offer.Id, RestaurantId = rId });
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger?.LogInformation("Successfully synced Campaign '{CampaignId}' ({CampaignName}) to Offer '{OfferId}' with Status '{Status}'.",
            campaign.Id, campaign.NameEn, offer.Id, offer.Status);

        return offer;
    }

    public async Task<int> SyncAllCampaignsAsync(CancellationToken cancellationToken = default)
    {
        var campaigns = await _context.Campaigns
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .Include(c => c.RestaurantEnrollments)
            .ToListAsync(cancellationToken);

        var synced = 0;
        foreach (var campaign in campaigns)
        {
            await SyncCampaignToOfferAsync(campaign, cancellationToken);
            synced++;
        }

        return synced;
    }

    private static OfferStatus MapCampaignStatus(CampaignStatus status) => status switch
    {
        CampaignStatus.Draft => OfferStatus.Draft,
        CampaignStatus.OpenForJoining => OfferStatus.Draft,
        CampaignStatus.JoiningClosed => OfferStatus.Draft,
        CampaignStatus.UnderReview => OfferStatus.Draft,
        CampaignStatus.NeedsChanges => OfferStatus.Draft,
        CampaignStatus.Scheduled => OfferStatus.Scheduled,
        CampaignStatus.Active => OfferStatus.Active,
        CampaignStatus.Stopped => OfferStatus.Paused,
        CampaignStatus.Ended => OfferStatus.Ended,
        CampaignStatus.Cancelled => OfferStatus.Cancelled,
        _ => OfferStatus.Draft
    };
}
