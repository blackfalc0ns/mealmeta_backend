using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public interface ICancellationConfirmationService
{
    Task<Result<CancellationConfirmationResultDto>> ConfirmCancellationAsync(
        CancellationConfirmationRequestDto request,
        Guid callerUserId,
        UserType callerUserType,
        CancellationToken cancellationToken = default);
}

public sealed record CancellationConfirmationRequestDto(
    Guid SubscriptionId,
    string? Reason,
    string IdempotencyKey,
    bool ConfirmZeroRefund = false);

public sealed record CancellationConfirmationResultDto(
    Guid DecisionId,
    Guid SubscriptionId,
    string OperationId,
    string CancellationState,
    decimal RefundDue,
    string RefundState,
    string FinancialState,
    bool IsZeroRefund,
    IReadOnlyList<CalculatedRefundSourceAllocation> SourceAllocations);

/// <summary>
/// F06.4:
/// Coordinates cancellation confirmation by verifying idempotency, acquiring necessary
/// operational locks, executing calendar and hold transitions, and writing an immutable
/// CancellationSettlementDecision with reserved RefundSourceAllocations.
/// </summary>
public sealed class CancellationConfirmationService : ICancellationConfirmationService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IOperationalPolicyService? _policyService;
    private readonly IApplicationLockManager? _lockManager;

    public CancellationConfirmationService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IOperationalPolicyService? policyService = null,
        IApplicationLockManager? lockManager = null)
    {
        _db = db;
        _clock = clock;
        _policyService = policyService;
        _lockManager = lockManager;
    }

    public async Task<Result<CancellationConfirmationResultDto>> ConfirmCancellationAsync(
        CancellationConfirmationRequestDto request,
        Guid callerUserId,
        UserType callerUserType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("cancellation.missing_idempotency_key", "An idempotency key is required.");
        }

        // 1. Idempotency check: if decision already exists for this operationId, return it immediately
        var existingDecision = await _db.CancellationSettlementDecisions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == request.IdempotencyKey, cancellationToken);

        if (existingDecision != null)
        {
            IReadOnlyList<CalculatedRefundSourceAllocation> savedAllocations = string.IsNullOrEmpty(existingDecision.SourceAllocationsJson)
                ? new List<CalculatedRefundSourceAllocation>()
                : JsonSerializer.Deserialize<List<CalculatedRefundSourceAllocation>>(existingDecision.SourceAllocationsJson)
                  ?? new List<CalculatedRefundSourceAllocation>();

            return new CancellationConfirmationResultDto(
                existingDecision.Id,
                existingDecision.SubscriptionId,
                existingDecision.OperationId,
                "Confirmed",
                existingDecision.RefundDue,
                existingDecision.IsZeroRefund ? "NotRequired" : "Queued",
                existingDecision.FinancialState,
                existingDecision.IsZeroRefund,
                savedAllocations);
        }

        // 2. Load Subscription
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == request.SubscriptionId, cancellationToken);

        if (subscription == null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{request.SubscriptionId}' was not found.");
        }

        // 3. Authorization check
        if (callerUserType == UserType.Customer)
        {
            var isOwner = subscription.CustomerUserId == callerUserId;
            var isFamilyManager = subscription.FamilyGroupId.HasValue &&
                await _db.FamilyGroups.AnyAsync(g => g.Id == subscription.FamilyGroupId.Value && g.ManagerCustomerUserId == callerUserId, cancellationToken);

            if (!isOwner && !isFamilyManager)
            {
                return Error.Forbidden("subscription.unauthorized", "You are not authorized to cancel this subscription.");
            }
        }

        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Completed or SubscriptionStatus.Expired)
        {
            return Error.Conflict("subscription.already_terminated", $"Subscription is already terminated with status '{subscription.Status}'.");
        }

        // 4. Lock operational aggregates
        if (_lockManager != null && subscription.FamilyGroupId.HasValue)
        {
            await _lockManager.LockFamilyGroupAsync(subscription.FamilyGroupId.Value, cancellationToken);
        }

        // 5. Load days and determine state
        var days = await _db.SubscriptionDays
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        var totalDays = subscription.DurationDays > 0 ? subscription.DurationDays : (days.Count > 0 ? days.Count : 1);
        var dailyListPrice = totalDays > 0 ? Math.Round(subscription.PaidAmount.Amount / totalDays, 3) : 0m;
        var dailyEffectiveCashNet = dailyListPrice;

        var today = _clock.TodayUtc;
        var lockThresholdDate = today.AddDays(1); // 24h lead window

        var dayAllocations = new List<DayAllocationItem>();
        var refundableDays = new List<SubscriptionDay>();

        foreach (var d in days)
        {
            var isExecuted = d.Status == SubscriptionDayStatus.Delivered;
            var isCancelled = d.Status == SubscriptionDayStatus.Cancelled;
            var isLocked = !isExecuted && !isCancelled && d.ServiceDate <= lockThresholdDate;

            dayAllocations.Add(new DayAllocationItem(
                d.Id,
                d.ServiceDate,
                dailyListPrice,
                dailyEffectiveCashNet,
                0m,
                isExecuted,
                isLocked,
                IsPlatformFailure: false,
                IsPreviouslyCancelled: isCancelled));

            if (!isExecuted && !isLocked && !isCancelled)
            {
                refundableDays.Add(d);
            }
        }

        // Calculate via pure calculator
        var policySettings = _policyService != null ? await _policyService.GetSettingsAsync(cancellationToken) : new OperationalPolicySettings();
        var cancellationRate = 0m;
        if (policySettings.Cancellation.FeeType == CancellationFeeType.Percentage)
        {
            cancellationRate = policySettings.Cancellation.FeeValue / 100m;
        }

        var calcInput = new CancellationCalculationInput(
            dayAllocations,
            cancellationRate,
            OtherFees: null,
            SourceRights: new[]
            {
                new SourceRightItem(subscription.Id, FundingSourceType.OriginalCash, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode)
            });

        var calcResult = CancellationFinancialCalculator.Calculate(calcInput);

        if (calcResult.IsZeroRefund && !request.ConfirmZeroRefund)
        {
            return Error.Conflict("cancellation.zero_refund_confirmation_required",
                "This cancellation results in a zero refund. Explicit confirmation is required.");
        }

        // 6. Transition operational days
        var cancelledDayIds = new List<Guid>();
        foreach (var day in refundableDays)
        {
            day.Status = SubscriptionDayStatus.Cancelled;
            day.ModifiedAtUtc = _clock.UtcNow;
            cancelledDayIds.Add(day.Id);
        }

        // 7. Release pending holds & clean selections
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        if (cancelledDayIds.Count > 0)
        {
            var mealSelections = await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (mealSelections.Count > 0) _db.DailyMealSelections.RemoveRange(mealSelections);

            var restaurantSelections = await _db.DailyRestaurantSelections
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (restaurantSelections.Count > 0) _db.DailyRestaurantSelections.RemoveRange(restaurantSelections);
        }

        // 8. Update subscription status
        var lockedDaysRemaining = days.Count(d => d.Status != SubscriptionDayStatus.Delivered && d.Status != SubscriptionDayStatus.Cancelled && d.ServiceDate <= lockThresholdDate);
        subscription.Status = lockedDaysRemaining > 0 ? SubscriptionStatus.CancellationPending : SubscriptionStatus.Cancelled;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        // 9. Save Decision snapshot
        var decision = new CancellationSettlementDecision
        {
            QuoteId = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            ConfirmedBy = callerUserId,
            ConfirmedAtUtc = _clock.UtcNow,
            Reason = request.Reason ?? "CustomerRequested",
            OperationId = request.IdempotencyKey,
            FinancialState = "PendingPosting",
            ListUnused = calcResult.ListUnused,
            NetUnused = calcResult.NetUnused,
            RepriceCalculated = calcResult.NewRepriceCalculated,
            RepriceApplied = calcResult.AppliedReprice,
            RepriceUnapplied = calcResult.UnappliedReprice,
            CancelFeeCalculated = calcResult.CancelFeeCalculated,
            CancelFeeApplied = calcResult.AppliedCancelFee,
            OtherFees = calcResult.OtherFeesCalculated,
            RefundDue = calcResult.RefundDue,
            IsZeroRefund = calcResult.IsZeroRefund,
            ExecutedRepriceShare = calcResult.ExecutedRepriceShare,
            LockedRepriceShare = calcResult.LockedRepriceShare,
            SourceAllocationsJson = JsonSerializer.Serialize(calcResult.SourceAllocations)
        };

        _db.CancellationSettlementDecisions.Add(decision);

        // 10. Reserve Source Allocations
        foreach (var alloc in calcResult.SourceAllocations)
        {
            var existingReservedTotal = await _db.RefundSourceAllocations
                .Where(x => x.OriginalSourceId == alloc.SourceId && (x.State == RefundSourceAllocationState.Reserved || x.State == RefundSourceAllocationState.Settled))
                .SumAsync(x => x.Amount, cancellationToken);

            var totalAvailableFromSource = calcInput.SourceRights?.FirstOrDefault(s => s.SourceId == alloc.SourceId)?.RemainingCashRight ?? subscription.PaidAmount.Amount;

            if (existingReservedTotal + alloc.Amount > totalAvailableFromSource)
            {
                return Error.Conflict("refund.source_already_reserved",
                    $"Funding source '{alloc.SourceId}' cannot be over-reserved. Available: {totalAvailableFromSource - existingReservedTotal}, Requested: {alloc.Amount}.");
            }

            var sourceAlloc = new RefundSourceAllocation
            {
                RefundOperationId = decision.Id,
                OriginalSourceId = alloc.SourceId,
                SourceType = alloc.SourceType,
                Amount = alloc.Amount,
                Currency = subscription.PaidAmount.CurrencyCode,
                State = RefundSourceAllocationState.Reserved,
                RestoredExpiryUtc = alloc.RestoredExpiryUtc,
                CreatedAtUtc = _clock.UtcNow
            };
            _db.RefundSourceAllocations.Add(sourceAlloc);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CancellationConfirmationResultDto(
            decision.Id,
            subscription.Id,
            decision.OperationId,
            "Confirmed",
            decision.RefundDue,
            decision.IsZeroRefund ? "NotRequired" : "Queued",
            decision.FinancialState,
            decision.IsZeroRefund,
            calcResult.SourceAllocations);
    }
}
