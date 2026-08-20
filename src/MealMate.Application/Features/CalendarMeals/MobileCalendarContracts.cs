namespace MealMate.Application.Features.CalendarMeals;

public sealed record MobileCalendarDayDto(
    Guid SubscriptionDayId,
    DateOnly ServiceDate,
    string DayOfWeekAr,
    int DayNumber,
    string Status,
    bool IsAutoSelected,
    DateTime? LockAtUtc,
    string? CountdownRemainingText,
    Guid? SelectedRestaurantId,
    string? SelectedRestaurantName,
    IReadOnlyList<MobileSelectedMealDto> SelectedMeals);

public sealed record MobileSelectedMealDto(
    Guid MealId,
    string MealName,
    string Slot,
    int Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    string? PhotoUrl);

public sealed record MobileEligibleRestaurantDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    decimal Rating,
    int AvailableMealsCount,
    int AllowedDaysQuota,
    int UsedDaysCount,
    int RemainingDaysQuota,
    bool IsQuotaExhausted,
    IReadOnlyList<string> Badges);

public sealed record MobileIngredientOriginDto(
    string Category,
    string Origin,
    string CountryFlagCode,
    bool IsImported);

public sealed record MobileRestaurantProfileMenuDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string? CoverImageUrl,
    string? LogoUrl,
    decimal Rating,
    IReadOnlyList<MobileIngredientOriginDto> IngredientOrigins,
    IReadOnlyDictionary<string, IReadOnlyList<MobileMealCardDto>> CategorizedMeals);

public sealed record MobileMealCardDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string Slot,
    string? PhotoUrl,
    int Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Allergens,
    bool HasCustomerAllergenConflict);

public sealed record MobileMealNutritionDetailsDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string? PhotoUrl,
    decimal RestaurantRating,
    int RatingCount,
    IReadOnlyList<string> Ingredients,
    int Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams,
    decimal FiberGrams,
    decimal ProteinPercentage,
    decimal CarbsPercentage,
    decimal FatPercentage,
    IReadOnlyList<string> Allergens);
