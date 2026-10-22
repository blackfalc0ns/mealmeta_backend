using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
namespace MealMate.Application.Features.RestaurantOperations;

public sealed record ScheduledReductionDto(
    int TargetLimit,
    string EffectiveDate,
    string FirstSafeDate,
    string Reason);

public sealed record RestaurantCapacityOverviewDto(
    OperationalStatusDto Status,
    int DailyLimit,
    int ConfirmedToday,
    decimal NearLimitThreshold,
    string UpdatedAtIso,
    List<CapacityDayDto> Days,
    List<WeeklyDayScheduleDto> WeeklyDays,
    List<WorkingDayExceptionDto> Exceptions)
{
    public ScheduledReductionDto? ScheduledReduction { get; init; }
    public string ConfirmationMode { get; init; } = "manual";
}

public sealed record OperationalStatusDto(
    bool IsActive,
    int UpcomingWorkingDaysCount,
    string StatusLabelAr,
    string StatusLabelEn,
    string WorkingDaysSummaryAr,
    string WorkingDaysSummaryEn);

public sealed record CapacityDayDto(
    string Id,
    string DateIso,
    int WeekdayIndex,
    string WeekdayLabelAr,
    string WeekdayLabelEn,
    string DateLabel,
    int Confirmed,
    int Limit,
    bool IsWorkingDay,
    bool IsHoliday,
    string? HolidayReason,
    bool IsManualBusy,
    bool IsAutoBusy,
    string BusyKind,
    bool IsToday,
    bool IsPast,
    bool CanToggleManual);

public sealed record WorkingDaysScheduleDto(
    List<WeeklyDayScheduleDto> WeeklyDays,
    List<WorkingDayExceptionDto> Exceptions);

public sealed record WeeklyDayScheduleDto(
    int DayOfWeek,
    string DayNameAr,
    string DayNameEn,
    bool IsWorkingDay);

public sealed record WeeklyDayScheduleInputDto(
    int DayOfWeek,
    bool IsWorkingDay);

public sealed record WorkingDayExceptionDto(
    Guid Id,
    string DateIso,
    bool IsWorkingDay,
    string? Reason);

public sealed record GetRestaurantCapacityOverviewQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? RestaurantId = null) : IQuery<RestaurantCapacityOverviewDto>;

public sealed record UpdateBaseCapacityCommand(
    int NewCapacity,
    Guid? RestaurantId = null) : ICommand<RestaurantCapacityOverviewDto>;

public sealed record UpdateDateCapacityCommand(
    DateOnly Date,
    int NewCapacity,
    Guid? RestaurantId = null) : ICommand<CapacityDayDto>;

public sealed record SetDateManualBusyCommand(
    DateOnly Date,
    bool IsManualBusy,
    string? Reason = null,
    Guid? RestaurantId = null) : ICommand<CapacityDayDto>;

public sealed record GetWorkingDaysScheduleQuery(Guid? RestaurantId = null) : IQuery<WorkingDaysScheduleDto>;

public sealed record UpdateWorkingDaysScheduleCommand(
    List<WeeklyDayScheduleInputDto> Schedule,
    Guid? RestaurantId = null) : ICommand<WorkingDaysScheduleDto>;

public sealed record AddWorkingDayExceptionCommand(
    DateOnly Date,
    bool IsWorkingDay,
    string? Reason = null,
    Guid? RestaurantId = null) : ICommand<WorkingDayExceptionDto>;

public sealed record DeleteWorkingDayExceptionCommand(
    DateOnly Date,
    Guid? RestaurantId = null) : ICommand<bool>;

public sealed record CapacityReductionRequestDto(
    Guid Id,
    Guid RestaurantId,
    int CurrentLimit,
    int RequestedLimit,
    string RequestedEffectiveDate,
    string FirstSafeDate,
    string Reason,
    string Status,
    string SubmittedAtUtc,
    string? ReviewedAtUtc,
    string? AdminNotes);

public sealed record GetRestaurantCapacityReductionsQuery(
    Guid? RestaurantId = null) : IQuery<List<CapacityReductionRequestDto>>;

public sealed record SubmitCapacityReductionRequestCommand(
    int RequestedLimit,
    DateOnly RequestedEffectiveDate,
    string Reason,
    Guid? RestaurantId = null) : ICommand<CapacityReductionRequestDto>;

public sealed record WithdrawCapacityReductionRequestCommand(
    Guid RequestId,
    Guid? RestaurantId = null) : ICommand<bool>;
