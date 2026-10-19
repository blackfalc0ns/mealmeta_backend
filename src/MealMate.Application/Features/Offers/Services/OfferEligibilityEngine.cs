using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using System.Text.Json;

namespace MealMate.Application.Features.Offers.Services;

public sealed class OfferEligibilityEngine : IOfferEligibilityEngine
{
    public OfferEligibilityResult Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc)
    {
        var reasons = new List<string>();

        // 1. Status Check
        if (offer.Status != OfferStatus.Active)
        {
            reasons.Add($"Offer is not active (status is '{offer.Status}').");
        }

        // 2. Dates Check
        if (evaluationTimeUtc < offer.StartsAtUtc || evaluationTimeUtc > offer.EndsAtUtc)
        {
            reasons.Add("Current time is outside the offer validity period.");
        }

        // 3. Customer Segment Check
        if (offer.CustomerSegment == CustomerSegmentFilter.NewCustomersOnly && context.PriorSubscriptionCount > 0)
        {
            reasons.Add("Offer is restricted to new customers only.");
        }
        else if (offer.CustomerSegment == CustomerSegmentFilter.ExistingCustomersOnly && context.PriorSubscriptionCount == 0)
        {
            reasons.Add("Offer is restricted to existing subscribers only.");
        }

        // 4. Per Customer Usage Limit
        if (context.CustomerOfferRedemptionCount >= offer.PerCustomerLimit)
        {
            reasons.Add($"Customer has reached redemption limit ({offer.PerCustomerLimit}) for this offer.");
        }

        // 5. Total Usage Limit
        if (offer.TotalUsageLimit.HasValue && offer.CurrentUsageCount >= offer.TotalUsageLimit.Value)
        {
            reasons.Add("Offer has reached its maximum total redemptions.");
        }

        // 6. Minimum Order Amount Check (enforced when cart amount is provided)
        if (context.CartAmount > 0 && offer.MinOrderAmount != null && context.CartAmount < offer.MinOrderAmount.Amount)
        {
            reasons.Add($"Cart amount ({context.CartAmount} KWD) is below the minimum required ({offer.MinOrderAmount.Amount} KWD).");
        }

        // 7. Duration Bounds (enforced when duration is provided)
        if (context.DurationDays > 0)
        {
            if (offer.MinSubscriptionDays.HasValue && context.DurationDays < offer.MinSubscriptionDays.Value)
            {
                reasons.Add($"Subscription duration ({context.DurationDays} days) is less than the required minimum ({offer.MinSubscriptionDays.Value} days).");
            }
            if (offer.MaxSubscriptionDays.HasValue && context.DurationDays > offer.MaxSubscriptionDays.Value)
            {
                reasons.Add($"Subscription duration ({context.DurationDays} days) exceeds maximum allowed ({offer.MaxSubscriptionDays.Value} days).");
            }
        }

        // 8. Geographic Area Check
        if (!string.IsNullOrWhiteSpace(offer.AllowedGovernoratesJson) && !string.IsNullOrWhiteSpace(context.DeliveryGovernorate))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(offer.AllowedGovernoratesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(context.DeliveryGovernorate, StringComparer.OrdinalIgnoreCase))
                {
                    reasons.Add($"Delivery governorate '{context.DeliveryGovernorate}' is outside the offer scope.");
                }
            }
            catch
            {
                // Fallback safe ignore
            }
        }

        var isScopeUnresolved = false;
        if (context.Purpose == OfferEvaluationPurpose.Discovery)
        {
            var isFullyResolved = context.ProgramId.HasValue
                && context.BundleId.HasValue
                && context.Tier.HasValue
                && !string.IsNullOrWhiteSpace(context.SubscriptionType)
                && context.DurationDays > 0;
            if (!isFullyResolved)
            {
                isScopeUnresolved = true;
            }
        }

        // 9. Program Scope Check
        if (offer.PackageTargets.Count > 0)
        {
            if (context.Purpose == OfferEvaluationPurpose.Discovery)
            {
                var candidateTargets = offer.PackageTargets.AsEnumerable();

                if (context.ProgramId.HasValue)
                {
                    candidateTargets = candidateTargets.Where(t => t.ProgramId == context.ProgramId.Value);
                }

                if (context.BundleId.HasValue)
                {
                    candidateTargets = candidateTargets.Where(t => t.BundleId == context.BundleId.Value);
                }

                if (context.Tier.HasValue)
                {
                    candidateTargets = candidateTargets.Where(t => t.Tier == context.Tier.Value);
                }

                if (!string.IsNullOrWhiteSpace(context.SubscriptionType))
                {
                    candidateTargets = candidateTargets.Where(t => string.Equals(t.SubscriptionType, context.SubscriptionType, StringComparison.OrdinalIgnoreCase));
                }

                if (context.DurationDays > 0)
                {
                    candidateTargets = candidateTargets.Where(t => t.AllowedDurationDays.Contains(context.DurationDays));
                }

                var matchingTargets = candidateTargets.ToList();
                if (matchingTargets.Count == 0)
                {
                    reasons.Add("Offer does not match the selected package target (subscription type, program, bundle, tier and duration).");
                }
            }
            else
            {
                var packageMatch = context.ProgramId.HasValue && context.BundleId.HasValue && context.Tier.HasValue
                    && !string.IsNullOrWhiteSpace(context.SubscriptionType)
                    && offer.PackageTargets.Any(target =>
                        target.ProgramId == context.ProgramId.Value
                        && target.BundleId == context.BundleId.Value
                        && target.Tier == context.Tier.Value
                        && string.Equals(target.SubscriptionType, context.SubscriptionType, StringComparison.OrdinalIgnoreCase)
                        && target.AllowedDurationDays.Contains(context.DurationDays));
                if (!packageMatch)
                {
                    reasons.Add("Offer does not match the selected package target (subscription type, program, bundle, tier and duration).");
                }
            }
        }

        if (offer.PackageTargets.Count == 0 && offer.EligiblePrograms.Count > 0 && context.ProgramId.HasValue &&
            !offer.EligiblePrograms.Any(p => p.ProgramId == context.ProgramId.Value))
        {
            reasons.Add("Offer is not applicable to the selected program.");
        }

        // 10. Bundle Scope Check
        if (offer.PackageTargets.Count == 0 && offer.EligibleBundles.Count > 0 && context.BundleId.HasValue &&
            !offer.EligibleBundles.Any(b => b.BundleId == context.BundleId.Value))
        {
            reasons.Add("Offer is not applicable to the selected bundle.");
        }

        // 11. Restaurant Scope Check
        if (offer.EligibleRestaurants.Count > 0 && context.RestaurantId.HasValue &&
            !offer.EligibleRestaurants.Any(r => r.RestaurantId == context.RestaurantId.Value))
        {
            reasons.Add("Offer is not applicable to the selected restaurant.");
        }

        // 12. Promo Code Stacking Check
        if (context.HasPromoCode && !offer.AllowPromoCodeStacking)
        {
            reasons.Add("Offer does not allow stacking with additional promo codes.");
        }

        if (reasons.Count > 0)
        {
            return new OfferEligibilityResult(false, 0m, reasons);
        }

        // Calculate Discount
        decimal discount = 0m;
        if (offer.DiscountType == DiscountType.Percentage && offer.DiscountRate != null)
        {
            discount = Math.Round(context.CartAmount * offer.DiscountRate.Value, 3, MidpointRounding.AwayFromZero);
        }
        else if (offer.DiscountType == DiscountType.FixedAmount && offer.DiscountAmount != null)
        {
            discount = Math.Min(context.CartAmount, Math.Round(offer.DiscountAmount.Amount, 3, MidpointRounding.AwayFromZero));
        }

        return new OfferEligibilityResult(true, discount, Array.Empty<string>(), isScopeUnresolved);
    }
}
