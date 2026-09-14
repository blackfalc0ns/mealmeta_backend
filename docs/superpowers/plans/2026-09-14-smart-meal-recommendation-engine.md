# Smart Meal Recommendation & Selection Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace naive/random meal selection with a deterministic, multi-factor smart recommendation and selection engine that eliminates repetition, balances calories and macros, and rotates restaurants dynamically.

**Architecture:** A domain-driven application service `IMealRecommendationEngine` evaluates candidate menus for daily boxes using individual meal scoring (recency decay, dislikes, dietary codes, customer affinity, ratings) and combinatorial box optimization (target Kcal deviation, keto net carb boundaries, and restaurant rotation penalty). Integrated directly into `CustomerCalendarEngine.AutoSelectFullBoxAsync`.

**Tech Stack:** .NET 9, C# 13, EF Core, FluentAssertions, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-smart-meal-recommendation-engine-design.md`

## Global Constraints
- Target Framework: .NET 9 / C# 13.
- Allergen safety is absolute: Any allergen match receives $-\infty$ and is immediately dropped.
- Recency decay penalties: $\Delta t = 1 \to -100$, $\Delta t = 2 \to -60$, $\Delta t = 3 \to -30$, $4 \le \Delta t \le 6 \to -15$, $\ge 7 \to +15$.
- Same restaurant as yesterday penalty: $-40$; consecutive 2 days: $-80$.
- Disliked ingredients penalty: $-50$; clean bonus: $+25$.
- No regression in existing `CustomerCalendarHandlerTests` or subscription flow.

---

### Task 1: Recommendation Engine Contracts & Models

**Files:**
- Create: `src/MealMate.Application/Features/CalendarMeals/MealRecommendationContracts.cs`
- Test: `tests/MealMate.UnitTests/CalendarMeals/MealRecommendationContractsTests.cs`

**Interfaces:**
- Produces:
  - `IMealRecommendationEngine`: `Task<MealRecommendationResult?> RecommendBoxForDayAsync(MealRecommendationContext context, CancellationToken ct = default)`
  - `MealRecommendationContext(Guid CustomerUserId, Guid SubscriptionId, DateOnly ServiceDate, Guid? CurrentRestaurantId, MealPreferenceFilter.CustomerPreferenceState Preferences, ProgramDailyNutritionTargetDto? NutritionTarget, IReadOnlyList<SlotRequirementDto> RequiredSlots, IReadOnlyList<ConsumptionHistoryItem> History, IReadOnlyList<CandidateRestaurantMenu> Candidates)`
  - `ConsumptionHistoryItem(DateOnly ServiceDate, Guid RestaurantId, Guid MealId, string Slot, bool WasManuallySelected)`
  - `CandidateRestaurantMenu(Guid RestaurantId, string NameAr, string NameEn, decimal Rating, int RatingCount, bool IsBusy, bool IsQuotaExhausted, int UsedDaysCount, int QuotaLimit, IReadOnlyList<MealSnapshotItem> Meals)`
  - `MealRecommendationResult(Guid RestaurantId, IReadOnlyList<MealSnapshotItem> SelectedMeals, decimal TotalKcal, decimal TotalProteinG, decimal TotalCarbsG, decimal TotalFatG, int CompatibilityScorePercentage, IReadOnlyList<string> MatchHighlights)`

- [ ] **Step 1: Write the failing unit test for contracts validation**

Create `tests/MealMate.UnitTests/CalendarMeals/MealRecommendationContractsTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCheckout;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealRecommendationContractsTests
{
    [Fact]
    public void MealRecommendationContext_Instantiates_Correctly()
    {
        var context = new MealRecommendationContext(
            CustomerUserId: Guid.NewGuid(),
            SubscriptionId: Guid.NewGuid(),
            ServiceDate: new DateOnly(2026, 9, 15),
            CurrentRestaurantId: null,
            Preferences: MealPreferenceFilter.Empty,
            NutritionTarget: null,
            RequiredSlots: [],
            History: [],
            Candidates: []);

        context.CustomerUserId.Should().NotBeEmpty();
        context.ServiceDate.Should().Be(new DateOnly(2026, 9, 15));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealRecommendationContractsTests"`
Expected: FAIL with compilation error (types not defined).

- [ ] **Step 3: Implement `MealRecommendationContracts.cs`**

Create `src/MealMate.Application/Features/CalendarMeals/MealRecommendationContracts.cs`:
```csharp
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramNutrition;

namespace MealMate.Application.Features.CalendarMeals;

public sealed record ConsumptionHistoryItem(
    DateOnly ServiceDate,
    Guid RestaurantId,
    Guid MealId,
    string Slot,
    bool WasManuallySelected);

public sealed record CandidateRestaurantMenu(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    decimal Rating,
    int RatingCount,
    bool IsBusy,
    bool IsQuotaExhausted,
    int UsedDaysCount,
    int QuotaLimit,
    IReadOnlyList<MealSnapshotItem> Meals);

public sealed record MealRecommendationContext(
    Guid CustomerUserId,
    Guid SubscriptionId,
    DateOnly ServiceDate,
    Guid? CurrentRestaurantId,
    MealPreferenceFilter.CustomerPreferenceState Preferences,
    ProgramDailyNutritionTargetDto? NutritionTarget,
    IReadOnlyList<SlotRequirementDto> RequiredSlots,
    IReadOnlyList<ConsumptionHistoryItem> History,
    IReadOnlyList<CandidateRestaurantMenu> Candidates);

public sealed record MealRecommendationResult(
    Guid RestaurantId,
    IReadOnlyList<MealSnapshotItem> SelectedMeals,
    decimal TotalKcal,
    decimal TotalProteinG,
    decimal TotalCarbsG,
    decimal TotalFatG,
    int CompatibilityScorePercentage,
    IReadOnlyList<string> MatchHighlights);

public interface IMealRecommendationEngine
{
    Task<MealRecommendationResult?> RecommendBoxForDayAsync(
        MealRecommendationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealRecommendationContractsTests"`
Expected: PASS (1 passed).

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/CalendarMeals/MealRecommendationContracts.cs tests/MealMate.UnitTests/CalendarMeals/MealRecommendationContractsTests.cs
git commit -m "feat(recommendation): define smart meal recommendation contracts"
```

---

### Task 2: Individual Meal Fitness Scorer with Recency Decay & Preference Engine

**Files:**
- Create: `src/MealMate.Application/Features/CalendarMeals/MealFitnessScorer.cs`
- Test: `tests/MealMate.UnitTests/CalendarMeals/MealFitnessScorerTests.cs`

**Interfaces:**
- Produces:
  - `MealFitnessScorer.ComputeMealScore(MealSnapshotItem meal, DateOnly serviceDate, MealPreferenceFilter.CustomerPreferenceState preferences, IReadOnlyList<ConsumptionHistoryItem> history, decimal restaurantRating)`
  - Returns `double?` (`null` if allergen conflict; otherwise numerical fitness score).

- [ ] **Step 1: Write the failing tests for `MealFitnessScorer`**

Create `tests/MealMate.UnitTests/CalendarMeals/MealFitnessScorerTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealFitnessScorerTests
{
    private readonly DateOnly _targetDate = new(2026, 9, 15);

    [Fact]
    public void ComputeMealScore_ReturnsNull_WhenAllergenPresent()
    {
        var prefs = new MealPreferenceFilter.CustomerPreferenceState(
            BlockedAllergens: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PEANUTS" },
            DislikedNameKeys: new HashSet<string>(),
            DietaryCodes: []);

        var meal = new MealSnapshotItem(
            Guid.NewGuid(), "Peanut Butter Oats", "Breakfast", 350, 15, 45, 12, 5,
            PresentAllergenCodes: ["PEANUTS"],
            MayContainAllergenCodes: [],
            Ingredients: [],
            DietaryPreferenceCodes: [],
            SortOrder: 1);

        var score = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, history: [], restaurantRating: 4.5m);

        score.Should().BeNull();
    }

    [Fact]
    public void ComputeMealScore_AppliesHeavyRecencyDecay_WhenConsumedYesterday()
    {
        var mealId = Guid.NewGuid();
        var prefs = MealPreferenceFilter.Empty;

        var meal = new MealSnapshotItem(
            mealId, "Grilled Chicken Rice", "Lunch", 500, 45, 50, 10, 4,
            PresentAllergenCodes: [],
            MayContainAllergenCodes: [],
            Ingredients: [],
            DietaryPreferenceCodes: [],
            SortOrder: 1);

        var historyYesterday = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-1), Guid.NewGuid(), mealId, "Lunch", WasManuallySelected: false)
        };

        var historyOlder = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-8), Guid.NewGuid(), mealId, "Lunch", WasManuallySelected: false)
        };

        var scoreYesterday = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, historyYesterday, restaurantRating: 4.0m);
        var scoreOlder = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, historyOlder, restaurantRating: 4.0m);

        scoreYesterday.Should().NotBeNull();
        scoreOlder.Should().NotBeNull();
        (scoreOlder!.Value - scoreYesterday!.Value).Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void ComputeMealScore_PenalizesDislikedIngredients()
    {
        var prefsClean = MealPreferenceFilter.Empty;
        var prefsDislike = new MealPreferenceFilter.CustomerPreferenceState(
            BlockedAllergens: new HashSet<string>(),
            DislikedNameKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EN:MUSHROOM" },
            DietaryCodes: []);

        var meal = new MealSnapshotItem(
            Guid.NewGuid(), "Mushroom Risotto", "Dinner", 450, 12, 65, 15, 3,
            PresentAllergenCodes: [],
            MayContainAllergenCodes: [],
            Ingredients: [new MealIngredientSnapshotItem(Guid.NewGuid(), "مشروم", "Mushroom", [])],
            DietaryPreferenceCodes: [],
            SortOrder: 1);

        var scoreClean = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefsClean, history: [], restaurantRating: 4.0m);
        var scoreDislike = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefsDislike, history: [], restaurantRating: 4.0m);

        (scoreClean!.Value - scoreDislike!.Value).Should().BeGreaterOrEqualTo(70);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealFitnessScorerTests"`
Expected: FAIL with compilation error (`MealFitnessScorer` does not exist).

- [ ] **Step 3: Implement `MealFitnessScorer.cs`**

Create `src/MealMate.Application/Features/CalendarMeals/MealFitnessScorer.cs`:
```csharp
using MealMate.Application.Features.CustomerCalendar;
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealFitnessScorerTests"`
Expected: PASS (3 passed).

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/CalendarMeals/MealFitnessScorer.cs tests/MealMate.UnitTests/CalendarMeals/MealFitnessScorerTests.cs
git commit -m "feat(recommendation): implement meal fitness scorer with recency decay"
```

---

### Task 3: Combinatorial Box Optimizer & Restaurant Rotation Engine

**Files:**
- Create: `src/MealMate.Application/Features/CalendarMeals/MealRecommendationEngine.cs`
- Test: `tests/MealMate.UnitTests/CalendarMeals/MealRecommendationEngineTests.cs`

**Interfaces:**
- Produces:
  - `MealRecommendationEngine : IMealRecommendationEngine`
- Consumes:
  - `MealFitnessScorer.ComputeMealScore`
  - `MealRecommendationContext`

- [ ] **Step 1: Write the failing tests for `MealRecommendationEngine`**

Create `tests/MealMate.UnitTests/CalendarMeals/MealRecommendationEngineTests.cs`:
```csharp
using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramNutrition;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealRecommendationEngineTests
{
    private readonly IMealRecommendationEngine _engine = new MealRecommendationEngine();
    private readonly DateOnly _targetDate = new(2026, 9, 15);

    [Fact]
    public async Task RecommendBoxForDayAsync_AvoidsYesterdayRestaurant_WhenAlternativeAvailable()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();

        var slots = new List<SlotRequirementDto> { new("Lunch", 1) };
        var lunchA = new MealSnapshotItem(Guid.NewGuid(), "Burger A", "Lunch", 600, 30, 50, 20, 2, [], [], [], [], 1);
        var lunchB = new MealSnapshotItem(Guid.NewGuid(), "Steak B", "Lunch", 600, 40, 30, 15, 2, [], [], [], [], 1);

        var candidateA = new CandidateRestaurantMenu(restA, "Rest A", "Rest A", 4.5m, 10, false, false, 2, 5, [lunchA]);
        var candidateB = new CandidateRestaurantMenu(restB, "Rest B", "Rest B", 4.5m, 10, false, false, 1, 5, [lunchB]);

        var history = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-1), restA, lunchA.MealId, "Lunch", WasManuallySelected: false)
        };

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: null,
            MealPreferenceFilter.Empty, null, slots, history, [candidateA, candidateB]);

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.RestaurantId.Should().Be(restB);
        result.SelectedMeals.Should().ContainSingle(x => x.MealId == lunchB.MealId);
    }

    [Fact]
    public async Task RecommendBoxForDayAsync_OptimizesForCalorieTarget()
    {
        var restId = Guid.NewGuid();
        var slots = new List<SlotRequirementDto> { new("Lunch", 1) };

        var mealFar = new MealSnapshotItem(Guid.NewGuid(), "Heavy Lunch", "Lunch", 950, 40, 90, 40, 3, [], [], [], [], 1);
        var mealClose = new MealSnapshotItem(Guid.NewGuid(), "Balanced Lunch", "Lunch", 520, 40, 45, 12, 4, [], [], [], [], 2);

        var candidate = new CandidateRestaurantMenu(restId, "Healthy Fit", "Healthy Fit", 4.8m, 50, false, false, 0, 10, [mealFar, mealClose]);

        var target = new ProgramDailyNutritionTargetDto(
            Guid.NewGuid(), "FIT", Guid.NewGuid(), "STD", 0, 1, 0, 0, 1,
            SuggestedKcal: 500, SuggestedProteinG: 40, SuggestedCarbsG: 45, SuggestedFatG: 12, SuggestedFiberG: 4,
            KcalMin: 450, KcalMax: 550, ProteinGMin: 35, ProteinGMax: 50, CarbsGMin: 30, CarbsGMax: 60, FatGMin: 10, FatGMax: 20,
            CarbMode: "Standard", KetoDayNetCarbsLimit: null, StandardVersion: "v1");

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: null,
            MealPreferenceFilter.Empty, target, slots, history: [], [candidate]);

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.SelectedMeals.Should().ContainSingle(x => x.MealId == mealClose.MealId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealRecommendationEngineTests"`
Expected: FAIL with compilation error (`MealRecommendationEngine` does not exist).

- [ ] **Step 3: Implement `MealRecommendationEngine.cs`**

Create `src/MealMate.Application/Features/CalendarMeals/MealRecommendationEngine.cs`:
```csharp
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
        var viableRestaurants = context.Candidates
            .Where(r => !r.IsBusy && !r.IsQuotaExhausted && r.Meals.Count > 0)
            .OrderBy(r => context.CurrentRestaurantId == r.RestaurantId ? 0 : 1)
            .ToList();

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
        var selectedMeals = new List<MealSnapshotItem>();
        double mealScoresSum = 0;

        foreach (var req in context.RequiredSlots)
        {
            var slotMeals = restaurant.Meals
                .Where(m => string.Equals(m.Slot, req.Slot, StringComparison.OrdinalIgnoreCase))
                .Where(m => selectedMeals.All(sm => sm.MealId != m.MealId))
                .Select(m => new
                {
                    Meal = m,
                    Score = MealFitnessScorer.ComputeMealScore(
                        m, context.ServiceDate, context.Preferences, context.History, restaurant.Rating)
                })
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score!.Value)
                .ThenBy(x => x.Meal.SortOrder)
                .Take(req.Count)
                .ToList();

            if (slotMeals.Count < req.Count)
            {
                // Cannot fulfill this required slot with safe meals -> disqualify restaurant
                return null;
            }

            foreach (var sm in slotMeals)
            {
                selectedMeals.Add(sm.Meal);
                mealScoresSum += sm.Score!.Value;
            }
        }

        // Restaurant rotation score
        double restaurantScore = 30.0; // baseline under cap
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

        // Macro balancing score
        double macroScore = 50.0;
        var boxKcal = selectedMeals.Sum(x => x.Calories ?? 0m);
        if (context.NutritionTarget is { SuggestedKcal: > 0 } target)
        {
            var deviation = Math.Abs((double)(boxKcal - target.SuggestedKcal));
            var targetKcal = (double)target.SuggestedKcal;
            macroScore = Math.Max(0.0, 100.0 - ((deviation / targetKcal) * 200.0));

            // Keto net carb check
            if (string.Equals(target.CarbMode, "Keto", StringComparison.OrdinalIgnoreCase) && target.KetoDayNetCarbsLimit.HasValue)
            {
                var netCarbs = selectedMeals.Sum(x => (x.CarbsG ?? 0m) - (x.FiberG ?? 0m));
                if (netCarbs > target.KetoDayNetCarbsLimit.Value)
                {
                    macroScore -= 150.0;
                }
            }
        }

        var totalScore = mealScoresSum + restaurantScore + macroScore;
        return new ScoredBoxCandidate(restaurant.RestaurantId, selectedMeals, totalScore, restaurantScore, macroScore);
    }

    private sealed record ScoredBoxCandidate(
        Guid RestaurantId,
        IReadOnlyList<MealSnapshotItem> SelectedMeals,
        double TotalScore,
        double RestaurantRotationBonus,
        double MacroFitScore);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~MealRecommendationEngineTests"`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/CalendarMeals/MealRecommendationEngine.cs tests/MealMate.UnitTests/CalendarMeals/MealRecommendationEngineTests.cs
git commit -m "feat(recommendation): implement combinatorial meal recommendation engine"
```

---

### Task 4: Integrate `IMealRecommendationEngine` with `CustomerCalendarEngine` & Dependency Injection

**Files:**
- Modify: `src/MealMate.Application/DependencyInjection.cs`
- Modify: `src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs`
- Modify: `src/MealMate.Application/Features/CalendarMeals/SmartAutoSelectionService.cs`
- Test: `tests/MealMate.UnitTests/CustomerCalendar/CustomerCalendarHandlerTests.cs`

**Interfaces:**
- Registers `IMealRecommendationEngine` in DI.
- Connects `AutoSelectFullBoxAsync` in `CustomerCalendarEngine` to call `IMealRecommendationEngine`.
- Maintains 100% backward compatibility for all existing unit tests and background jobs.

- [ ] **Step 1: Register `IMealRecommendationEngine` in `DependencyInjection.cs`**

Add `services.AddScoped<Features.CalendarMeals.IMealRecommendationEngine, Features.CalendarMeals.MealRecommendationEngine>();` and update `SmartAutoSelectionService`.

- [ ] **Step 2: Update `CustomerCalendarEngine.cs` to use `IMealRecommendationEngine`**

In `CustomerCalendarEngine`:
1. Inject `IMealRecommendationEngine`.
2. In `AutoSelectFullBoxAsync`, query past 14 days of `DailyMealSelections` and `DailyRestaurantSelections` for the subscription.
3. Build `MealRecommendationContext` containing candidates from `ctx.RestaurantDays`.
4. Call `_recommendationEngine.RecommendBoxForDayAsync(context, ct)`.
5. Apply the returned box seamlessly.

- [ ] **Step 3: Run existing CustomerCalendar unit tests**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj --filter "FullyQualifiedName~CustomerCalendar"`
Expected: ALL PASS.

- [ ] **Step 4: Run whole backend unit test suite**

Run: `dotnet test tests/MealMate.UnitTests/MealMate.UnitTests.csproj`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/DependencyInjection.cs src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs src/MealMate.Application/Features/CalendarMeals/SmartAutoSelectionService.cs
git commit -m "feat(calendar): integrate smart meal recommendation engine into calendar dispatch"
```
