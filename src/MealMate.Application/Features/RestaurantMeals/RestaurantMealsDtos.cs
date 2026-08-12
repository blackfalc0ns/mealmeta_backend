using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.RestaurantMeals;

// DTOs
public sealed record IngredientDto(
    Guid Id,
    Guid RestaurantId,
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal? DefaultCaloriesPer100g,
    decimal? DefaultProteinPer100g,
    decimal? DefaultCarbsPer100g,
    decimal? DefaultFatPer100g,
    string? AllergenCodes,
    string? Category,
    bool IsActive,
    DateTime CreatedAtUtc,
    decimal? DefaultWeightGrams = null,
    string? OriginCountryCode = null,
    string? SupplierName = null,
    bool IsLocal = false,
    bool IsHalalCertified = false,
    string? TraceabilityNote = null,
    bool IsOriginVisibleToCustomers = true);

public sealed record MealIngredientDto(
    Guid IngredientId,
    string IngredientCode,
    string IngredientNameAr,
    string IngredientNameEn,
    decimal PortionGrams,
    bool IsOptional,
    int SortOrder,
    decimal? CalculatedCalories,
    decimal? CalculatedProteinG,
    decimal? CalculatedCarbsG,
    decimal? CalculatedFatG);

public sealed record MealImageDto(
    Guid Id,
    string ImageUrl,
    int DisplayOrder,
    bool IsPrimary,
    string? AltTextAr,
    string? AltTextEn);

public sealed record MealDto(
    Guid Id,
    Guid MenuId,
    Guid RestaurantId,
    Guid? ProgramId,
    Guid? BundleId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Slot,
    bool IsAvailableForNewOrders,
    VerificationStatus ApprovalStatus,
    string? RejectionReason,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    DateTime CreatedAtUtc,
    IReadOnlyList<MealIngredientDto> Ingredients,
    IReadOnlyList<MealImageDto> Images,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    IReadOnlyList<Guid>? MenuDayIds = null,
    IReadOnlyList<string>? NutritionWarnings = null);

public sealed record MenuDto(
    Guid Id,
    Guid RestaurantId,
    Guid? ProgramId,
    string NameAr,
    string NameEn,
    bool IsActive,
    DateTime CreatedAtUtc,
    int MealCount,
    IReadOnlyList<MealDto>? Meals = null);

// Ingredient Commands & Queries
public sealed record BulkIngredientItemDto(
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? DefaultCaloriesPer100g = null,
    decimal? DefaultProteinPer100g = null,
    decimal? DefaultCarbsPer100g = null,
    decimal? DefaultFatPer100g = null,
    string? AllergenCodes = null,
    string? Category = null,
    decimal? DefaultWeightGrams = null,
    string? OriginCountryCode = null,
    string? SupplierName = null,
    bool IsLocal = false,
    bool IsHalalCertified = false,
    string? TraceabilityNote = null,
    bool IsOriginVisibleToCustomers = true);

public sealed record BulkCreateIngredientsCommand(
    Guid RestaurantId,
    List<BulkIngredientItemDto> Items) : ICommand<int>;

public sealed record ClearIngredientsCommand(Guid RestaurantId) : ICommand<int>;

public sealed record CreateIngredientCommand(
    Guid RestaurantId,
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? DefaultCaloriesPer100g = null,
    decimal? DefaultProteinPer100g = null,
    decimal? DefaultCarbsPer100g = null,
    decimal? DefaultFatPer100g = null,
    string? AllergenCodes = null,
    string? Category = null,
    decimal? DefaultWeightGrams = null,
    string? OriginCountryCode = null,
    string? SupplierName = null,
    bool IsLocal = false,
    bool IsHalalCertified = false,
    string? TraceabilityNote = null,
    bool IsOriginVisibleToCustomers = true) : ICommand<IngredientDto>;

public sealed record UpdateIngredientCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? DefaultCaloriesPer100g = null,
    decimal? DefaultProteinPer100g = null,
    decimal? DefaultCarbsPer100g = null,
    decimal? DefaultFatPer100g = null,
    string? AllergenCodes = null,
    string? Category = null,
    decimal? DefaultWeightGrams = null,
    string? OriginCountryCode = null,
    string? SupplierName = null,
    bool IsLocal = false,
    bool IsHalalCertified = false,
    string? TraceabilityNote = null,
    bool IsOriginVisibleToCustomers = true) : ICommand<IngredientDto>;

public sealed record ToggleIngredientActiveCommand(Guid Id) : ICommand<IngredientDto>;

public sealed record GetIngredientByIdQuery(Guid Id) : IQuery<IngredientDto>;

public sealed record GetIngredientsListQuery(
    Guid RestaurantId,
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<IngredientDto>>;

// Menu Commands & Queries
public sealed record CreateMenuCommand(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    Guid? ProgramId = null) : ICommand<MenuDto>;

public sealed record UpdateMenuCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    Guid? ProgramId = null) : ICommand<MenuDto>;

public sealed record ToggleMenuActiveCommand(Guid Id) : ICommand<MenuDto>;

public sealed record GetMenuByIdQuery(Guid Id) : IQuery<MenuDto>;

public sealed record GetMenusListQuery(
    Guid RestaurantId,
    bool? IsActive = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<MenuDto>>;

public sealed record RestaurantMealBundleDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn);

public sealed record RestaurantMealProgramDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    IReadOnlyList<RestaurantMealBundleDto> Bundles);

public sealed record GetRestaurantMealProgramsQuery(Guid RestaurantId)
    : IQuery<IReadOnlyList<RestaurantMealProgramDto>>;

// Meal Commands & Queries
public sealed record BulkMealItemDto(
    string Code,
    string NameAr,
    string NameEn,
    Guid ProgramId,
    Guid BundleId,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Ingredients = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null);

public sealed record BulkCreateMealsCommand(
    Guid RestaurantId,
    Guid? MenuId,
    List<BulkMealItemDto> Items) : ICommand<int>;

public sealed record ClearMealsCommand(Guid RestaurantId) : ICommand<int>;

public sealed record CreateMealCommand(
    Guid? MenuId,
    Guid RestaurantId,
    string Code,
    string NameAr,
    string NameEn,
    Guid BundleId,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    Guid? ProgramId = null,
    IReadOnlyList<Guid>? MenuDayIds = null,
    IReadOnlyList<MealProgramAssignmentInput>? ProgramAssignments = null,
    IReadOnlyList<MealScheduleAssignmentInput>? ScheduleAssignments = null) : ICommand<MealDto>;

public sealed record UpdateMealCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    Guid? BundleId = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    Guid? ProgramId = null,
    IReadOnlyList<Guid>? MenuDayIds = null,
    IReadOnlyList<MealProgramAssignmentInput>? ProgramAssignments = null,
    IReadOnlyList<MealScheduleAssignmentInput>? ScheduleAssignments = null) : ICommand<MealDto>;

public sealed record ToggleMealAvailabilityCommand(Guid Id) : ICommand<MealDto>;

public sealed record AssignMealProgramCommand(
    Guid MealId,
    Guid RestaurantId,
    Guid ProgramId) : ICommand<MealDto>;

public sealed record LinkMealIngredientCommand(
    Guid MealId,
    Guid IngredientId,
    decimal PortionGrams,
    bool IsOptional = false,
    int SortOrder = 0) : ICommand<MealDto>;

public sealed record UnlinkMealIngredientCommand(
    Guid MealId,
    Guid IngredientId) : ICommand<MealDto>;

public sealed record AddMealImageCommand(
    Guid MealId,
    string ImageUrl,
    bool IsPrimary = false,
    int DisplayOrder = 0,
    string? AltTextAr = null,
    string? AltTextEn = null) : ICommand<MealDto>;

public sealed record RemoveMealImageCommand(
    Guid MealId,
    Guid ImageId) : ICommand<MealDto>;

public sealed record GetMealByIdQuery(Guid Id) : IQuery<MealDto>;

public sealed record GetMealsListQuery(
    Guid RestaurantId,
    Guid? MenuId = null,
    bool? AvailableOnly = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<MealDto>>;

public sealed record ApproveMealCommand(Guid MealId) : ICommand<MealDto>;

public sealed record RejectMealCommand(Guid MealId, string RejectionReason) : ICommand<MealDto>;

public sealed record ArchiveMealCommand(Guid MealId) : ICommand<MealDto>;

public sealed record UnarchiveMealCommand(Guid MealId) : ICommand<MealDto>;

public sealed record DeleteMealCommand(Guid MealId) : ICommand<bool>;

public sealed record DeleteIngredientCommand(Guid IngredientId) : ICommand<bool>;

public sealed record GetAdminMealsListQuery(
    Guid? RestaurantId = null,
    VerificationStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<MealDto>>;
