using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Offers;

namespace MealMate.Application.Features.Offers.Services;

public interface ICampaignOfferSyncService
{
    Task<Offer?> SyncCampaignToOfferAsync(Guid campaignId, CancellationToken cancellationToken = default);
    Task<Offer> SyncCampaignToOfferAsync(Campaign campaign, CancellationToken cancellationToken = default);
    Task<int> SyncAllCampaignsAsync(CancellationToken cancellationToken = default);
}
