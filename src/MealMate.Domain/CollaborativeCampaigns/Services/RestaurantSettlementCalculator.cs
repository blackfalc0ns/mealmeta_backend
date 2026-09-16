using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.Services;

public sealed class RestaurantSettlementCalculator
{
    private const int Precision = 3;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public RestaurantSettlementResult CalculateSettlement(
        Money restaurantBasePrice,
        Money referenceCustomerPrice,
        Percentage restaurantContributionPct,
        Percentage commissionRate)
    {
        if (restaurantBasePrice is null || restaurantBasePrice.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(restaurantBasePrice), "Restaurant base price must be non-negative.");
        }

        if (referenceCustomerPrice is null || referenceCustomerPrice.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceCustomerPrice), "Reference customer price must be non-negative.");
        }

        if (commissionRate is null || commissionRate.Value < 0m || commissionRate.Value > 1.0m)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionRate), "Commission rate must be between 0% and 100%.");
        }

        if (restaurantContributionPct is null || restaurantContributionPct.Value < 0m || restaurantContributionPct.Value > 1.0m)
        {
            throw new ArgumentOutOfRangeException(nameof(restaurantContributionPct), "Restaurant contribution percentage must be between 0% and 100%.");
        }

        var currency = restaurantBasePrice.CurrencyCode;
        if (currency != referenceCustomerPrice.CurrencyCode)
        {
            throw new InvalidOperationException("Currency mismatch between restaurant base price and reference customer price.");
        }

        var baseAmount = restaurantBasePrice.Amount;
        var refAmount = referenceCustomerPrice.Amount;

        var concessionAmount = Math.Round(baseAmount * restaurantContributionPct.Value, Precision, Rounding);
        var grossSettlement = baseAmount - concessionAmount;
        var commissionAmount = Math.Round(grossSettlement * commissionRate.Value, Precision, Rounding);
        var netPayout = grossSettlement - commissionAmount;

        if (netPayout < 0m)
        {
            throw new InvalidOperationException("Negative restaurant settlement is strictly prohibited.");
        }

        var wholesaleSpreadAmount = refAmount - baseAmount;

        return new RestaurantSettlementResult(
            restaurantBasePrice,
            referenceCustomerPrice,
            restaurantContributionPct,
            commissionRate,
            new Money(concessionAmount, currency),
            new Money(grossSettlement, currency),
            new Money(commissionAmount, currency),
            new Money(netPayout, currency),
            new Money(wholesaleSpreadAmount, currency));
    }
}
