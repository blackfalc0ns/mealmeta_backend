namespace MealMate.Application.Features.MealLabels;

public sealed record MealLabelDimensionsDto(
    int WidthMm,
    int HeightMm,
    string Orientation); // "portrait" | "landscape" | "square"

public sealed record MealLabelBrandDto(
    string NameAr,
    string NameEn,
    string? LogoUrl,
    string FallbackInitials);

public sealed record MealLabelNutritionDto(
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG);

public sealed record MealBoxLabelItemDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    MealLabelNutritionDto Nutrition,
    string IngredientsAr,
    string IngredientsEn,
    string AllergensAr,
    string AllergensEn,
    string PrepNotesAr,
    string PrepNotesEn,
    string? PackagingDate,
    string? OrderReference,
    int Quantity);

public sealed record MealBoxLabelDataDto(
    string EffectiveTemplateKey,
    MealLabelDimensionsDto Dimensions,
    MealLabelBrandDto RestaurantBrand,
    MealLabelBrandDto AppBrand,
    MealBoxLabelItemDto Meal);

public sealed record MealBoxLabelTemplateDto(
    Guid Id,
    string Key,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int DefaultWidthMm,
    int DefaultHeightMm,
    string DefaultOrientation,
    bool IsEnabled,
    bool IsPublished,
    bool IsRestaurantVisible,
    bool IsGlobalDefault,
    int Version,
    string ConfigurationJson,
    DateTime? PublishedAtUtc);

public sealed record RestaurantMealLabelSettingsDto(
    Guid RestaurantId,
    Guid? SelectedTemplateId,
    string? SelectedTemplateKey,
    Guid? AdminAssignedTemplateId,
    string? AdminAssignedTemplateKey,
    bool AllowRestaurantSelection,
    int ConfiguredWidthMm,
    int ConfiguredHeightMm,
    string Orientation,
    bool ShowPackagingDate,
    bool ShowOrderReference,
    bool ShowQuantity,
    Guid EffectiveTemplateId,
    string EffectiveTemplateKey);

public sealed record UpdateMealLabelTemplateConfigRequest(string ConfigurationJson);

public sealed record UpdateMealLabelTemplateVisibilityRequest(bool IsEnabled, bool IsRestaurantVisible);

public sealed record SetGlobalDefaultMealLabelTemplateRequest(Guid TemplateId);

public sealed record AdminUpdateRestaurantMealLabelSettingsRequest(
    Guid? AdminAssignedTemplateId,
    bool AllowRestaurantSelection,
    int? ConfiguredWidthMm,
    int? ConfiguredHeightMm,
    string? Orientation,
    bool? ShowPackagingDate,
    bool? ShowOrderReference,
    bool? ShowQuantity);

public sealed record RestaurantSelectMealLabelTemplateRequest(
    Guid SelectedTemplateId,
    int? ConfiguredWidthMm,
    int? ConfiguredHeightMm,
    string? Orientation);

public sealed record LogMealLabelPrintRequest(
    Guid TemplateId,
    int TemplateVersion,
    string PrintStatus = "Success",
    string? Notes = null);
