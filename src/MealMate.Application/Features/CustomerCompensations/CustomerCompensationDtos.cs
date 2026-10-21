using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.CustomerCompensations;

public sealed record RefundBreakdownDto(
    decimal CardRefundAmount,
    decimal WalletRefundAmount,
    decimal ApologyCreditAmount);

public sealed record CustomerCompensationSummaryDto(
    Guid CompensationId,
    Guid SubscriptionId,
    Guid? BeneficiaryFamilyMemberId,
    DateOnly FailedServiceDate,
    string Reason,
    decimal TotalAmount,
    string Currency,
    RefundBreakdownDto RefundBreakdown,
    DateTime FailureConfirmedAtUtc,
    DateTime ChoiceDeadlineUtc,
    DateTime ServerTimeUtc,
    long RemainingSeconds,
    CustomerCompensationStatus Status,
    DateOnly? SelectedAlternativeDate);

public sealed record EligibleAlternativeDateDto(
    DateOnly Date,
    int DayOfWeek,
    string DayNameAr,
    string DayNameEn,
    bool IsAvailable,
    int AvailableCapacity);

public sealed record ChooseCompensationRequest(
    string Choice, // "CashRefund" | "AlternativeDay"
    DateOnly? TargetDate = null);

public sealed record ChooseCompensationResultDto(
    Guid CompensationId,
    CustomerCompensationStatus Status,
    string StatusLabel,
    DateOnly? SelectedAlternativeDate,
    string Message,
    DateTime ServerTimeUtc);
