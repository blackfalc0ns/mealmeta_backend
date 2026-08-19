using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyGroupScreenDto(
    Guid FamilyGroupId,
    string Name,
    FamilyGroupStatus Status,
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
    string NutritionalGoalCode,
    FamilyActivityLevel ActivityLevel,
    Guid? DeliveryAddressId,
    FamilyMemberSetupStatus SetupStatus,
    Guid? ProgramId,
    Guid? BundleId,
    RestaurantTier? SelectedTier,
    int? DurationDays,
    bool IncludeFriday);

public sealed record FamilyMemberWriteDto(
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    string NutritionalGoalCode,
    FamilyActivityLevel ActivityLevel,
    Guid DeliveryAddressId);

public sealed record FamilyDraftMetadataDto(
    IReadOnlyList<FamilyLookupDto> Relationships,
    IReadOnlyList<FamilyLookupDto> Goals,
    IReadOnlyList<FamilyLookupDto> ActivityLevels,
    int MaxMembers,
    decimal DiscountPercent,
    int MinMembersForDiscount);

public sealed record UpsertFamilyGroupRequest(string Name);

public sealed record GetFamilyDraftMetadataQuery : IQuery<FamilyDraftMetadataDto>;

public sealed record GetCurrentFamilyGroupQuery : IQuery<FamilyGroupScreenDto>;

public sealed record UpsertFamilyGroupCommand(string Name) : ICommand<FamilyGroupScreenDto>;

public sealed record AddFamilyDraftMemberCommand(FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record UpdateFamilyMemberCommand(Guid MemberId, FamilyMemberWriteDto Member) : ICommand<FamilyGroupScreenDto>;

public sealed record DeleteFamilyMemberCommand(Guid MemberId) : ICommand<FamilyGroupScreenDto>;
