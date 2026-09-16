namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public sealed record AcceptedRestaurantOffer(
    Guid RestaurantId,
    int DedicatedDailyCapacity);

public sealed record CampaignOfferSnapshot(
    Guid CampaignId,
    int OffersVersion,
    DateTime CapturedAtUtc,
    IReadOnlyList<AcceptedRestaurantOffer> AcceptedOffers);
