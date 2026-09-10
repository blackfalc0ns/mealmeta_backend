using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public sealed record SubscriptionTypeOptionDto(
    string Code,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string DescriptionEn,
    int MaxMembers,
    bool IsAvailable,
    bool MembersConfiguredAfterPayment,
    string? DisabledReason = null,
    bool RequiresConversion = false,
    string? ConversionEndpoint = null);

public sealed record CustomerProgramCardDto(
    Guid ProgramId,
    string Code,
    string? ShortCode,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<string> KeywordsAr,
    IReadOnlyList<string> KeywordsEn,
    string? ImageUrl,
    int BundleCount,
    IReadOnlyList<string> SupportedSlots);

public sealed record CustomerSubscriptionDeliveryAddressDto(
    Guid AddressId,
    Guid RegionId,
    string Label,
    string AddressLine,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    decimal? Latitude,
    decimal? Longitude,
    bool IsDefault);

public sealed record CustomerProgramBundlesDto(
    bool HasDeliveryAddress,
    IReadOnlyList<CustomerBundleCardDto> Bundles,
    bool HasAllergies = true,
    bool HasDietaryPreferences = true,
    bool IsProfileComplete = true);

public sealed record CustomerBundleCardDto(
    Guid BundleId,
    Guid ProgramId,
    string Code,
    string NameAr,
    string NameEn,
    string? Description,
    string? ImageUrl,
    int MealsPerDay,
    int BreakfastCount,
    int LunchCount,
    int DinnerCount,
    int SnackCount,
    IReadOnlyList<SlotRequirementDto> SlotRequirements,
    int SortOrder,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<CustomerBundleSlotDto> Slots,
    int RestaurantCount,
    IReadOnlyList<CustomerBundleRestaurantDto> Restaurants,
    int SelectionCount,
    bool IsMostSelected,
    CustomerBundleBadgeDto? Badge);

public sealed record CustomerBundleSlotDto(string Code, string NameAr, string NameEn, int Quantity);

public sealed record CustomerBundleRestaurantDto(Guid RestaurantId, string? LogoUrl);

public sealed record CustomerBundleBadgeDto(string Code, string NameAr, string NameEn);

public sealed record CustomerDurationOptionDto(
    int? DurationDays,
    string NameAr,
    string NameEn,
    int SelectionCount,
    bool IsPopular,
    CustomerBundleBadgeDto? Badge,
    bool SupportsFriday,
    bool IncludeFriday,
    int? FridayDays,
    int? TotalServiceDays,
    CustomerFridayOptionDto FridayOption,
    CustomerFridayAvailabilityDto FridayAvailability,
    bool IsCustom = false,
    int? MinDays = null,
    int? MaxDays = null);

public sealed record CustomerFridayOptionDto(string Code, string NameAr, string NameEn, bool DefaultSelected, bool IsEnabled);

public sealed record CustomerFridayAvailabilityDto(
    bool IsAvailable,
    int RestaurantCount,
    string Code,
    string MessageAr,
    string MessageEn,
    string DisplayType);

public sealed record CustomerTierOptionDto(
    RestaurantTier Tier,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    string DescriptionAr,
    string DescriptionEn,
    int RestaurantCount,
    IReadOnlyList<CustomerBundleRestaurantDto> Restaurants,
    int? DurationDays,
    decimal? TotalPrice,
    decimal? DailyPrice,
    decimal? PriceDifference,
    string? CurrencyCode,
    int SelectionCount,
    bool IsRecommended,
    bool IncludeFriday,
    int? FridayDays,
    int? TotalServiceDays,
    CustomerFridayAvailabilityDto FridayAvailability);

public sealed record DurationPriceOptionDto(
    int DurationDays,
    string NameAr,
    string NameEn,
    decimal TotalPrice,
    decimal DailyPrice,
    string CurrencyCode);

public sealed record TierConfigurationDto(
    RestaurantTier Tier,
    string NameAr,
    string NameEn,
    int RestaurantCount,
    IReadOnlyList<DurationPriceOptionDto> Durations);

public sealed record SubscriptionConfigurationDto(
    Guid ProgramId,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    string? Description,
    int MealsPerDay,
    IReadOnlyList<SlotRequirementDto> SlotRequirements,
    IReadOnlyList<TierConfigurationDto> Tiers);

public sealed record SubscriptionSummaryRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    DateOnly? StartDate = null,
    bool IncludeFriday = false);

public sealed record SubscriptionFlowContext(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    DateOnly StartDate,
    Guid RegionId,
    bool IncludeFriday = false);

public sealed record CustomerRestaurantBrowseCardDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    decimal? Rating,
    int MealCount,
    int BundleMealCount,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    string? TierLogoUrl,
    IReadOnlyList<CustomerRestaurantClassificationDto> Classifications);

public sealed record CustomerRestaurantClassificationDto(string? LogoUrl, RestaurantTier Tier);

public sealed record CustomerRestaurantBrowseDetailsDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    string? CoverImageUrl,
    IReadOnlyList<CustomerRestaurantBrowseCoreIngredientDto> CoreIngredients,
    IReadOnlyList<CustomerIngredientOriginDto> IngredientOrigins,
    IReadOnlyList<CustomerRestaurantBrowseMealGroupDto> MealGroups);

public sealed record CustomerRestaurantBrowseCoreIngredientDto(
    Guid IngredientId,
    string NameAr,
    string NameEn,
    string? IconUrl,
    bool IsLocal = false,
    string? OriginCountryCode = null,
    string? OriginCountryNameAr = null,
    string? OriginCountryNameEn = null,
    string? FlagEmoji = null);

public sealed record CustomerIngredientOriginDto(
    string Category,
    string NameAr,
    string NameEn,
    bool IsLocal,
    string SourceAr,
    string SourceEn,
    string? OriginCountryCode,
    string? OriginCountryNameAr,
    string? OriginCountryNameEn,
    string? FlagEmoji,
    string? IconUrl = null);

public sealed record CustomerRestaurantBrowseMealGroupDto(
    string Slot,
    string NameAr,
    string NameEn,
    IReadOnlyList<CustomerRestaurantBrowseMealDto> Meals);

public sealed record CustomerRestaurantBrowseMealDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    IReadOnlyList<CustomerRestaurantBrowseMealIngredientDto> Ingredients);

public sealed record CustomerRestaurantBrowseMealIngredientDto(
    Guid IngredientId,
    string NameAr,
    string NameEn,
    string Category,
    bool IsCore = false,
    string? IconUrl = null);

public sealed record CustomerRestaurantBrowseMealDetailsDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    string Slot,
    string SlotNameAr,
    string SlotNameEn,
    Guid RestaurantId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    string? RestaurantLogoUrl,
    decimal? RestaurantRating,
    int RestaurantRatingCount,
    TimeOnly? DeliveryWindowStart,
    TimeOnly? DeliveryWindowEnd,
    IReadOnlyList<CustomerRestaurantBrowseMealIngredientDto> Ingredients,
    CustomerMealNutritionDto Nutrition,
    IReadOnlyList<CustomerMealAllergenDto> Allergens);

public sealed record CustomerMealNutritionDto(
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    decimal? FiberG,
    int? CarbsPercent,
    int? ProteinPercent,
    int? FatPercent);

public sealed record CustomerMealAllergenDto(string Code, string NameAr, string NameEn);

public sealed record CustomerRestaurantCardDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    RestaurantTier Tier,
    string? TierLogoUrl,
    decimal FinalPrice,
    string CurrencyCode,
    int MealCount,
    TimeOnly? DeliveryWindowStart,
    TimeOnly? DeliveryWindowEnd,
    IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate,
    bool IncludeFriday,
    int FridayDays,
    int TotalServiceDays);

public sealed record CustomerMealGroupDto(string Slot, string NameAr, string NameEn, IReadOnlyList<CustomerMealDetailsDto> Meals);

public sealed record CustomerRestaurantDetailsDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? CoverImageUrl,
    RestaurantTier Tier,
    string? TierLogoUrl,
    decimal FinalPrice,
    string CurrencyCode,
    TimeOnly? DeliveryWindowStart,
    TimeOnly? DeliveryWindowEnd,
    IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate,
    IReadOnlyList<CustomerMealGroupDto> MealGroups,
    bool IncludeFriday,
    int FridayDays,
    int TotalServiceDays);

public sealed record CustomerIngredientDetailsDto(
    Guid IngredientId,
    string NameAr,
    string NameEn,
    decimal PortionGrams,
    string? OriginCountryCode,
    string? SupplierName,
    bool IsLocal,
    bool IsHalalCertified,
    IReadOnlyList<string> AllergenCodes,
    bool IsCore = false,
    string? IconUrl = null);

public sealed record CustomerMealDetailsDto(
    Guid MealId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Slot,
    int SortOrder,
    string? ImageUrl,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? NetCarbsG,
    decimal? FatG,
    decimal? FiberG,
    decimal? ServingWeightG,
    IReadOnlyList<string> PresentAllergenCodes,
    IReadOnlyList<string> MayContainAllergenCodes,
    IReadOnlyList<string> DietaryPreferenceCodes,
    IReadOnlyList<CustomerIngredientDetailsDto> Ingredients,
    bool IsSelectable,
    string? BlockReason);

public sealed record CustomerSummarySlotDto(string Slot, string NameAr, string NameEn, int Count);

public sealed record CustomerSummaryIncludeDto(string Code, string TextAr, string TextEn);

public sealed record CustomerSummaryIncludesDto(string TitleAr, string TitleEn, IReadOnlyList<CustomerSummaryIncludeDto> Items);

public sealed record SubscriptionOrderSummaryDto(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    string? ProgramImageUrl,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    string? ImageUrl,
    int RestaurantCount,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    int DurationDays,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<DateOnly> ServiceDates,
    IReadOnlyList<CustomerSummarySlotDto> SlotRequirements,
    decimal BasePrice,
    decimal CampaignDiscount,
    decimal PromoDiscount,
    decimal WalletCredit,
    decimal GatewayFee,
    decimal FinalPrice,
    string CurrencyCode,
    bool TermsAcceptanceRequired,
    string CheckoutEndpoint,
    bool IncludeFriday,
    int FridayDays,
    int TotalServiceDays,
    CustomerSummaryIncludesDto Includes,
    SubscriptionSummaryAddressDto? Address = null,
    IReadOnlyList<SubscriptionSummaryRestaurantDto>? Restaurants = null,
    bool CanCheckout = true,
    string? CheckoutBlockReason = null,
    DateOnly? ChosenStartDate = null,
    RestaurantRepetitionSummaryDto? RestaurantRepetition = null);

public sealed record CustomerSubscriptionPrerequisitesDto(
    bool IsComplete,
    bool HasDeliveryAddress,
    bool HasAllergies,
    bool HasDietaryPreferences,
    Guid DeliveryRegionId);
