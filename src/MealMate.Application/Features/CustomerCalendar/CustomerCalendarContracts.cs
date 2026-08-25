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
    IReadOnlyList<string> AllergenCodes);

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

public sealed record CustomerCalendarMealDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string Slot,
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
    bool IsSelected = false);

public sealed record CustomerCalendarDayDto(
    Guid Id,
    DateOnly ServiceDate,
    Guid? RestaurantId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    SubscriptionDayStatus Status,
    string UiStatus,
    bool IsAutoSelected,
    bool CanEdit,
    DateTime? SelectionDeadlineUtc,
    DateTime? LockAtUtc,
    int? LockRemainingSeconds,
    int FilledCount,
    int RequiredCount,
    IReadOnlyList<SlotRequirementDto> SlotRequirements,
    string? FailureReasonCode,
    IReadOnlyList<CustomerCalendarMealDto> SelectedMeals);

public sealed record CustomerCalendarScreenDto(
    Guid SubscriptionId,
    Guid ProgramId,
    Guid BundleId,
    int RequiredCount,
    IReadOnlyList<SlotRequirementDto> SlotRequirements,
    bool IsFulfillmentReady,
    IReadOnlyList<CustomerCalendarDayDto> Days);

public sealed record CustomerDayRestaurantDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    RestaurantTier Tier,
    int UsedCount,
    int Limit,
    int RemainingCount,
    bool IsBusy,
    bool IsQuotaExhausted,
    bool IsEnabled,
    string? DisabledReason,
    int SafeMealCount);

public sealed record CustomerDayRestaurantsDto(
    DateOnly ServiceDate,
    int Limit,
    int AdminCeiling,
    IReadOnlyList<CustomerDayRestaurantDto> Restaurants);

public sealed record CustomerDayRestaurantMealsDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    DateOnly ServiceDate,
    int RequiredCount,
    int FilledCount,
    IReadOnlyList<SlotRequirementDto> SlotRequirements,
    IReadOnlyList<CustomerCalendarMealDto> Meals,
    IReadOnlyList<Guid> SelectedMealIds);

public sealed record SelectDayRestaurantRequest(Guid RestaurantId);
public sealed record SelectDaySlotMealRequest(Guid MealId, string Slot);
public sealed record CompleteDayBoxRequest(string? IdempotencyKey = null);

public sealed record GetCustomerCalendarQuery(Guid SubscriptionId) : IQuery<CustomerCalendarScreenDto>;
public sealed record GetCustomerDayRestaurantsQuery(Guid SubscriptionId, DateOnly ServiceDate) : IQuery<CustomerDayRestaurantsDto>;
public sealed record GetCustomerDayRestaurantMealsQuery(Guid SubscriptionId, DateOnly ServiceDate, Guid RestaurantId, string? Slot = null) : IQuery<CustomerDayRestaurantMealsDto>;
/// <summary>Legacy mobile path: meals for the day's restaurant without restaurantId in the URL.</summary>
public sealed record GetCustomerDayAvailableMealsQuery(Guid SubscriptionId, DateOnly ServiceDate, string? Slot = null) : IQuery<CustomerDayRestaurantMealsDto>;
public sealed record SelectCustomerDayRestaurantCommand(Guid SubscriptionId, DateOnly ServiceDate, Guid RestaurantId) : ICommand<CustomerCalendarDayDto>;
public sealed record SelectCustomerDaySlotMealCommand(Guid SubscriptionId, DateOnly ServiceDate, Guid MealId, string Slot) : ICommand<CustomerCalendarDayDto>;
public sealed record CompleteCustomerDayBoxCommand(Guid SubscriptionId, DateOnly ServiceDate, string? IdempotencyKey = null) : ICommand<CustomerCalendarDayDto>;
public sealed record AutoSelectCustomerDayBoxCommand(Guid SubscriptionId, DateOnly ServiceDate, bool LockAfter = false) : ICommand<CustomerCalendarDayDto>;
