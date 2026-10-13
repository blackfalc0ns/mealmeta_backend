using MealMate.Domain.CalendarMeals;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public interface IRestaurantDiversificationCalculator
{
    DiversificationLimitSnapshot CalculateLimit(
        Guid subscriptionId,
        int durationDays,
        int availableRestaurantsCount,
        int adminCeiling = 8);

    bool IsSelectionAllowed(int currentSelectedDaysForRestaurant, int limitValue);
}

public sealed class RestaurantDiversificationCalculator : IRestaurantDiversificationCalculator
{
    public const int HardMinLimit = 3;
    public const int DefaultAdminCeiling = 8;

    public DiversificationLimitSnapshot CalculateLimit(
        Guid subscriptionId,
        int durationDays,
        int availableRestaurantsCount,
        int adminCeiling = DefaultAdminCeiling)
    {
        if (durationDays <= 0) durationDays = 1;
        if (availableRestaurantsCount <= 0) availableRestaurantsCount = 1;
        if (adminCeiling < HardMinLimit) adminCeiling = HardMinLimit;

        var rawLimit = (decimal)durationDays / availableRestaurantsCount;
        int limitValue;
        int lMax;
        const int lMin = HardMinLimit;

        if (availableRestaurantsCount <= 1)
        {
            limitValue = durationDays;
            lMax = durationDays;
        }
        else
        {
            var computed = Math.Max(1, (int)Math.Ceiling(rawLimit));
            // A configured ceiling must not make the full service quota impossible.
            // Use the rounded-up average when fewer restaurants need more days each.
            lMax = Math.Max(adminCeiling, computed);
            limitValue = Math.Clamp(computed, HardMinLimit, lMax);
        }

        return new DiversificationLimitSnapshot
        {
            SubscriptionId = subscriptionId,
            DurationDays = durationDays,
            NSnapshot = availableRestaurantsCount,
            RawLimit = Math.Round(rawLimit, 4),
            LMin = lMin,
            LMax = lMax,
            LimitValue = limitValue,
            CapturedAtUtc = DateTime.UtcNow
        };
    }

    public bool IsSelectionAllowed(int currentSelectedDaysForRestaurant, int limitValue)
    {
        if (limitValue <= 0) return true;
        return currentSelectedDaysForRestaurant < limitValue;
    }
}
