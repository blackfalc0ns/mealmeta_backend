using MealMate.Domain.CustomerFinance;
using MealMate.Domain.ValueObjects;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

/// <summary>
/// F06.2 & F06.3:
/// Pure calculation engine for subscription cancellation financial settlement,
/// implementing the approved waterfall, reprice recovery, cancel fee calculation,
/// largest-remainder share distribution, and apology credit restoration rules.
/// </summary>
public static class CancellationFinancialCalculator
{
    public static CancellationCalculationResult Calculate(CancellationCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Group days: Counted = union(Executed, (Locked - PlatformFailure))
        var countedDays = new List<DayAllocationItem>();
        var countedDayIds = new HashSet<Guid>();

        var unusedDays = new List<DayAllocationItem>();
        var unusedDayIds = new HashSet<Guid>();

        foreach (var day in input.AllDays)
        {
            if (day.IsPreviouslyCancelled)
            {
                continue;
            }

            // Exclude platform failure from counted deductions:
            // These days are handled via 100% compensation and apology credits, not cancellation fees.
            if (day.IsPlatformFailure)
            {
                continue;
            }

            if (day.IsExecuted || day.IsLockedUnexecuted)
            {
                if (countedDayIds.Add(day.DayId))
                {
                    countedDays.Add(day);
                }
            }
            else
            {
                if (unusedDayIds.Add(day.DayId))
                {
                    unusedDays.Add(day);
                }
            }
        }

        var listUnused = MoneyRoundingPolicy.RoundFinalKwd(unusedDays.Sum(d => d.SavedListPrice));
        var netUnused = MoneyRoundingPolicy.RoundFinalKwd(unusedDays.Sum(d => d.EffectiveCashNet));

        var newReprice = MoneyRoundingPolicy.RoundFinalKwd(countedDays.Sum(d => d.UnrecoveredDiscountDelta));
        var cancelFee = MoneyRoundingPolicy.RoundFinalKwd(listUnused * input.CancellationRate);
        var otherFees = MoneyRoundingPolicy.RoundFinalKwd(input.OtherFees?.Sum(f => f.Amount) ?? 0m);

        var refundRaw = netUnused - newReprice - otherFees - cancelFee;

        // 2. Waterfall deductions
        var available = netUnused;

        var appliedReprice = Math.Min(available, newReprice);
        available -= appliedReprice;

        var appliedOther = Math.Min(available, otherFees);
        available -= appliedOther;

        var appliedCancel = Math.Min(available, cancelFee);
        available -= appliedCancel;

        var refundAfterAllocation = available;

        if (input.RemainingCashRightLimit.HasValue)
        {
            refundAfterAllocation = Math.Min(refundAfterAllocation, input.RemainingCashRightLimit.Value);
        }

        var refundDue = Math.Max(0m, refundAfterAllocation);

        var unappliedReprice = newReprice - appliedReprice;
        var unappliedOther = otherFees - appliedOther;
        var unappliedCancel = cancelFee - appliedCancel;

        // 3. Largest remainder split between executed and locked unexecuted reprice shares
        var executedDiff = countedDays.Where(d => d.IsExecuted).Sum(d => d.UnrecoveredDiscountDelta);
        var lockedDiff = countedDays.Where(d => !d.IsExecuted && d.IsLockedUnexecuted).Sum(d => d.UnrecoveredDiscountDelta);

        decimal executedRepriceShare = 0m;
        decimal lockedRepriceShare = 0m;

        if (appliedReprice > 0m && (executedDiff + lockedDiff) > 0m)
        {
            var items = new List<(string ItemKey, decimal Weight)>();
            if (executedDiff > 0m) items.Add(("executed", executedDiff));
            if (lockedDiff > 0m) items.Add(("locked", lockedDiff));

            if (items.Count == 1)
            {
                if (items[0].ItemKey == "executed") executedRepriceShare = appliedReprice;
                else lockedRepriceShare = appliedReprice;
            }
            else
            {
                var allocated = MoneyRoundingPolicy.AllocateKwd(appliedReprice, items);
                executedRepriceShare = allocated.First(a => a.ItemKey == "executed").AmountKwd;
                lockedRepriceShare = allocated.First(a => a.ItemKey == "locked").AmountKwd;
            }
        }

        // 4. Source lot allocations
        var sourceAllocations = new List<CalculatedRefundSourceAllocation>();
        if (input.SourceRights != null && input.SourceRights.Count > 0 && refundDue > 0m)
        {
            var remainingToAllocate = refundDue;
            foreach (var src in input.SourceRights)
            {
                if (remainingToAllocate <= 0m) break;
                if (src.RemainingCashRight <= 0m) continue;

                var take = Math.Min(remainingToAllocate, src.RemainingCashRight);
                DateTime? restoredExpiry = null;
                if (src.SourceType == FundingSourceType.ApologyCredit)
                {
                    // Enforce minimum 30 days validity upon refund
                    restoredExpiry = DateTime.UtcNow.AddDays(30);
                }

                sourceAllocations.Add(new CalculatedRefundSourceAllocation(
                    src.SourceId,
                    src.SourceType,
                    take,
                    restoredExpiry));

                remainingToAllocate -= take;
            }
        }

        var isZeroRefund = refundDue == 0m;

        return new CancellationCalculationResult(
            countedDayIds,
            unusedDayIds,
            listUnused,
            netUnused,
            newReprice,
            appliedReprice,
            unappliedReprice,
            cancelFee,
            appliedCancel,
            unappliedCancel,
            otherFees,
            appliedOther,
            unappliedOther,
            refundRaw,
            refundDue,
            executedRepriceShare,
            lockedRepriceShare,
            isZeroRefund,
            sourceAllocations);
    }
}
