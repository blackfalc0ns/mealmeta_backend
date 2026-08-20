using System.Text.Json.Serialization;
using MealMate.Application.Common.Messaging;
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
    int CurrentStep);

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
    bool IsCurrentUser);

public sealed record FamilyMemberWriteDto(
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    [property: JsonConverter(typeof(EmptyStringToNullableGuidConverter))]
    Guid? DeliveryAddressId);

public sealed record FamilyDraftMetadataDto(
    IReadOnlyList<FamilyLookupDto> Relationships,
    IReadOnlyList<FamilySizePackageDto> Packages,
    int MaxMembers,
    decimal DiscountPercent,
    int MinMembersForDiscount);

public sealed record UpsertFamilyGroupRequest(string Name, Guid? FamilyPackageId = null);

public sealed record GetFamilyDraftMetadataQuery : IQuery<FamilyDraftMetadataDto>;

public sealed record GetFamilyRelationshipsQuery : IQuery<IReadOnlyList<FamilyLookupDto>>;

public sealed record GetFamilySizePackagesQuery : IQuery<IReadOnlyList<FamilySizePackageDto>>;

public sealed record GetCurrentFamilyGroupQuery : IQuery<FamilyGroupScreenDto>;

public sealed record UpsertFamilyGroupCommand(string Name, Guid? FamilyPackageId = null) : ICommand<FamilyGroupScreenDto>;

public sealed record AddFamilyDraftMemberCommand(FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record UpdateFamilyMemberCommand(Guid MemberId, FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record DeleteFamilyMemberCommand(Guid MemberId) : ICommand<FamilyGroupScreenDto>;

public sealed record SetFamilyMemberPlanRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    bool IncludeFriday);

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
    string CheckoutEndpoint);

public sealed record FamilyMemberSummaryDto(
    string DisplayName,
    string RoleAr,
    string RoleEn,
    string ProgramNameAr,
    string ProgramNameEn,
    string BundleNameAr,
    string BundleNameEn,
    int DurationDays,
    bool IncludeFriday,
    int BreakfastCount,
    int LunchCount,
    int SnackCount,
    int DinnerCount,
    decimal MemberPrice);

public sealed record FamilyMealOverviewRowDto(
    Guid MemberId,
    string DisplayName,
    bool Breakfast,
    bool Lunch,
    bool Snack,
    bool Dinner,
    int MealsPerDay);

public sealed record GetFamilyCheckoutSummaryQuery(Guid? MemberId = null) : IQuery<FamilyCheckoutSummaryDto>;
