using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.Application.Features.CalendarMeals;

public static class ProteinTypeClassifier
{
    public const string Poultry = "poultry";
    public const string RedMeat = "red_meat";
    public const string Seafood = "seafood";
    public const string Vegetarian = "vegetarian";
    public const string Egg = "egg";
    public const string Other = "other";

    public static string Classify(MealSnapshotItem meal)
    {
        var text = $"{meal.NameEn} {meal.NameAr} " +
                   string.Join(" ", meal.Ingredients.Select(i => $"{i.NameEn} {i.NameAr}"));

        if (ContainsAny(text, "chicken", "turkey", "دجاج", "حبش", "رومي", "فراخ", "شيش طاووق")) return Poultry;
        if (ContainsAny(text, "beef", "steak", "lamb", "meat", "veal", "لحم", "كفتة", "ستيك", "شاورما لحم", "كباب", "برجر")) return RedMeat;
        if (ContainsAny(text, "fish", "salmon", "shrimp", "tuna", "prawn", "seafood", "سمك", "سلمون", "جمبري", "روبيان", "تونة", "بحري", "سي فود")) return Seafood;
        if (ContainsAny(text, "egg", "omelette", "بيض", "اومليت", "أومليت")) return Egg;
        if (ContainsAny(text, "halloumi", "falafel", "tofu", "vegan", "vegetarian", "حلوم", "فلافل", "نباتي", "مشروم", "خضار")) return Vegetarian;

        return Other;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public static class MealFitnessScorer
{
    public static double? ComputeMealScore(
        MealSnapshotItem meal,
        DateOnly serviceDate,
        MealPreferenceFilter.CustomerPreferenceState preferences,
        IReadOnlyList<ConsumptionHistoryItem> history,
        decimal restaurantRating,
        IReadOnlySet<Guid>? rejectedMealIds = null)
    {
        // 1. Hard-Gate Safety Filter: Allergen conflict = absolute exclusion
        if (MealPreferenceFilter.BlockReason(meal, preferences) == MealPreferenceFilter.AllergyConflict)
        {
            return null;
        }

        double score = 50.0; // Baseline neutral score

        // 1.b Customer Rejected Meals Penalty (from MealChangeRequests)
        if (rejectedMealIds is not null && rejectedMealIds.Contains(meal.MealId))
        {
            score -= 60.0;
        }

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

        // 2.b Protein Fatigue / Diversity Check
        var mealProtein = ProteinTypeClassifier.Classify(meal);
        if (mealProtein != ProteinTypeClassifier.Other)
        {
            var yesterdaySameSlot = history
                .Where(x => x.ServiceDate == serviceDate.AddDays(-1) && (string.Equals(x.Slot, meal.Slot, StringComparison.OrdinalIgnoreCase) || IsMainSlot(meal.Slot)))
                .ToList();

            if (yesterdaySameSlot.Any(x => string.Equals(x.ProteinType, mealProtein, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 35.0;

                var twoDaysAgoSameSlot = history
                    .Where(x => x.ServiceDate == serviceDate.AddDays(-2) && (string.Equals(x.Slot, meal.Slot, StringComparison.OrdinalIgnoreCase) || IsMainSlot(meal.Slot)))
                    .ToList();

                if (twoDaysAgoSameSlot.Any(x => string.Equals(x.ProteinType, mealProtein, StringComparison.OrdinalIgnoreCase)))
                {
                    score -= 35.0;
                }
            }
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

    private static bool IsMainSlot(string slot)
        => slot.Equals("lunch", StringComparison.OrdinalIgnoreCase)
           || slot.Equals("dinner", StringComparison.OrdinalIgnoreCase);
}
