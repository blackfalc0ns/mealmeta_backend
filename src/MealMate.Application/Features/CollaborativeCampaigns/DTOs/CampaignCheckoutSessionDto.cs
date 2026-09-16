namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

public sealed record CampaignReservationAllocationDto(
    DateOnly ServiceDate,
    Guid RestaurantId,
    int Units);

public sealed record CampaignCheckoutSessionDto(
    Guid SessionId,
    Guid CampaignId,
    Guid BundleId,
    Guid CustomerId,
    int DurationDays,
    decimal FinalCustomerPrice,
    string Currency,
    DateTime ExpiresAtUtc,
    string Status,
    IReadOnlyList<CampaignReservationAllocationDto> Allocations);
