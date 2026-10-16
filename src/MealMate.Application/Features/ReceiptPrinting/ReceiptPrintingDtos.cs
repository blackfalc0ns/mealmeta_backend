namespace MealMate.Application.Features.ReceiptPrinting;

public sealed record ReceiptTemplateDto(
    Guid Id,
    string Key,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    bool IsEnabled,
    bool IsPublished,
    bool IsRestaurantVisible,
    bool IsGlobalDefault,
    int Version,
    string ConfigurationJson,
    DateTime? PublishedAtUtc);

public sealed record RestaurantReceiptSettingsDto(
    Guid RestaurantId,
    Guid? SelectedTemplateId,
    string? SelectedTemplateKey,
    Guid? AdminAssignedTemplateId,
    string? AdminAssignedTemplateKey,
    bool AllowRestaurantSelection,
    Guid EffectiveTemplateId,
    string EffectiveTemplateKey);

public sealed record ReceiptBrandDto(
    string NameAr,
    string NameEn,
    string? LogoUrl,
    string FallbackInitials);

public sealed record ReceiptCustomerDto(
    string NameAr,
    string NameEn,
    string AreaAr,
    string AreaEn,
    string AddressLineAr,
    string AddressLineEn);

public sealed record ReceiptDeliveryDto(
    DateOnly DeliveryDate,
    string TimeWindow,
    string? Shift,
    string? BatchCode,
    string Status);

public sealed record ReceiptMealNutritionDto(
    decimal Calories,
    decimal ProteinGrams,
    decimal CarbsGrams,
    decimal FatGrams);

public sealed record ReceiptMealItemDto(
    Guid Id,
    int ItemIndex,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    int Quantity,
    ReceiptMealNutritionDto Nutrition,
    IReadOnlyList<string> Allergens,
    string? PreparationNotes);

public sealed record OperationalReceiptDto(
    Guid OrderId,
    string OrderCode,
    string BarcodeText,
    string QrPayload,
    int MealCount,
    string EffectiveTemplateKey,
    string TemplateConfigurationJson,
    ReceiptBrandDto AppBrand,
    ReceiptBrandDto RestaurantBrand,
    ReceiptCustomerDto Customer,
    ReceiptDeliveryDto Delivery,
    IReadOnlyList<ReceiptMealItemDto> Meals);

public sealed record UpdateReceiptTemplateConfigRequest(string ConfigurationJson);

public sealed record UpdateReceiptTemplateVisibilityRequest(bool IsEnabled, bool IsRestaurantVisible);

public sealed record SetGlobalDefaultTemplateRequest(Guid TemplateId);

public sealed record AdminUpdateRestaurantSettingsRequest(Guid? AdminAssignedTemplateId, bool AllowRestaurantSelection);

public sealed record SelectRestaurantTemplateRequest(Guid SelectedTemplateId);

public sealed record LogReceiptPrintRequest(
    Guid OrderId,
    Guid TemplateId,
    int TemplateVersion,
    string? PrintStatus,
    string? Notes);
