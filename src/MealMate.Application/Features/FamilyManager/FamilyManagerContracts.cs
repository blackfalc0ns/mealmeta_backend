using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyManager;

public sealed record LocalizedPairDto(string Ar, string En);

public sealed record FamilyShortcutDto(string Code, bool Enabled, string? UnavailableReason);

public sealed record FamilyWalletDto(bool Available, string? UnavailableReason);

public sealed record FamilyTodayNutritionDto(decimal Calories, decimal ProteinG, decimal CarbsG, decimal FatG);

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
    int? TodayRequiredCount,
    int RemainingDays = 0);

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
    IReadOnlyList<FamilyShortcutDto> Shortcuts,
    FamilyWalletDto Wallet);

public sealed record FamilyMemberListItemDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    int AgeYears,
    bool IsPaused,
    Guid? MemberSubscriptionId,
    string? ProgramNameAr,
    string? ProgramNameEn,
    int RemainingDays,
    FamilyMemberSetupStatus SetupStatus);

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
    Guid? LinkedCustomerUserId,
    string? PhoneNumber = null,
    int RemainingDays = 0,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool CanRenew = false,
    string? RenewalPath = null,
    FamilyTodayNutritionDto? TodayNutrition = null,
    string? CountryCode = null,
    string? NationalPhoneNumber = null);

public sealed record FamilyMemberAccessDto(
    Guid MemberId,
    bool HasSeparateAccount,
    Guid? LinkedCustomerUserId,
    bool IsPaused,
    DateTime? PausedAtUtc,
    DateTime? RemovedAtUtc,
    string? InviteDeepLink,
    DateTime? InviteExpiresAtUtc,
    string? PhoneNumber = null,
    bool HasExistingLogin = false,
    string? GeneratedPassword = null,
    string? CountryCode = null,
    string? NationalPhoneNumber = null);

public sealed record FamilyPermissionItemDto(string Key, string LabelAr, string LabelEn, bool Value);

public sealed record FamilyPermissionGroupDto(
    string Code,
    string LabelAr,
    string LabelEn,
    IReadOnlyList<FamilyPermissionItemDto> Permissions);

public sealed record FamilyMemberPermissionsDto(
    Guid MemberId,
    bool EditDailyMeals,
    bool ChangeDeliveryTime,
    bool ViewProgram,
    bool ChangeProgram,
    bool EditMealsAndDays,
    bool EditAddress,
    bool UnitExchange,
    bool ChangePassword,
    IReadOnlyList<FamilyPermissionGroupDto> Groups);

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
public sealed record GetFamilyMembersQuery : IQuery<IReadOnlyList<FamilyMemberListItemDto>>;
public sealed record GetFamilyMemberQuery(Guid MemberId) : IQuery<FamilyMemberDetailDto>;
public sealed record CreateFamilyMemberCommand(CreateFamilyMemberRequest Request) : ICommand<FamilyMemberDetailDto>;
public sealed record GetFamilyMemberAccessQuery(Guid MemberId) : IQuery<FamilyMemberAccessDto>;
public sealed record UpdateFamilyMemberAccessCommand(Guid MemberId, UpdateFamilyMemberAccessRequest Request) : ICommand<FamilyMemberAccessDto>;
public sealed record GetFamilyMemberPermissionsQuery(Guid MemberId) : IQuery<FamilyMemberPermissionsDto>;
public sealed record UpdateFamilyMemberPermissionsCommand(Guid MemberId, UpdateFamilyMemberPermissionsRequest Request) : ICommand<FamilyMemberPermissionsDto>;
public sealed record AcceptFamilyInviteCommand(string Token) : ICommand<FamilyMemberAccessDto>;

public static class FamilyPermissionCatalog
{
    public static IReadOnlyList<FamilyPermissionGroupDto> Groups(
        bool editDailyMeals,
        bool changeDeliveryTime,
        bool viewProgram,
        bool changeProgram,
        bool editMealsAndDays,
        bool editAddress,
        bool unitExchange,
        bool changePassword)
        =>
        [
            new("meals", "الوجبات", "Meals",
            [
                new("editDailyMeals", "تعديل الوجبات اليومية", "Edit daily meals", editDailyMeals),
                new("editMealsAndDays", "تعديل الوجبات والأيام", "Edit meals and days", editMealsAndDays),
                new("unitExchange", "استبدال الوحدة", "Unit exchange", unitExchange)
            ]),
            new("program", "البرنامج", "Program",
            [
                new("viewProgram", "عرض البرنامج", "View program", viewProgram),
                new("changeProgram", "تغيير البرنامج", "Change program", changeProgram)
            ]),
            new("delivery", "التوصيل", "Delivery",
            [
                new("changeDeliveryTime", "تغيير وقت التوصيل", "Change delivery time", changeDeliveryTime),
                new("editAddress", "تعديل العنوان", "Edit address", editAddress)
            ]),
            new("account", "الحساب", "Account",
            [
                new("changePassword", "تغيير كلمة المرور", "Change password", changePassword)
            ])
        ];
}
