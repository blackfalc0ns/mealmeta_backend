using MealMate.Domain.CustomerFinance;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public sealed record DayAllocationItem(
    Guid DayId,
    DateOnly ServiceDate,
    decimal SavedListPrice,
    decimal EffectiveCashNet,
    decimal UnrecoveredDiscountDelta,
    bool IsExecuted,
    bool IsLockedUnexecuted,
    bool IsPlatformFailure = false,
    bool IsPreviouslyCancelled = false);

public sealed record OtherFeeItem(
    string FeeId,
    decimal Amount,
    string Description = "");

public sealed record SourceRightItem(
    Guid SourceId,
    FundingSourceType SourceType,
    decimal RemainingCashRight,
    string Currency = "KWD");

public sealed record CancellationCalculationInput(
    IReadOnlyList<DayAllocationItem> AllDays,
    decimal CancellationRate,
    IReadOnlyList<OtherFeeItem>? OtherFees = null,
    IReadOnlyList<SourceRightItem>? SourceRights = null,
    decimal? RemainingCashRightLimit = null);

public sealed record CalculatedRefundSourceAllocation(
    Guid SourceId,
    FundingSourceType SourceType,
    decimal Amount,
    DateTime? RestoredExpiryUtc = null);

public sealed record CancellationCalculationResult(
    IReadOnlySet<Guid> CountedDayIds,
    IReadOnlySet<Guid> UnusedDayIds,
    decimal ListUnused,
    decimal NetUnused,
    decimal NewRepriceCalculated,
    decimal AppliedReprice,
    decimal UnappliedReprice,
    decimal CancelFeeCalculated,
    decimal AppliedCancelFee,
    decimal UnappliedCancelFee,
    decimal OtherFeesCalculated,
    decimal AppliedOtherFees,
    decimal UnappliedOtherFees,
    decimal RefundRaw,
    decimal RefundDue,
    decimal ExecutedRepriceShare,
    decimal LockedRepriceShare,
    bool IsZeroRefund,
    IReadOnlyList<CalculatedRefundSourceAllocation> SourceAllocations);
