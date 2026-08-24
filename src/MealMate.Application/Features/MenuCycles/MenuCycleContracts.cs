using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.MenuCycles;

public sealed record MenuCycleDayInput(int DayNumber, string NameAr, string NameEn);

public sealed record MenuCycleMealDto(
    Guid MealId, Guid ProgramId, string ProgramNameAr, string ProgramNameEn,
    string MealNameAr, string MealNameEn, string Slot, int SortOrder,
    bool IsPrimary, VerificationStatus ApprovalStatus);

public sealed record MenuProgramReadinessDto(
    Guid ProgramId, string ProgramNameAr, string ProgramNameEn,
    int CompletedSlots, int RequiredSlots, int ApprovedMeals,
    bool IsReady, IReadOnlyList<string> MissingSlots);

public sealed record MenuDayDto(
    Guid Id, int DayNumber, string NameAr, string NameEn,
    bool IsReady, IReadOnlyList<MenuProgramReadinessDto> Programs,
    IReadOnlyList<MenuCycleMealDto> Meals);

public sealed record MenuCycleVersionDto(
    Guid Id, int VersionNumber, DateOnly? EffectiveFrom, VerificationStatus Status,
    bool IsPaused, string? ReviewNotes, DateTime? ReviewedAtUtc,
    int ReadyDays, int TotalDays, bool IsReady, IReadOnlyList<MenuDayDto> Days);

public sealed record MenuCycleDto(
    Guid Id, Guid? RestaurantId, string NameAr, string NameEn,
    MenuPlanSource Source, bool IsTemplate, ScheduleMode Mode, DateTime CreatedAtUtc,
    MenuCycleVersionDto? CurrentVersion,
    IReadOnlyList<Guid> ProgramIds,
    bool IsShared);

public sealed record MenuPlanProgramDto(
    Guid ProgramId, string ProgramNameAr, string ProgramNameEn,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsPrimaryOwner);

public sealed record MealProgramAssignmentInput(Guid ProgramId, Guid? BundleId = null);
public sealed record MealScheduleAssignmentInput(
    Guid? MenuDayId = null, DayOfWeek? DayOfWeek = null, bool IsPrimary = false, int SortOrder = 0);

public sealed record ResolvedMenuPlanDto(
    Guid PlanId, Guid VersionId, Guid ProgramId, ScheduleMode Mode,
    Guid? MenuDayId, int? DayNumber, string? NameAr, string? NameEn,
    IReadOnlyList<MenuCycleMealDto> Meals);

public sealed record MenuCycleAuditDto(
    Guid Id, string Action, string? Notes, Guid? PerformedByUserId, DateTime CreatedAtUtc);

public sealed record MenuCycleDetailsDto(MenuCycleDto Cycle, IReadOnlyList<MenuCycleAuditDto> Audit);

public sealed record MenuCyclePreviewDayDto(
    DateOnly Date, bool IsWorkingDay, string? ClosureReason,
    Guid? VersionId, Guid? MenuDayId, int? DayNumber,
    string? NameAr, string? NameEn);

public sealed record MenuCycleListItemDto(
    Guid Id, Guid? RestaurantId, string? RestaurantNameAr, string? RestaurantNameEn,
    string NameAr, string NameEn, bool IsTemplate, MenuPlanSource Source, ScheduleMode Mode,
    VerificationStatus Status, bool IsPaused, int ReadyDays, int TotalDays, int ReadinessPercent,
    int? TodayMenuNumber, string? TodayMenuNameAr,
    int? NextMenuNumber, string? NextMenuNameAr, DateOnly? EffectiveFrom,
    IReadOnlyList<Guid> ProgramIds, bool IsShared);

public sealed record ProgramSlotRequirementsDto(
    Guid ProgramId, string ProgramNameAr, string ProgramNameEn,
    IReadOnlyList<string> RequiredSlots);

public sealed record CreateRestaurantMenuCycleCommand(
    Guid RestaurantId, string NameAr, string NameEn, DateOnly EffectiveFrom,
    IReadOnlyList<MenuCycleDayInput> Days, IReadOnlyList<Guid> ProgramIds,
    ScheduleMode Mode = ScheduleMode.Cycle) : ICommand<MenuCycleDto>;

public sealed record SetWeekdayMealCommand(
    Guid RestaurantId, Guid PlanId, DayOfWeek DayOfWeek, Guid MealId,
    int SortOrder = 0, bool IsPrimary = false) : ICommand<MenuCycleDto>;

public sealed record RemoveWeekdayMealCommand(
    Guid RestaurantId, Guid PlanId, DayOfWeek DayOfWeek, Guid MealId) : ICommand<MenuCycleDto>;

public sealed record ResolveMenuPlanQuery(
    Guid RestaurantId, Guid ProgramId, DateOnly ServiceDate) : IQuery<ResolvedMenuPlanDto>;

public sealed record UpdateMenuCycleDraftCommand(
    Guid RestaurantId, Guid CycleId, string NameAr, string NameEn,
    DateOnly EffectiveFrom, IReadOnlyList<MenuCycleDayInput> Days) : ICommand<MenuCycleDto>;

public sealed record CreateFutureMenuCycleVersionCommand(
    Guid RestaurantId, Guid CycleId, DateOnly EffectiveFrom,
    IReadOnlyList<MenuCycleDayInput>? Days = null) : ICommand<MenuCycleDto>;

public sealed record SubmitMenuCycleCommand(Guid RestaurantId, Guid CycleId) : ICommand<MenuCycleDto>;
public sealed record GetRestaurantMenuCyclesQuery(Guid RestaurantId) : IQuery<IReadOnlyList<MenuCycleListItemDto>>;
public sealed record GetRestaurantMenuCycleQuery(Guid RestaurantId, Guid CycleId) : IQuery<MenuCycleDetailsDto>;
public sealed record PreviewRestaurantMenuCycleQuery(Guid RestaurantId, Guid CycleId, DateOnly From, int Days) : IQuery<IReadOnlyList<MenuCyclePreviewDayDto>>;
public sealed record GetRestaurantMenuOfDateQuery(Guid RestaurantId, DateOnly Date, Guid? ProgramId = null) : IQuery<MenuCyclePreviewDayDto>;

public sealed record SetMenuDayMealCommand(
    Guid RestaurantId, Guid MenuDayId, Guid MealId, int SortOrder = 0, bool IsPrimary = false) : ICommand<MenuDayDto>;
public sealed record RemoveMenuDayMealCommand(Guid RestaurantId, Guid MenuDayId, Guid MealId) : ICommand<MenuDayDto>;

public sealed record GetAdminMenuCyclesQuery() : IQuery<IReadOnlyList<MenuCycleListItemDto>>;
public sealed record GetAdminMenuCycleQuery(Guid CycleId) : IQuery<MenuCycleDetailsDto>;
public sealed record PreviewAdminMenuCycleQuery(Guid CycleId, DateOnly From, int Days) : IQuery<IReadOnlyList<MenuCyclePreviewDayDto>>;
public sealed record CreateAdminFutureMenuCycleVersionCommand(Guid CycleId, DateOnly EffectiveFrom) : ICommand<MenuCycleDto>;
public sealed record ReviewMenuCycleCommand(Guid CycleId, VerificationStatus Decision, string? Notes) : ICommand<MenuCycleDto>;
public sealed record SetMenuCyclePausedCommand(Guid CycleId, bool IsPaused, string? Notes) : ICommand<MenuCycleDto>;
public sealed record SetProgramSlotRequirementsCommand(Guid ProgramId, IReadOnlyList<string> Slots) : ICommand<ProgramSlotRequirementsDto>;
public sealed record GetProgramSlotRequirementsQuery() : IQuery<IReadOnlyList<ProgramSlotRequirementsDto>>;
