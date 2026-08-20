using MealMate.Domain.CalendarMeals;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public interface IRestaurantDiversificationCalculator
{
    DiversificationLimitSnapshot CalculateLimit(
        Guid subscriptionId,
        int durationDays,
        int availableRestaurantsCount,
        int margin = 1);

    bool IsSelectionAllowed(int currentSelectedDaysForRestaurant, int limitValue);
}

public sealed class RestaurantDiversificationCalculator : IRestaurantDiversificationCalculator
{
    public DiversificationLimitSnapshot CalculateLimit(
        Guid subscriptionId,
        int durationDays,
        int availableRestaurantsCount,
        int margin = 1)
    {
        if (durationDays <= 0) durationDays = 1;
        if (availableRestaurantsCount <= 0) availableRestaurantsCount = 1;
        if (margin < 0) margin = 0;

        decimal rawLimit = (decimal)durationDays / availableRestaurantsCount;

        int limitValue;
        int lMax;
        int lMin = 1;

        if (availableRestaurantsCount <= 1)
        {
            limitValue = durationDays;
            lMax = durationDays;
        }
        else
        {
            var calculatedCeil = (int)Math.Ceiling(rawLimit);
            limitValue = Math.Min(durationDays, Math.Max(1, calculatedCeil + margin));
            lMax = limitValue;
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
