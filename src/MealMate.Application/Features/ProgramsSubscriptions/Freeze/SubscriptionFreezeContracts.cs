using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public sealed record CustomerFreezeRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    bool AcknowledgeLostSelections = false);

public sealed record CustomerSubscriptionFreezePolicyDto(
    int DefaultDurationDays,
    int StartDelayDays,
    bool ExtendSubscriptionEnd,
    DateOnly EarliestAllowedStartDate,
    string MessageAr,
    string MessageEn,
    string? NoticeAr = null,
    string? NoticeEn = null);

public sealed record CustomerPendingFreezeDto(
    Guid RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    int FreezeDays,
    VerificationStatus Status);

public sealed record CustomerActiveFreezeDto(
    Guid RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    int FreezeDays,
    int FrozenServiceDaysCount,
    string Status,
    string StatusNameAr,
    bool IsCurrentlyActive,
    bool IsUpcoming,
    string? Reason,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc,
    IReadOnlyList<DateOnly> FrozenServiceDates,
    IReadOnlyList<DateOnly> CompensationDates,
    DateOnly SubscriptionEndDate,
    string SummaryAr,
    string SummaryEn);

public sealed record CustomerFreezeHistoryItemDto(
    Guid RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    int FreezeDays,
    string Status,
    string StatusNameAr,
    string? Reason,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc);

public sealed record CustomerSubscriptionFreezeDetailsDto(
    Guid SubscriptionId,
    string SubscriptionStatus,
    string SubscriptionStatusNameAr,
    bool HasFreeze,
    bool IsCurrentlyFrozen,
    CustomerActiveFreezeDto? CurrentFreeze,
    IReadOnlyList<CustomerFreezeHistoryItemDto> FreezeHistory,
    string NoticeAr,
    string NoticeEn);
