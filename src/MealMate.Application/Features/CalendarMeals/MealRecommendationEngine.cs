using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.Application.Features.CalendarMeals;

public sealed class MealRecommendationEngine : IMealRecommendationEngine
{
    public Task<MealRecommendationResult?> RecommendBoxForDayAsync(
        MealRecommendationContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.RequiredSlots.Count == 0 || context.Candidates.Count == 0)
        {
            return Task.FromResult<MealRecommendationResult?>(null);
        }

        // Filter viable candidates: not busy, not quota exhausted, and has meals
        var viableRestaurants = context.CurrentRestaurantId.HasValue
            ? context.Candidates.Where(r => r.RestaurantId == context.CurrentRestaurantId.Value && r.Meals.Count > 0).ToList()
            : context.Candidates.Where(r => !r.IsBusy && !r.IsQuotaExhausted && r.Meals.Count > 0).ToList();

        if (viableRestaurants.Count == 0)
        {
            // Fallback to restaurants even if busy/quota if no other option exists
            viableRestaurants = context.Candidates.Where(r => r.Meals.Count > 0).ToList();
            if (viableRestaurants.Count == 0) return Task.FromResult<MealRecommendationResult?>(null);
        }

        ScoredBoxCandidate? bestOverallBox = null;

        foreach (var restaurant in viableRestaurants)
        {
            var candidateBox = BuildBestBoxForRestaurant(restaurant, context);
            if (candidateBox is null) continue;

            if (bestOverallBox is null || candidateBox.TotalScore > bestOverallBox.TotalScore)
            {
                bestOverallBox = candidateBox;
            }
        }

        if (bestOverallBox is null)
        {
            return Task.FromResult<MealRecommendationResult?>(null);
        }

        var totalKcal = bestOverallBox.SelectedMeals.Sum(x => x.Calories ?? 0m);
        var totalProtein = bestOverallBox.SelectedMeals.Sum(x => x.ProteinG ?? 0m);
        var totalCarbs = bestOverallBox.SelectedMeals.Sum(x => x.CarbsG ?? 0m);
        var totalFat = bestOverallBox.SelectedMeals.Sum(x => x.FatG ?? 0m);

        var scorePercentage = Math.Clamp((int)Math.Round(bestOverallBox.TotalScore / (context.RequiredSlots.Sum(s => s.Count) + 1)), 1, 100);

        var highlights = new List<string>();
        if (bestOverallBox.RestaurantRotationBonus > 0) highlights.Add("Cuisine & Restaurant Rotation");
        if (bestOverallBox.MacroFitScore >= 70) highlights.Add("Target Calories Matched");
        if (bestOverallBox.SelectedMeals.All(m => MealPreferenceFilter.BlockReason(m, context.Preferences) is null))
        {
            highlights.Add("Free of Disliked Foods");
        }
        if (bestOverallBox.SelectedMeals.Any(m => ProteinTypeClassifier.Classify(m) != ProteinTypeClassifier.Other))
        {
            highlights.Add("Balanced Protein Diversity");
        }
        if (context.NutritionTarget is { SuggestedProteinG: > 0 } pt && pt.SuggestedProteinG > 0)
        {
            var proteinDevRatio = Math.Abs((double)(totalProtein - pt.SuggestedProteinG)) / (double)pt.SuggestedProteinG;
            if (proteinDevRatio <= 0.15)
            {
                highlights.Add("Meets Target Protein Goal");
            }
        }

        var result = new MealRecommendationResult(
            bestOverallBox.RestaurantId,
            bestOverallBox.SelectedMeals,
            totalKcal,
            totalProtein,
            totalCarbs,
            totalFat,
            scorePercentage,
            highlights);

        return Task.FromResult<MealRecommendationResult?>(result);
    }

    private static ScoredBoxCandidate? BuildBestBoxForRestaurant(
        CandidateRestaurantMenu restaurant,
        MealRecommendationContext context)
    {
        var slotCandidateLists = new List<List<(MealSnapshotItem Meal, double Score)>>();
        var remainingLockedIds = context.LockedMealIds is not null
            ? new HashSet<Guid>(context.LockedMealIds)
            : null;

        foreach (var req in context.RequiredSlots)
        {
            for (var i = 0; i < req.Count; i++)
            {
                var lockedMeal = remainingLockedIds is not null
                    ? restaurant.Meals.FirstOrDefault(m => remainingLockedIds.Contains(m.MealId) && string.Equals(m.Slot, req.Slot, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (lockedMeal is not null)
                {
                    remainingLockedIds!.Remove(lockedMeal.MealId);
                    var score = MealFitnessScorer.ComputeMealScore(lockedMeal, context.ServiceDate, context.Preferences, context.History, restaurant.Rating, context.RejectedMealIds) ?? 50.0;
                    slotCandidateLists.Add([(lockedMeal, score)]);
                    continue;
                }

                var viable = restaurant.Meals
                    .Where(m => string.Equals(m.Slot, req.Slot, StringComparison.OrdinalIgnoreCase))
                    .Select(m => (Meal: m, Score: MealFitnessScorer.ComputeMealScore(m, context.ServiceDate, context.Preferences, context.History, restaurant.Rating, context.RejectedMealIds)))
                    .Where(x => x.Score is not null)
                    .OrderByDescending(x => x.Score!.Value)
                    .ThenBy(x => x.Meal.SortOrder)
                    .Take(Math.Max(4, req.Count * 2))
                    .Select(x => (x.Meal, Score: x.Score!.Value))
                    .ToList();

                if (viable.Count == 0)
                {
                    return null;
                }

                slotCandidateLists.Add(viable);
            }
        }

        // Restaurant rotation score
        double restaurantScore = 30.0;
        var yesterdayRestaurant = context.History
            .Where(x => x.ServiceDate == context.ServiceDate.AddDays(-1))
            .Select(x => x.RestaurantId)
            .FirstOrDefault();

        if (yesterdayRestaurant != Guid.Empty && yesterdayRestaurant == restaurant.RestaurantId)
        {
            restaurantScore -= 40.0;

            var twoDaysAgoRestaurant = context.History
                .Where(x => x.ServiceDate == context.ServiceDate.AddDays(-2))
                .Select(x => x.RestaurantId)
                .FirstOrDefault();

            if (twoDaysAgoRestaurant == restaurant.RestaurantId)
            {
                restaurantScore -= 40.0;
            }
        }

        // Generate combinations
        var combinations = GenerateCombinations(slotCandidateLists);
        if (combinations.Count == 0) return null;

        ScoredBoxCandidate? best = null;

        foreach (var combo in combinations)
        {
            var selectedMeals = combo.Select(x => x.Meal).ToList();
            var mealScoresSum = combo.Sum(x => x.Score);

            double macroScore = 50.0;
            var boxKcal = selectedMeals.Sum(x => x.Calories ?? 0m);
            if (context.NutritionTarget is { SuggestedKcal: > 0 } target)
            {
                var deviation = Math.Abs((double)(boxKcal - target.SuggestedKcal));
                var targetKcal = (double)target.SuggestedKcal;
                macroScore = Math.Max(0.0, 100.0 - ((deviation / targetKcal) * 200.0));

                if (string.Equals(target.CarbMode, "Keto", StringComparison.OrdinalIgnoreCase) && target.KetoDayNetCarbsLimit.HasValue)
                {
                    var netCarbs = selectedMeals.Sum(x => x.CarbsG ?? 0m);
                    if (netCarbs > target.KetoDayNetCarbsLimit.Value)
                    {
                        macroScore -= 150.0;
                    }
                }

                if (target.SuggestedProteinG > 0)
                {
                    var boxProtein = selectedMeals.Sum(x => x.ProteinG ?? 0m);
                    var proteinDeviation = Math.Abs((double)(boxProtein - target.SuggestedProteinG));
                    var proteinRatio = proteinDeviation / (double)target.SuggestedProteinG;
                    if (proteinRatio <= 0.15)
                    {
                        macroScore += 15.0;
                    }
                    else if (proteinRatio > 0.4)
                    {
                        macroScore -= 15.0;
                    }
                }
            }

            var totalScore = mealScoresSum + restaurantScore + macroScore;
            if (best is null || totalScore > best.TotalScore)
            {
                best = new ScoredBoxCandidate(restaurant.RestaurantId, selectedMeals, totalScore, restaurantScore, macroScore);
            }
        }

        return best;
    }

    private static List<List<(MealSnapshotItem Meal, double Score)>> GenerateCombinations(
        List<List<(MealSnapshotItem Meal, double Score)>> lists)
    {
        var results = new List<List<(MealSnapshotItem Meal, double Score)>> { new() };

        foreach (var list in lists)
        {
            var next = new List<List<(MealSnapshotItem Meal, double Score)>>();
            foreach (var existing in results)
            {
                foreach (var item in list)
                {
                    if (list.Any(candidate => existing.All(x => x.Meal.MealId != candidate.Meal.MealId))
                        && existing.Any(x => x.Meal.MealId == item.Meal.MealId))
                    {
                        continue;
                    }
                    var clone = new List<(MealSnapshotItem Meal, double Score)>(existing) { item };
                    next.Add(clone);
                }
            }

            if (next.Count > 100)
            {
                results = next.OrderByDescending(x => x.Sum(m => m.Score)).Take(50).ToList();
            }
            else
            {
                results = next;
            }
        }

        return results;
    }

    private sealed record ScoredBoxCandidate(
        Guid RestaurantId,
        IReadOnlyList<MealSnapshotItem> SelectedMeals,
        double TotalScore,
        double RestaurantRotationBonus,
        double MacroFitScore);
}
