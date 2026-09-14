# Design Spec: Smart Meal Recommendation & Selection Engine

**Date:** 2026-09-14  
**Target Project:** MealMate Backend  
**Status:** Approved for Implementation Planning  

---

## 1. Objective & Scope

The objective is to replace the existing naive, single-order meal selection mechanism (`CustomerCalendarEngine.TryPickFullBox` / `BuildFullBox` and dummy `SmartAutoSelectionService`) with a high-performance, deterministic, multi-criteria recommendation and selection engine.

Key goals:
- **Zero Hallucination / Deterministic**: No random picks; every selection is mathematically scored based on quantifiable user goals and history.
- **Anti-Repetition & Variety (Recency Decay)**: Eliminate monotony by enforcing dynamic penalties on recently consumed dishes and restaurants across 7–14 days.
- **Nutritional & Macro Matching**: Dynamically compose the day's box to align with `ProgramNutritionStandards` (Kcal targets, protein thresholds, and Keto net carbs).
- **Personalized Safety & Preference**: Strict hard-gate allergen exclusion, heavy penalty on customer disliked ingredients, and positive weighting for dietary patterns (Keto, Low Carb, Vegetarian) and past manual choices.
- **Restaurant Diversification**: Encourage culinary variety by rotating eligible restaurants across consecutive days while respecting delivery region coverage and monthly caps.

---

## 2. Architecture & Component Boundaries

### 2.1 Service Interface
A clean, decoupled service placed in `MealMate.Application.Features.CalendarMeals`:

```csharp
public interface IMealRecommendationEngine
{
    Task<MealRecommendationResult> RecommendBoxForDayAsync(
        MealRecommendationContext context,
        CancellationToken cancellationToken = default);
}
```

### 2.2 Domain Context & Contracts
```csharp
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

public sealed record MealRecommendationResult(
    Guid RestaurantId,
    IReadOnlyList<MealSnapshotItem> SelectedMeals,
    decimal TotalKcal,
    decimal TotalProteinG,
    decimal TotalCarbsG,
    decimal TotalFatG,
    int CompatibilityScorePercentage,
    IReadOnlyList<string> MatchHighlights);
```

---

## 3. Mathematical Scoring Model

The recommendation engine evaluates candidate boxes $B = \{m_1, m_2, \dots, m_k\}$ satisfying the day's required slots.

### 3.1 Hard Safety Gate (Pre-Filter)
- Any meal with:
  $$\text{Allergens}(m) \cap \text{Customer.BlockedAllergens} \ne \emptyset \implies S_{\text{meal}} = -\infty$$
  These meals are completely dropped before evaluation.
- Any restaurant that cannot fulfill all required slots with safe meals is disqualified for the day.

### 3.2 Individual Meal Score ($S_{\text{meal}}$)
For each safe meal $m$, the fitness score is computed as:
$$S_{\text{meal}}(m) = D_{\text{recency}}(m) + P_{\text{preference}}(m) + A_{\text{affinity}}(m) + R_{\text{quality}}(m)$$

#### A) Recency Decay Penalty ($D_{\text{recency}}$)
Let $\Delta t$ be the number of days since meal $m$ was last consumed:
- $\Delta t = 1$ (Yesterday): **$-100$ points**
- $\Delta t = 2$ (2 days ago): **$-60$ points**
- $\Delta t = 3$ (3 days ago): **$-30$ points**
- $4 \le \Delta t \le 6$: **$-15$ points**
- $\Delta t \ge 7$ or never consumed: **$+15$ points** (Novelty bonus)

#### B) Customer Dislikes & Dietary Preferences ($P_{\text{preference}}$)
- No disliked ingredients: **$+25$ points**
- Contains disliked ingredient: **$-50$ points**
- Matches customer dietary code (e.g. Keto, Low Carb, Halal): **$+20$ points**
- Violates customer dietary code: **$-40$ points**

#### C) Customer Affinity ($A_{\text{affinity}}$)
- Manually chosen by customer in the past 30 days: **$+20$ points**

#### D) Quality & Rating ($R_{\text{quality}}$)
- Meal/Restaurant Rating $R \in [1.0, 5.0]$:
  $$R_{\text{quality}} = (R - 3.0) \times 10$$
  *(e.g., 4.8 rating $\to +18$ points; 3.0 rating $\to 0$ points)*

---

### 3.3 Box Combinatorial Macro Balancing ($Score_{\text{box}}$)
For each combination of meals completing the required slots:

1. **Calorie Deviation:**
   Let $Kcal_{\text{total}} = \sum_{m \in B} Kcal(m)$, and $Kcal_{\text{target}}$ be the bundle/program target:
   $$\Delta_{\text{kcal}} = |Kcal_{\text{total}} - Kcal_{\text{target}}|$$
   $$Score_{\text{kcal}} = \max\left(0, 100 - \left(\frac{\Delta_{\text{kcal}}}{Kcal_{\text{target}}} \times 200\right)\right)$$

2. **Keto Net Carbs Constraint:**
   If the program is Keto and $\text{NetCarbs}_{\text{total}} > \text{KetoDayNetCarbsLimit}$:
   $$Penalty_{\text{keto}} = -150 \text{ points}$$

---

### 3.4 Restaurant Rotation Score ($Score_{\text{restaurant}}$)
- Same restaurant as yesterday ($\Delta t_{\text{rest}} = 1$): **$-40$ points**
- Same restaurant 2 consecutive days: **$-80$ points**
- Under quota and not busy: **$+30$ points**

### 3.5 Total Day Fitness Score
$$\text{Fitness}(B) = Score_{\text{restaurant}} + Score_{\text{kcal}} + Penalty_{\text{keto}} + \sum_{m \in B} S_{\text{meal}}(m)$$

The combination with the highest $\text{Fitness}(B)$ is chosen as the winner.

---

## 4. Integration with Calendar & Dispatch

- **Integration Point**: [`CustomerCalendarEngine.cs`](file:///f:/MealMate/backend/src/MealMate.Application/Features/CustomerCalendar/CustomerCalendarEngine.cs) invokes `IMealRecommendationEngine.RecommendBoxForDayAsync(...)` inside `AutoSelectFullBoxAsync`.
- **Consumption History Retrieval**: Queries `DailyMealSelections` and `DailyRestaurantSelections` for the subscription for the prior 14 days.
- **Nutrition Targets**: Queries `ProgramNutritionStandards` using `Subscription.ProgramId` and `Subscription.BundleId`.
- **Clean Replacement**: The legacy 68-line dummy `SmartAutoSelectionService.cs` is replaced and superseded by `MealRecommendationEngine.cs`.

---

## 5. Verification Plan

### Automated Unit Tests (`tests/MealMate.UnitTests/CalendarMeals/MealRecommendationEngineTests.cs`)
1. **Safety Test**: Meals with user allergens receive $-\infty$ and are never selected under any circumstances.
2. **Anti-Repetition Test**: Given 2 safe meals for Lunch, the engine strictly avoids yesterday's lunch meal ($\Delta t = 1$) in favor of the fresh alternative.
3. **Restaurant Rotation Test**: Given 2 restaurants under cap, the engine prefers switching to the alternative restaurant rather than repeating yesterday's restaurant.
4. **Macro Fitness Test**: Selects the meal combination that achieves the closest calorie total to the program's target.
5. **Dislike Avoidance Test**: Penalizes meals containing disliked ingredients and prefers clean alternatives.
6. **Cold Start Test**: Handles a new subscriber with empty history gracefully by favoring top-rated meals.
7. **CustomerCalendarEngine E2E Integration**: Existing tests in `CustomerCalendarHandlerTests.cs` continue to pass without regression.
