using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Localization;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyManager;

public sealed record LocalizedPairDto(string Ar, string En);

public sealed record FamilyShortcutDto(string Code, bool Enabled, string? UnavailableReason);

public sealed record FamilyWalletDto(bool Available, string? UnavailableReason);

public sealed record FamilyTodayNutritionDto(decimal Calories, decimal ProteinG, decimal CarbsG, decimal FatG);

public sealed record FamilyMemberLifecycleDto(
    FamilyMemberSetupStatus SetupStatus, bool CheckoutSelected, string? PaymentStatus,
    string FulfillmentStatus, string? ServiceStatus, string AccessStatus, string DisplayStatus);

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
    int RemainingDays = 0, FamilyMemberLifecycleDto? Lifecycle = null,
    Guid? UpcomingSubscriptionId = null, DateOnly? UpcomingStartDate = null);

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
    FamilyMemberSetupStatus SetupStatus, FamilyMemberLifecycleDto? Lifecycle = null,
    Guid? UpcomingSubscriptionId = null, DateOnly? UpcomingStartDate = null,
    string? ProfileImageUrl = null, bool HasSeparateAccount = false,
    FamilyPlanSummaryDto? PlanSummary = null, FamilyTodayMealSelectionDto? TodayMealSelection = null);

public sealed record FamilyPlanSummaryDto(
    int? MealsPerDay, int? DurationDays, DateOnly? StartDate, DateOnly? EndDate,
    DateOnly? NextRenewalDate, int? TotalServiceDays, int? UsedServiceDays, decimal? UsagePercentage);

public sealed record FamilyTodayMealSelectionDto(
    DateOnly ServiceDate, string UiStatus, int FilledCount, int RequiredCount, bool IsAutoSelected);

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
    string? NationalPhoneNumber = null, FamilyMemberLifecycleDto? Lifecycle = null,
    Guid? UpcomingSubscriptionId = null, DateOnly? UpcomingStartDate = null);

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
    string? NationalPhoneNumber = null,
    bool RequiresPasswordSetup = false);

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
    IReadOnlyList<FamilyPermissionGroupDto> Groups,
    long Version = 0,
    bool ViewCalendar = false,
    bool ViewInvoices = false,
    bool SelectRestaurant = false,
    bool ConfirmMeals = false,
    bool RequestFreeze = false,
    bool RequestRenewal = false,
    bool RequestUpgrade = false,
    bool CancelSubscription = false,
    bool ViewFamilyCalendars = false,
    bool ViewFamilyPlans = false,
    bool ViewFamilyInvoices = false);

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
    bool? Remove,
    bool GenerateTemporaryPassword = false);

public sealed record UpdateFamilyMemberPermissionsRequest(
    bool EditDailyMeals,
    bool ChangeDeliveryTime,
    bool ViewProgram,
    bool ChangeProgram,
    bool EditMealsAndDays,
    bool EditAddress,
    bool UnitExchange,
    bool ChangePassword,
    long? ExpectedVersion = null,
    bool? ViewCalendar = null,
    bool? ViewInvoices = null,
    bool? SelectRestaurant = null,
    bool? ConfirmMeals = null,
    bool? RequestFreeze = null,
    bool? RequestRenewal = null,
    bool? RequestUpgrade = null,
    bool? CancelSubscription = null,
    bool? ViewFamilyCalendars = null,
    bool? ViewFamilyPlans = null,
    bool? ViewFamilyInvoices = null);

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
    public static IReadOnlyList<FamilyPermissionGroupDto> Groups(FamilyMemberPermissions p, ILocalizedMessageProvider messages)
    {
        FamilyPermissionItemDto Item(string key, bool value) => new(key,
            messages.GetArabicMessage($"family.permission.{key}"), messages.GetEnglishMessage($"family.permission.{key}"), value);
        FamilyPermissionGroupDto Group(string key, params FamilyPermissionItemDto[] items) => new(key,
            messages.GetArabicMessage($"family.permission_group.{key}"), messages.GetEnglishMessage($"family.permission_group.{key}"), items);
        return [
            Group("meals", Item("viewCalendar", p.ViewCalendar), Item("viewFamilyCalendars", p.ViewFamilyCalendars),
                Item("selectRestaurant", p.SelectRestaurant), Item("editDailyMeals", p.EditDailyMeals), Item("confirmMeals", p.ConfirmMeals),
                Item("editMealsAndDays", p.EditMealsAndDays), Item("unitExchange", p.UnitExchange)),
            Group("program", Item("viewProgram", p.ViewProgram), Item("viewFamilyPlans", p.ViewFamilyPlans),
                Item("changeProgram", p.ChangeProgram), Item("requestFreeze", p.RequestFreeze), Item("requestRenewal", p.RequestRenewal),
                Item("requestUpgrade", p.RequestUpgrade), Item("cancelSubscription", p.CancelSubscription)),
            Group("delivery", Item("changeDeliveryTime", p.ChangeDeliveryTime), Item("editAddress", p.EditAddress)),
            Group("account", Item("changePassword", p.ChangePassword), Item("viewInvoices", p.ViewInvoices),
                Item("viewFamilyInvoices", p.ViewFamilyInvoices))
        ];
    }
}

/// <summary>Query: the current actor's capability decisions for one family member.</summary>
public sealed record GetMyFamilyCapabilitiesQuery(Guid MemberId)
    : MealMate.Application.Common.Messaging.IQuery<IReadOnlyList<FamilyCapabilityDto>>;

/// <summary>One action's capability decision as rendered by the family UI.</summary>
public sealed record FamilyCapabilityDto(
    string Action,
    bool Allowed,
    string? ReasonCode,
    string? RequiredPermission);

/// <summary>Family switcher item with resolved subscription IDs so mobile can switch between members seamlessly.</summary>
public sealed record FamilySwitcherItemDto(
    Guid MemberId,
    string DisplayName,
    FamilyMemberRole Role,
    bool IsManager,
    Guid? MemberSubscriptionId,
    Guid? UpcomingSubscriptionId,
    DateOnly? UpcomingStartDate,
    string? ProgramNameAr,
    string? ProgramNameEn,
    string? ProfileImageUrl,
    bool IsPaused,
    bool CanEditDailyMeals,
    FamilyMemberSetupStatus SetupStatus);

public sealed record GetFamilySwitcherQuery : IQuery<IReadOnlyList<FamilySwitcherItemDto>>;

