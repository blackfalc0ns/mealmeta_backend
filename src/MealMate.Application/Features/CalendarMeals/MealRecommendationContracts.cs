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
