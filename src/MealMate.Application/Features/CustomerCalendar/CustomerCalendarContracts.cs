using MealMate.Application.Common.Messaging;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed record CustomerMealIngredientDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? OriginCountryCode,
    string? SupplierName,
    bool IsLocal,
    bool IsHalalCertified,
    IReadOnlyList<string> AllergenCodes,
    bool IsCore = false,
    string? IconUrl = null);

public static class CustomerCalendarSettings
{
    public const string RepetitionCeilingKey = "calendar.restaurant_repetition_ceiling";
    public const int HardMinLimit = 3;
    public const int DefaultAdminCeiling = 8;
}

public static class CustomerCalendarUiStatus
{
    public const string Empty = "empty";
    public const string Drafting = "drafting";
    public const string AwaitingRestaurant = "awaiting_restaurant";
    public const string AutoSelected = "auto_selected";
    public const string Locked = "locked";
    public const string Frozen = "frozen";
    public const string Cancelled = "cancelled";
    public const string ReplacementRequired = "replacement_required";
}

public static class CustomerCalendarSlotLabels
{
    public static string NameAr(string slot) => slot.Trim().ToLowerInvariant() switch
    {
        "breakfast" => "الإفطار",
        "lunch" => "الغداء",
        "dinner" => "العشاء",
        "snack" => "سناك",
        _ => slot
    };

    public static string NameEn(string slot) => slot.Trim().ToLowerInvariant() switch
    {
        "breakfast" => "Breakfast",
        "lunch" => "Lunch",
        "dinner" => "Dinner",
        "snack" => "Snack",
        _ => slot
    };

    public static CustomerCalendarSlotDto ToDto(SlotRequirementDto slot)
        => new(slot.Slot, NameAr(slot.Slot), NameEn(slot.Slot), slot.Count);

    public static IReadOnlyList<CustomerCalendarSlotDto> ToDtos(IEnumerable<SlotRequirementDto> slots)
        => slots.Select(ToDto).ToList();
}

public sealed record CustomerCalendarSlotDto(
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    int Count);

public sealed record CustomerCalendarMealSummaryDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    int SortOrder,
    string? ImageUrl,
    bool IsSelected = false);

public sealed record CustomerCalendarMealAllergenDto(
    string Code,
    string NameAr,
    string NameEn);

public sealed record CustomerCalendarMealDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    int SortOrder,
    string? ImageUrl,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<string> PresentAllergenCodes,
    IReadOnlyList<string> MayContainAllergenCodes,
    IReadOnlyList<string> DietaryPreferenceCodes,
    IReadOnlyList<CustomerMealIngredientDto> Ingredients,
    string? BlockReason,
    bool IsSelected = false,
    decimal? ProteinPercentage = null,
    decimal? CarbsPercentage = null,
    decimal? FatPercentage = null,
    decimal? CaloriesPercentage = null,
    IReadOnlyList<string>? Allergens = null,
    IReadOnlyList<string>? AllergensAr = null,
    IReadOnlyList<string>? AllergensEn = null,
    IReadOnlyList<CustomerCalendarMealAllergenDto>? AllergenDetails = null);

/// <summary>Machine-readable reason a calendar day rejects edits; surfaced as editBlockReason.
/// fulfillment_not_ready is a screen-level state (no days exist before fulfillment completes) and
/// is never a day-level value.</summary>
public static class CalendarEditBlockReasons
{
    public const string SelectionDeadlinePassed = "selection_deadline_passed";
    public const string DayLocked = "day_locked";
    public const string DayFrozen = "day_frozen";
    public const string DayCancelled = "day_cancelled";
    public const string FulfillmentNotReady = "fulfillment_not_ready";
}

public sealed record CustomerCalendarDaySummaryDto(
    Guid Id,
    DateOnly ServiceDate,
    Guid? RestaurantId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    string? RestaurantLogoUrl,
    SubscriptionDayStatus Status,
    string UiStatus,
    bool IsAutoSelected,
    bool CanEdit,
    DateTime? SelectionDeadlineUtc,
    DateTime? LockAtUtc,
    int? LockRemainingSeconds,
    int FilledCount,
    int RequiredCount,
    IReadOnlyList<CustomerCalendarSlotDto> SlotRequirements,
    string? FailureReasonCode,
    IReadOnlyList<CustomerCalendarMealSummaryDto> SelectedMeals,
    decimal? RestaurantRating = null,
    int RestaurantRatingCount = 0,
    TimeOnly? DeliveryWindowStart = null,
    TimeOnly? DeliveryWindowEnd = null,
    string? DeliveryTimeText = null,
    RestaurantTier? Tier = null,
    string? TierNameAr = null,
    string? TierNameEn = null,
    string? TierName = null,
    string? TierLogoUrl = null,
    string? ProgramNameAr = null,
    string? ProgramNameEn = null,
    string? ProgramName = null,
    string? EditBlockReason = null);

public sealed record CustomerDayNutritionTotalsDto(
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    decimal ProteinPercentage = 0m,
    decimal CarbsPercentage = 0m,
    decimal FatPercentage = 0m,
    decimal CaloriesPercentage = 0m);

public sealed record CustomerCalendarDayDto(
    Guid Id,
    DateOnly ServiceDate,
    Guid? RestaurantId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    string? RestaurantLogoUrl,
    SubscriptionDayStatus Status,
    string UiStatus,
    bool IsAutoSelected,
    bool CanEdit,
    DateTime? SelectionDeadlineUtc,
    DateTime? LockAtUtc,
    int? LockRemainingSeconds,
    int FilledCount,
    int RequiredCount,
    IReadOnlyList<CustomerCalendarSlotDto> SlotRequirements,
    string? FailureReasonCode,
    CustomerDayNutritionTotalsDto NutritionTotals,
    IReadOnlyList<CustomerCalendarMealDto> SelectedMeals,
    decimal? RestaurantRating = null,
    int RestaurantRatingCount = 0,
    TimeOnly? DeliveryWindowStart = null,
    TimeOnly? DeliveryWindowEnd = null,
    string? DeliveryTimeText = null,
    RestaurantTier? Tier = null,
    string? TierNameAr = null,
    string? TierNameEn = null,
    string? TierName = null,
    string? TierLogoUrl = null,
    string? ProgramNameAr = null,
    string? ProgramNameEn = null,
    string? ProgramName = null,
    decimal? ProteinPercentage = null,
    decimal? CarbsPercentage = null,
    decimal? FatPercentage = null,
    decimal? CaloriesPercentage = null,
    string? EditBlockReason = null);

public sealed record CustomerCalendarScreenDto(
    Guid SubscriptionId,
    Guid ProgramId,
    Guid BundleId,
    int RequiredCount,
    IReadOnlyList<CustomerCalendarSlotDto> SlotRequirements,
    bool IsFulfillmentReady,
    IReadOnlyList<CustomerCalendarDaySummaryDto> Days,
    bool HasUnreadNotifications = false,
    int UnreadNotificationsCount = 0);

public sealed record CustomerDayRestaurantDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    RestaurantTier Tier,
    int UsedCount,
    int Limit,
    int RemainingCount,
    bool IsBusy,
    bool IsQuotaExhausted,
    bool IsEnabled,
    string? DisabledReason,
    int SafeMealCount,
    decimal? Rating = null,
    int RatingCount = 0,
    TimeOnly? DeliveryWindowStart = null,
    TimeOnly? DeliveryWindowEnd = null,
    string? DeliveryTimeText = null,
    string? TierNameAr = null,
    string? TierNameEn = null,
    string? TierName = null,
    string? TierLogoUrl = null);

public sealed record CustomerDayRestaurantsDto(
    DateOnly ServiceDate,
    int Limit,
    int AdminCeiling,
    IReadOnlyList<CustomerDayRestaurantDto> Restaurants,
    int Page = 1,
    int PageSize = 0,
    int TotalCount = 0,
    int TotalPages = 0,
    bool HasNextPage = false,
    bool HasPreviousPage = false);

public sealed record CustomerRestaurantCoreIngredientDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? IconUrl,
    bool IsLocal = false,
    string? OriginCountryCode = null,
    string? OriginCountryNameAr = null,
    string? OriginCountryNameEn = null,
    string? FlagEmoji = null);

public sealed record CustomerCalendarSlotMealsDto(
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    int Count,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage,
    IReadOnlyList<CustomerCalendarMealDto> Meals);

public sealed record CustomerDayRestaurantMealsDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    DateOnly ServiceDate,
    int RequiredCount,
    int FilledCount,
    IReadOnlyList<CustomerCalendarSlotMealsDto> Slots,
    IReadOnlyList<CustomerRestaurantCoreIngredientDto> CoreIngredients,
    IReadOnlyList<Guid> SelectedMealIds,
    decimal? Rating = null,
    int RatingCount = 0,
    TimeOnly? DeliveryWindowStart = null,
    TimeOnly? DeliveryWindowEnd = null,
    string? DeliveryTimeText = null,
    RestaurantTier? Tier = null,
    string? TierNameAr = null,
    string? TierNameEn = null,
    string? TierName = null,
    string? TierLogoUrl = null,
    string? ProgramNameAr = null,
    string? ProgramNameEn = null,
    string? ProgramName = null,
    CustomerDayNutritionTotalsDto? NutritionTotals = null,
    decimal? CaloriesPercentage = null,
    decimal? ProteinPercentage = null,
    decimal? CarbsPercentage = null,
    decimal? FatPercentage = null);

public sealed record SelectDayRestaurantRequest(Guid RestaurantId);
public sealed record SelectDaySlotMealRequest(Guid MealId, string Slot);
public sealed record CompleteDayBoxRequest(string? IdempotencyKey = null);

public sealed record GetCustomerCalendarQuery(Guid SubscriptionId) : IQuery<CustomerCalendarScreenDto>;
public sealed record GetCustomerCalendarDayQuery(Guid SubscriptionId, DateOnly ServiceDate) : IQuery<CustomerCalendarDayDto>;
public sealed record GetCustomerDayRestaurantsQuery(
    Guid SubscriptionId,
    DateOnly ServiceDate,
    string? Query = null,
    int? Page = null,
    int? PageSize = null) : IQuery<CustomerDayRestaurantsDto>;
public sealed record GetCustomerDayRestaurantMealsQuery(
    Guid SubscriptionId,
    DateOnly ServiceDate,
    Guid RestaurantId,
    string? Slot = null,
    int Page = 1,
    int PageSize = 20) : IQuery<CustomerDayRestaurantMealsDto>;
/// <summary>Legacy mobile path: meals for the day's restaurant without restaurantId in the URL.</summary>
public sealed record GetCustomerDayAvailableMealsQuery(
    Guid SubscriptionId,
    DateOnly ServiceDate,
    string? Slot = null,
    int Page = 1,
    int PageSize = 20) : IQuery<CustomerDayRestaurantMealsDto>;
public sealed record SelectCustomerDayRestaurantCommand(Guid SubscriptionId, DateOnly ServiceDate, Guid RestaurantId) : ICommand<CustomerCalendarDayDto>;
public sealed record SelectCustomerDaySlotMealCommand(Guid SubscriptionId, DateOnly ServiceDate, Guid MealId, string Slot) : ICommand<CustomerCalendarDayDto>;
public sealed record CompleteCustomerDayBoxCommand(Guid SubscriptionId, DateOnly ServiceDate, string? IdempotencyKey = null) : ICommand<CustomerCalendarDayDto>;
public sealed record ConfirmCustomerDayBoxCommand(Guid SubscriptionId, DateOnly ServiceDate) : ICommand<CustomerCalendarDayDto>;
public sealed record AutoSelectCustomerDayBoxCommand(Guid SubscriptionId, DateOnly ServiceDate, bool LockAfter = false) : ICommand<CustomerCalendarDayDto>;
