using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Renewal;

public sealed record CustomerRenewalRequestDto(
    Guid RequestId,
    Guid PreviousSubscriptionId,
    string Status,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    bool IncludeFriday,
    Guid? RegionId,
    DateOnly SuggestedStartDate,
    string ConfigurationEndpoint,
    string CheckoutEndpoint);
