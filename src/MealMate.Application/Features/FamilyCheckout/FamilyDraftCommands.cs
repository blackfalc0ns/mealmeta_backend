using System.Text.Json.Serialization;
using FluentValidation;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Serialization;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilySizePackageDto(
    Guid Id,
    string NameAr,
    string NameEn,
    int MinMembers,
    int MaxMembers,
    decimal DiscountPercent,
    int SortOrder,
    string? ImageStorageKey = null,
    string? ImageUrl = null);

public sealed record FamilyGroupScreenDto(
    Guid FamilyGroupId,
    string Name,
    FamilyGroupStatus Status,
    Guid? SelectedFamilyPackageId,
    string? PackageNameAr,
    string? PackageNameEn,
    int MinMembers,
    int MaxMembers,
    decimal DiscountPercent,
    IReadOnlyList<FamilyMemberCardDto> Members,
    int CurrentStep,
    bool SessionExpired = false,
    DateTime? SessionExpiresAtUtc = null,
    string? SessionMessage = null,
    bool PackageAvailable = false,
    FamilyCheckoutStartedDto? PendingCheckout = null,
    string? RetryPaymentEndpoint = null);

public sealed record FamilyMemberCardDto(
    Guid MemberId,
    FamilyMemberRole Role,
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    Guid? DeliveryAddressId,
    FamilyMemberSetupStatus SetupStatus,
    Guid? ProgramId,
    Guid? BundleId,
    RestaurantTier? SelectedTier,
    int? DurationDays,
    bool IncludeFriday,
    bool IsCurrentUser,
    string? PhoneNumber = null,
    string? Occupation = null,
    DateOnly? StartDate = null,
    CustomerAddressDto? DeliveryAddress = null,
    IReadOnlyList<FoodPreferenceItemDto>? Allergies = null,
    bool AllergiesCompleted = false,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods = null,
    IReadOnlyList<DietaryPreferenceOptionDto>? DietaryPreferences = null,
    bool DietaryCompleted = false,
    bool SubscribeNow = true)
{
    public IReadOnlyList<FoodPreferenceItemDto> Selected => Allergies ?? [];
    public IReadOnlyList<string> SelectedCodes => (Allergies ?? []).Select(x => x.Code).ToList();
    public bool HasNoAllergies => AllergiesCompleted && (Allergies?.Count ?? 0) == 0;
    public bool HasNoDietaryRestriction => DietaryCompleted && (DietaryPreferences?.Count ?? 0) == 0;
    public IReadOnlyList<string> SelectedAllergyOptionCodes => FamilyPreferenceSelectionCodes.SelectedOptions(SelectedCodes, AllergiesCompleted, FamilyPreferenceSelectionCodes.NoAllergies);
    public IReadOnlyList<string> SelectedDietaryOptionCodes => FamilyPreferenceSelectionCodes.SelectedOptions((DietaryPreferences ?? []).Select(x => x.Code), DietaryCompleted, FamilyPreferenceSelectionCodes.NoDietaryRestrictions);
}

public sealed record FamilyMemberWriteDto(
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    [property: JsonConverter(typeof(EmptyStringToNullableGuidConverter))]
    Guid? DeliveryAddressId,
    string? Phone = null,
    string? Occupation = null);

public sealed record FamilyMemberLookupRequest(string? Phone);

public sealed record FamilyMemberLookupDto(
    bool Found,
    string? DisplayName,
    int? AgeYears,
    FamilyMemberGender? Gender);

public sealed record LookupFamilyMemberByPhoneQuery(string? Phone) : IQuery<FamilyMemberLookupDto>;

public sealed record SaveFamilyMemberAllergiesRequest(
    IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>? Allergies = null,
    IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>? Selected = null,
    IReadOnlyList<string>? SelectedCodes = null,
    IReadOnlyList<string>? Codes = null,
    IReadOnlyList<FoodPreferenceAllergenOptionDto>? Allergens = null,
    IReadOnlyList<MealMate.Application.Features.IdentityAccess.Customers.FoodPreferenceItemDto>? DislikedFoods = null,
    IReadOnlyList<string>? IngredientCodes = null,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);

public sealed record SaveFamilyMemberAllergiesCommand(
    Guid MemberId,
    IReadOnlyList<FoodPreferenceItemDto> Allergies,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods = null,
    bool ReplaceDislikedFoods = false,
    IReadOnlyList<string>? DietaryPreferenceCodes = null,
    bool ReplaceDietary = false)
    : ICommand<bool>;

public sealed record SaveFamilyMemberDislikedFoodsRequest(
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods = null,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedIngredients = null,
    IReadOnlyList<FoodPreferenceItemDto>? Selected = null,
    IReadOnlyList<FoodPreferenceItemDto>? Items = null,
    IReadOnlyList<string>? IngredientCodes = null,
    IReadOnlyList<string>? SelectedCodes = null,
    IReadOnlyList<string>? Ingredients = null);

public sealed record SaveFamilyMemberDislikedFoodsCommand(
    Guid MemberId,
    IReadOnlyList<FoodPreferenceItemDto> DislikedFoods)
    : ICommand<bool>;

public sealed record SaveFamilyMemberDietaryRequest(
    IReadOnlyList<string>? DietaryPreferenceCodes = null,
    IReadOnlyList<string>? SelectedCodes = null,
    IReadOnlyList<string>? Selected = null);

public sealed record SaveFamilyMemberDietaryCommand(
    Guid MemberId,
    IReadOnlyList<string> DietaryPreferenceCodes)
    : ICommand<bool>;

public sealed record FamilyMemberAllergiesDto(
    IReadOnlyList<FoodPreferenceItemDto> Selected,
    bool Completed,
    IReadOnlyList<FoodPreferenceItemDto>? DislikedFoods = null)
{
    public IReadOnlyList<string> SelectedCodes => Selected.Select(x => x.Code).ToList();
    public bool HasNoAllergies => Completed && Selected.Count == 0;
    public string NoneOptionCode => FamilyPreferenceSelectionCodes.NoAllergies;
    public IReadOnlyList<string> SelectedOptionCodes => FamilyPreferenceSelectionCodes.SelectedOptions(SelectedCodes, Completed, NoneOptionCode);
}

public sealed record FamilyMemberDislikedFoodsDto(
    IReadOnlyList<FoodPreferenceItemDto> Selected,
    bool Completed)
{
    public bool EatsEverything => Selected.Count == 0;
}

public sealed record FamilyMemberDietaryDto(
    IReadOnlyList<DietaryPreferenceOptionDto> Selected,
    bool Completed)
{
    public IReadOnlyList<string> SelectedCodes => Selected.Select(x => x.Code).ToList();
    public bool HasNoDietaryRestriction => Completed && Selected.Count == 0;
    public string NoneOptionCode => FamilyPreferenceSelectionCodes.NoDietaryRestrictions;
    public IReadOnlyList<string> SelectedOptionCodes => FamilyPreferenceSelectionCodes.SelectedOptions(SelectedCodes, Completed, NoneOptionCode);
}

public sealed record GetFamilyMemberAllergiesQuery(Guid MemberId) : IQuery<FamilyMemberAllergiesDto>;
public sealed record GetFamilyMemberDislikedFoodsQuery(Guid MemberId) : IQuery<FamilyMemberDislikedFoodsDto>;
public sealed record GetFamilyMemberDietaryQuery(Guid MemberId) : IQuery<FamilyMemberDietaryDto>;

public sealed record CreateFamilyMemberAddressCommand(
    Guid MemberId,
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null) : ICommand<CustomerAddressDto>;

public sealed record GetFamilyMemberAddressesQuery(Guid MemberId) : IQuery<IReadOnlyList<CustomerAddressDto>>;

public sealed record UpdateFamilyMemberAddressCommand(
    Guid MemberId,
    Guid AddressId,
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null) : ICommand<CustomerAddressDto>;

public sealed record PreviewFamilyMemberAddressCoverageCommand(
    Guid MemberId,
    Guid? AddressId = null,
    Guid? RegionId = null,
    Guid? BlockRegionId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    bool IsDefault = true,
    int? Page = null,
    int? PageSize = null) : ICommand<AddressCoveragePreviewDto>;

public sealed record SetFamilyMemberDefaultAddressCommand(Guid MemberId, Guid AddressId) : ICommand<CustomerAddressDto>;

public sealed class CreateFamilyMemberAddressCommandValidator : AbstractValidator<CreateFamilyMemberAddressCommand>
{
    public CreateFamilyMemberAddressCommandValidator()
    {
        RuleFor(x => new CreateCustomerAddressCommand(
                x.Label,
                x.AddressLine,
                x.RegionId,
                x.Latitude,
                x.Longitude,
                x.Notes,
                x.IsDefault,
                x.NotifyOnDelivery,
                x.Governorate,
                x.Area,
                x.Block,
                x.Street,
                x.Avenue,
                x.Building,
                x.Unit,
                x.Floor,
                x.BlockRegionId))
            .SetValidator(new CustomerAddressCommandValidator());
    }
}

public sealed class UpdateFamilyMemberAddressCommandValidator : AbstractValidator<UpdateFamilyMemberAddressCommand>
{
    public UpdateFamilyMemberAddressCommandValidator()
    {
        RuleFor(x => new UpdateCustomerAddressCommand(
                x.AddressId,
                x.Label,
                x.AddressLine,
                x.RegionId,
                x.Latitude,
                x.Longitude,
                x.Notes,
                x.IsDefault,
                x.NotifyOnDelivery,
                x.Governorate,
                x.Area,
                x.Block,
                x.Street,
                x.Avenue,
                x.Building,
                x.Unit,
                x.Floor,
                x.BlockRegionId))
            .SetValidator(new UpdateCustomerAddressCommandValidator());
    }
}

public sealed record FamilyDraftMetadataDto(
    IReadOnlyList<FamilyLookupDto> Relationships,
    IReadOnlyList<FamilySizePackageDto> Packages,
    int MaxMembers,
    decimal DiscountPercent,
    int MinMembersForDiscount,
    int SupportedContractVersion = FamilyInvoiceGate.SupportedContractVersion,
    int MinimumInvoiceContractVersion = FamilyInvoiceGate.SupportedContractVersion)
{
    public string NoAllergiesCode => FamilyPreferenceSelectionCodes.NoAllergies;
    public string NoDietaryRestrictionsCode => FamilyPreferenceSelectionCodes.NoDietaryRestrictions;
}

public sealed record UpsertFamilyGroupRequest(string Name, Guid? FamilyPackageId = null);

public sealed record GetFamilyDraftMetadataQuery : IQuery<FamilyDraftMetadataDto>;

public sealed record GetFamilyRelationshipsQuery : IQuery<IReadOnlyList<FamilyLookupDto>>;

public sealed record GetFamilySizePackagesQuery : IQuery<IReadOnlyList<FamilySizePackageDto>>;

public sealed record FamilyCapacityUpgradeRequest(Guid FamilyPackageId, Guid? ExpectedCurrentPackageId = null);

public sealed record FamilyCapacityUpgradeDto(
    Guid FamilyGroupId,
    Guid PreviousFamilyPackageId,
    Guid FamilyPackageId,
    int OccupiedSeatCount,
    int MaxSeats,
    int AvailableSeatCount,
    decimal DiscountPercent);

public sealed record UpgradeFamilyCapacityCommand(FamilyCapacityUpgradeRequest Request) : ICommand<FamilyCapacityUpgradeDto>;

public sealed record GetCurrentFamilyGroupQuery : IQuery<FamilyGroupScreenDto>;

public sealed record UpsertFamilyGroupCommand(string Name, Guid? FamilyPackageId = null) : ICommand<FamilyGroupScreenDto>;

public sealed record AddFamilyDraftMemberCommand(FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record UpdateFamilyMemberCommand(Guid MemberId, FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record DeleteFamilyMemberCommand(Guid MemberId) : ICommand<FamilyGroupScreenDto>;

public sealed record AbandonFamilyDraftCommand : ICommand<bool>;

/// <summary>
/// Partial member plan patch. Omitted fields keep current values; JSON null clears.
/// At least one field must be specified.
/// </summary>
public sealed class SetFamilyMemberPlanRequest
{
    [JsonConverter(typeof(OptionalJsonConverter<Guid>))]
    public Optional<Guid> ProgramId { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<Guid>))]
    public Optional<Guid> BundleId { get; init; }

    /// <summary>Alias for <see cref="BundleId"/> if the client echoes a selected* name.</summary>
    [JsonConverter(typeof(OptionalJsonConverter<Guid>))]
    public Optional<Guid> SelectedBundleId { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<RestaurantTier>))]
    public Optional<RestaurantTier> Tier { get; init; }

    /// <summary>Alias for <see cref="Tier"/> — response cards use selectedTier; mobile often echoes that name.</summary>
    [JsonConverter(typeof(OptionalJsonConverter<RestaurantTier>))]
    public Optional<RestaurantTier> SelectedTier { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<int>))]
    public Optional<int> DurationDays { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<bool>))]
    public Optional<bool> IncludeFriday { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<DateOnly>))]
    public Optional<DateOnly> StartDate { get; init; }

    public SetFamilyMemberPlanRequest()
    {
    }

    /// <summary>Full replace helper (tests / clients sending complete plan).</summary>
    public SetFamilyMemberPlanRequest(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        int durationDays,
        bool IncludeFriday = false)
    {
        ProgramId = Optional<Guid>.Of(programId);
        BundleId = Optional<Guid>.Of(bundleId);
        Tier = Optional<RestaurantTier>.Of(tier);
        DurationDays = Optional<int>.Of(durationDays);
        this.IncludeFriday = Optional<bool>.Of(IncludeFriday);
    }

    public Optional<Guid> ResolvedBundleId =>
        BundleId.IsSpecified ? BundleId : SelectedBundleId;

    public Optional<RestaurantTier> ResolvedTier =>
        Tier.IsSpecified ? Tier : SelectedTier;

    public bool HasAnyPatch =>
        ProgramId.IsSpecified
        || BundleId.IsSpecified
        || SelectedBundleId.IsSpecified
        || Tier.IsSpecified
        || SelectedTier.IsSpecified
        || DurationDays.IsSpecified
        || IncludeFriday.IsSpecified
        || StartDate.IsSpecified;
}

public sealed record SetFamilyMemberPlanCommand(
    Guid MemberId,
    SetFamilyMemberPlanRequest Plan) : ICommand<FamilyGroupScreenDto>;

public sealed record FamilyCheckoutSummaryDto(
    Guid FamilyGroupId,
    string FamilyName,
    Guid SelectedMemberId,
    FamilyMemberSummaryDto SelectedMember,
    IReadOnlyList<FamilyMemberCardDto> Members,
    decimal Subtotal,
    decimal FamilyDiscountPercent,
    decimal FamilyDiscountAmount,
    decimal FinalPrice,
    string CurrencyCode,
    IReadOnlyList<FamilyMealOverviewRowDto> MealOverview,
    bool CanCheckout,
    string? CheckoutBlockReason,
    string CheckoutEndpoint,
    bool ManagerPrepaid = false,
    Guid? ManagerSubscriptionId = null,
    int NewMembersCount = 0,
    decimal NewMembersSubtotal = 0m,
    decimal Discount = 0m,
    decimal WalletCredit = 0m,
    decimal GatewayFee = 0m,
    string DiscountPercentScale = "fraction_0_to_1",
    Guid? SelectedFamilyPackageId = null,
    Guid? EffectiveFamilyPackageId = null,
    int? EffectiveFamilyPackageMinMembers = null,
    int? EffectiveFamilyPackageMaxMembers = null,
    int ParticipantCount = 0,
    string PackageSelectionMode = "selected",
    int RosterCount = 0,
    int SelectedNewSubscriberCount = 0,
    int EligibleExistingSubscriberCount = 0,
    int UnsubscribedCount = 0,
    Guid? BatchId = null,
    long? BatchVersion = null,
    bool LockedQuote = false,
    IReadOnlyList<FamilyMemberScheduleDto>? MemberSchedules = null,
    int OccupiedSeatCount = 0,
    int AvailableSeatCount = 0,
    int MaxSeats = 0,
    int DeferredMemberCount = 0,
    int CurrentPurchaseCount = 0,
    bool ManagerSeatExempt = false,
    bool ManagerIndividualIsLive = false,
    bool IsInitialPurchaseOrReactivation = true,
    IReadOnlyList<FamilyMemberRestaurantDto>? Restaurants = null,
    RestaurantRepetitionSummaryDto? RestaurantRepetition = null,
    IndividualRestaurantAvailabilityDto? Availability = null,
    IReadOnlyList<MobileBlockingReasonDto>? BlockingReasons = null,
    MobileEligibilityDto? Eligibility = null,
    DateTime? SummaryExpiresAtUtc = null,
    DateTime? ServerNowUtc = null,
    MealMate.Application.Features.ProgramsSubscriptions.Flow.SubscriptionSummaryCouponDto? Coupon = null);

/// <summary>Per-member delivery schedule and eligible restaurants for the payment review screen.</summary>
public sealed record FamilyMemberRestaurantDto(Guid RestaurantId, string NameAr, string NameEn,
    int OccurrenceCount = 0, string? LogoUrl = null, RestaurantTier? Tier = null);

public sealed record FamilyMemberScheduleDto(
    Guid MemberId,
    DateOnly? PaymentDate,
    DateOnly? FirstDeliveryDate,
    DateOnly? EndDate,
    int DeliveryDaysCount,
    int MealsPerDay,
    int TotalMeals,
    IReadOnlyList<FamilyMemberRestaurantDto> Restaurants,
    RestaurantRepetitionSummaryDto? RestaurantRepetition = null,
    IndividualRestaurantAvailabilityDto? Availability = null);

public sealed record SetFamilyMemberStartDateRequest(DateOnly StartDate);

public sealed record SetFamilyMemberStartDateCommand(Guid MemberId, DateOnly StartDate)
    : ICommand<FamilyGroupScreenDto>;

/// <summary>Selected-member card on the family payment screen.</summary>
public sealed record FamilyMemberSummaryDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    bool IsManager,
    string RoleAr,
    string RoleEn,
    FamilyMemberSetupStatus SetupStatus,
    bool IsReady,
    Guid? ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid? BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier? SelectedTier,
    string? TierNameAr,
    string? TierNameEn,
    int DurationDays,
    string DurationLabelAr,
    string DurationLabelEn,
    bool IncludeFriday,
    bool Breakfast,
    bool Lunch,
    bool Snack,
    bool Dinner,
    int MealsPerDay,
    int BreakfastCount,
    int LunchCount,
    int SnackCount,
    int DinnerCount,
    decimal MemberPrice);

/// <summary>One row in the family meal overview table on the payment screen.</summary>
public sealed record FamilyMealOverviewRowDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    bool IsManager,
    FamilyMemberSetupStatus SetupStatus,
    bool IsReady,
    bool Breakfast,
    bool Lunch,
    bool Snack,
    bool Dinner,
    int MealsPerDay);

public sealed record GetFamilyCheckoutSummaryQuery(Guid? MemberId = null, string? PromoCode = null) : IQuery<FamilyCheckoutSummaryDto>;
