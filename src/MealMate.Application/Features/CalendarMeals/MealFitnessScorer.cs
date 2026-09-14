using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.Application.Features.CalendarMeals;

public static class MealFitnessScorer
{
    public static double? ComputeMealScore(
        MealSnapshotItem meal,
        DateOnly serviceDate,
        MealPreferenceFilter.CustomerPreferenceState preferences,
        IReadOnlyList<ConsumptionHistoryItem> history,
        decimal restaurantRating)
    {
        // 1. Hard-Gate Safety Filter: Allergen conflict = absolute exclusion
        if (MealPreferenceFilter.BlockReason(meal, preferences) == MealPreferenceFilter.AllergyConflict)
        {
            return null;
        }

        double score = 50.0; // Baseline neutral score

        // 2. Recency Decay Penalty (Anti-Repetition)
        var recentConsumptions = history
            .Where(x => x.MealId == meal.MealId && x.ServiceDate < serviceDate)
            .OrderByDescending(x => x.ServiceDate)
            .ToList();

        if (recentConsumptions.Count > 0)
        {
            var daysSinceLastEaten = serviceDate.DayNumber - recentConsumptions[0].ServiceDate.DayNumber;
            if (daysSinceLastEaten <= 1) score -= 100.0;
            else if (daysSinceLastEaten == 2) score -= 60.0;
            else if (daysSinceLastEaten == 3) score -= 30.0;
            else if (daysSinceLastEaten <= 6) score -= 15.0;
            else score += 15.0;

            if (recentConsumptions.Count(x => (serviceDate.DayNumber - x.ServiceDate.DayNumber) <= 7) >= 2)
            {
                score -= 40.0;
            }
        }
        else
        {
            // Fresh dish bonus
            score += 15.0;
        }

        // 3. Disliked Ingredients & Dietary Preferences
        var blockReason = MealPreferenceFilter.BlockReason(meal, preferences);
        if (blockReason == MealPreferenceFilter.IngredientPreferenceConflict)
        {
            score -= 50.0;
        }
        else
        {
            score += 25.0;
        }

        if (preferences.DietaryCodes.Count > 0)
        {
            if (blockReason == MealPreferenceFilter.DietaryPreferenceConflict)
            {
                score -= 40.0;
            }
            else
            {
                score += 20.0;
            }
        }

        // 4. Customer Affinity (Past manual selection bonus)
        if (recentConsumptions.Any(x => x.WasManuallySelected))
        {
            score += 20.0;
        }

        // 5. Quality & Rating Score
        var normalizedRating = (double)Math.Clamp(restaurantRating, 1.0m, 5.0m);
        score += (normalizedRating - 3.0) * 10.0;

        return score;
    }
}
