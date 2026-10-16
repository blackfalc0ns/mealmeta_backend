using MealMate.Domain.Offers;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.Offers.Services;

public sealed record OfferEligibilityContext(
    Guid CustomerId,
    int PriorSubscriptionCount,
    int CustomerOfferRedemptionCount,
    decimal CartAmount,
    int DurationDays,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null,
    bool HasPromoCode = false,
    RestaurantTier? Tier = null,
    string? SubscriptionType = null,
    bool IsOfferPromoCode = false
);

public sealed record OfferEligibilityResult(
    bool IsEligible,
    decimal CalculatedDiscountAmount,
    IReadOnlyList<string> IneligibilityReasons
);

public interface IOfferEligibilityEngine
{
    OfferEligibilityResult Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc);
}
