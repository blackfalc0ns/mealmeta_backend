using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyManager;

public sealed record LocalizedPairDto(string Ar, string En);

public sealed record FamilyShortcutDto(string Code, bool Enabled, string? UnavailableReason);

public sealed record FamilyHomeMemberDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    bool IsPaused,
    Guid? MemberSubscriptionId,
    string? ProgramLabelAr,
    string? ProgramLabelEn,
    string? TodayUiStatus,
    int? TodayFilledCount,
    int? TodayRequiredCount);

public sealed record FamilyHomeDto(
    Guid GroupId,
    string Name,
    FamilyGroupStatus Status,
    string? PackageLabelAr,
    string? PackageLabelEn,
    decimal QuotedFinalPrice,
    string CurrencyCode,
    IReadOnlyList<FamilyHomeMemberDto> Members,
    IReadOnlyList<FamilyHomeMemberDto> TodaySchedule,
    IReadOnlyList<FamilyShortcutDto> Shortcuts);

public sealed record FamilyMemberDetailDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    string NutritionalGoalCode,
    FamilyActivityLevel ActivityLevel,
    FamilyMemberSetupStatus SetupStatus,
    Guid? ProgramId,
    Guid? BundleId,
    RestaurantTier? SelectedTier,
    int? DurationDays,
    bool IncludeFriday,
    Guid? MemberSubscriptionId,
    string? ProgramNameAr,
    string? ProgramNameEn,
    bool IsPaused,
    bool HasSeparateAccount,
    Guid? LinkedCustomerUserId);

public sealed record FamilyMemberAccessDto(
    Guid MemberId,
    bool HasSeparateAccount,
    Guid? LinkedCustomerUserId,
    bool IsPaused,
    DateTime? PausedAtUtc,
    DateTime? RemovedAtUtc,
    string? InviteDeepLink,
    DateTime? InviteExpiresAtUtc);

public sealed record FamilyMemberPermissionsDto(
    Guid MemberId,
    bool EditDailyMeals,
    bool ChangeDeliveryTime,
    bool ViewProgram,
    bool ChangeProgram,
    bool EditMealsAndDays,
    bool EditAddress,
    bool UnitExchange,
    bool ChangePassword);

public sealed record CreateFamilyMemberRequest(
    string DisplayName,
    string RelationshipCode,
    FamilyMemberGender Gender,
    int AgeYears,
    string NutritionalGoalCode,
    FamilyActivityLevel ActivityLevel,
    Guid? ProgramId,
    Guid? BundleId,
    RestaurantTier? SelectedTier,
    int? DurationDays,
    bool IncludeFriday);

public sealed record UpdateFamilyMemberAccessRequest(
    bool? HasSeparateAccount,
    bool? IsPaused,
    bool? Remove);

public sealed record UpdateFamilyMemberPermissionsRequest(
    bool EditDailyMeals,
    bool ChangeDeliveryTime,
    bool ViewProgram,
    bool ChangeProgram,
    bool EditMealsAndDays,
    bool EditAddress,
    bool UnitExchange,
    bool ChangePassword);

public sealed record GetFamilyHomeQuery : IQuery<FamilyHomeDto>;
public sealed record GetFamilyMembersQuery : IQuery<IReadOnlyList<FamilyMemberDetailDto>>;
public sealed record GetFamilyMemberQuery(Guid MemberId) : IQuery<FamilyMemberDetailDto>;
public sealed record CreateFamilyMemberCommand(CreateFamilyMemberRequest Request) : ICommand<FamilyMemberDetailDto>;
public sealed record GetFamilyMemberAccessQuery(Guid MemberId) : IQuery<FamilyMemberAccessDto>;
public sealed record UpdateFamilyMemberAccessCommand(Guid MemberId, UpdateFamilyMemberAccessRequest Request) : ICommand<FamilyMemberAccessDto>;
public sealed record GetFamilyMemberPermissionsQuery(Guid MemberId) : IQuery<FamilyMemberPermissionsDto>;
public sealed record UpdateFamilyMemberPermissionsCommand(Guid MemberId, UpdateFamilyMemberPermissionsRequest Request) : ICommand<FamilyMemberPermissionsDto>;
public sealed record AcceptFamilyInviteCommand(string Token) : ICommand<FamilyMemberAccessDto>;
