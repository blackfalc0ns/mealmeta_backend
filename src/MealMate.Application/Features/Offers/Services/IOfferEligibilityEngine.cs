using MealMate.Domain.Offers;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.Offers.Services;

public enum OfferEvaluationPurpose
{
    Checkout = 0,
    Discovery = 1
}

public sealed record OfferEligibilityContext(
    Guid CustomerId,
    int PriorSubscriptionCount,
    int CustomerOfferRedemptionCount,
    decimal CartAmount = 0m,
    int DurationDays = 0,
    string? DeliveryGovernorate = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    Guid? RestaurantId = null,
    bool HasPromoCode = false,
    RestaurantTier? Tier = null,
    string? SubscriptionType = null,
    bool IsOfferPromoCode = false,
    OfferEvaluationPurpose Purpose = OfferEvaluationPurpose.Checkout
);

public sealed record OfferEligibilityResult(
    bool IsEligible,
    decimal CalculatedDiscountAmount,
    IReadOnlyList<string> IneligibilityReasons,
    bool IsScopeUnresolved = false
);

public interface IOfferEligibilityEngine
{
    OfferEligibilityResult Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc);
}
