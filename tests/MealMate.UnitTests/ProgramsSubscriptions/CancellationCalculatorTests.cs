using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.CustomerFinance;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class CancellationCalculatorTests
{
    [Fact]
    public void T05_SavedListAndDistinctDayUnion_ProducesRefund100_With14And6Split()
    {
        // Arrange: 26 days total. 7 executed (list 70, net 56, reprice diff 14), 
        // 3 locked (list 30, net 24, reprice diff 6), 16 unused (list 160, net 128)
        var days = new List<DayAllocationItem>();
        var baseDate = new DateOnly(2026, 10, 1);

        for (int i = 0; i < 7; i++)
        {
            days.Add(new DayAllocationItem(
                Guid.NewGuid(), baseDate.AddDays(i),
                SavedListPrice: 10m, EffectiveCashNet: 8m, UnrecoveredDiscountDelta: 2m,
                IsExecuted: true, IsLockedUnexecuted: false));
        }

        for (int i = 7; i < 10; i++)
        {
            days.Add(new DayAllocationItem(
                Guid.NewGuid(), baseDate.AddDays(i),
                SavedListPrice: 10m, EffectiveCashNet: 8m, UnrecoveredDiscountDelta: 2m,
                IsExecuted: false, IsLockedUnexecuted: true));
        }

        for (int i = 10; i < 26; i++)
        {
            days.Add(new DayAllocationItem(
                Guid.NewGuid(), baseDate.AddDays(i),
                SavedListPrice: 10m, EffectiveCashNet: 8m, UnrecoveredDiscountDelta: 0m,
                IsExecuted: false, IsLockedUnexecuted: false));
        }

        var input = new CancellationCalculationInput(
            AllDays: days,
            CancellationRate: 0.05m); // 5% fee on 160 = 8

        // Act
        var result = CancellationFinancialCalculator.Calculate(input);

        // Assert
        result.CountedDayIds.Should().HaveCount(10);
        result.UnusedDayIds.Should().HaveCount(16);
        result.ListUnused.Should().Be(160.000m);
        result.NetUnused.Should().Be(128.000m);
        result.NewRepriceCalculated.Should().Be(20.000m);
        result.AppliedReprice.Should().Be(20.000m);
        result.CancelFeeCalculated.Should().Be(8.000m);
        result.AppliedCancelFee.Should().Be(8.000m);
        result.RefundRaw.Should().Be(100.000m);
        result.RefundDue.Should().Be(100.000m);
        result.IsZeroRefund.Should().BeFalse();
        result.ExecutedRepriceShare.Should().Be(14.000m);
        result.LockedRepriceShare.Should().Be(6.000m);
    }

    [Fact]
    public void T06_WithNonGatewayOtherFee_ReducesRefundTo95()
    {
        var days = new List<DayAllocationItem>();
        var baseDate = new DateOnly(2026, 10, 1);

        for (int i = 0; i < 7; i++)
            days.Add(new DayAllocationItem(Guid.NewGuid(), baseDate.AddDays(i), 10m, 8m, 2m, true, false));
        for (int i = 7; i < 10; i++)
            days.Add(new DayAllocationItem(Guid.NewGuid(), baseDate.AddDays(i), 10m, 8m, 2m, false, true));
        for (int i = 10; i < 26; i++)
            days.Add(new DayAllocationItem(Guid.NewGuid(), baseDate.AddDays(i), 10m, 8m, 0m, false, false));

        var input = new CancellationCalculationInput(
            AllDays: days,
            CancellationRate: 0.05m,
            OtherFees: new[] { new OtherFeeItem("FEE-NON-GW", 5.000m) });

        var result = CancellationFinancialCalculator.Calculate(input);

        result.OtherFeesCalculated.Should().Be(5.000m);
        result.AppliedOtherFees.Should().Be(5.000m);
        result.RefundDue.Should().Be(95.000m);
    }

    [Fact]
    public void T07_DayExecutedAndLockedSimultaneously_IsCountedOnlyOnceInUnion()
    {
        var dayId = Guid.NewGuid();
        var days = new List<DayAllocationItem>
        {
            new DayAllocationItem(dayId, new DateOnly(2026, 10, 1), 10m, 8m, 2m, IsExecuted: true, IsLockedUnexecuted: true),
            new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 2), 10m, 8m, 0m, IsExecuted: false, IsLockedUnexecuted: false)
        };

        var input = new CancellationCalculationInput(days, CancellationRate: 0.05m);
        var result = CancellationFinancialCalculator.Calculate(input);

        result.CountedDayIds.Should().HaveCount(1);
        result.CountedDayIds.Should().Contain(dayId);
        result.NewRepriceCalculated.Should().Be(2.000m);
        result.UnusedDayIds.Should().HaveCount(1);
    }

    [Fact]
    public void T08_PlatformFailureDay_IsExcludedFromCountedDeductions()
    {
        // An unexecuted day that was locked, but failed due to platform emergency
        var platformFailureDay = new DayAllocationItem(
            Guid.NewGuid(), new DateOnly(2026, 10, 1),
            SavedListPrice: 10m, EffectiveCashNet: 8m, UnrecoveredDiscountDelta: 2m,
            IsExecuted: false, IsLockedUnexecuted: true, IsPlatformFailure: true);

        var normalUnusedDay = new DayAllocationItem(
            Guid.NewGuid(), new DateOnly(2026, 10, 2),
            SavedListPrice: 10m, EffectiveCashNet: 8m, UnrecoveredDiscountDelta: 0m,
            IsExecuted: false, IsLockedUnexecuted: false);

        var input = new CancellationCalculationInput(
            new[] { platformFailureDay, normalUnusedDay },
            CancellationRate: 0.05m);

        var result = CancellationFinancialCalculator.Calculate(input);

        // Platform failure day must not be in counted, and must not contribute to NewReprice
        result.CountedDayIds.Should().NotContain(platformFailureDay.DayId);
        result.NewRepriceCalculated.Should().Be(0m);
        result.RefundDue.Should().Be(8.000m - 0.500m); // 7.500m
    }

    [Fact]
    public void T37_Waterfall_AppliesRepriceThenOtherThenCancel_WithZeroRefundAndUnapplied3()
    {
        // available = 10, reprice = 8, other = 4, cancel = 1
        var day = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 1), 20m, 10m, 0m, false, false);
        var countedDay = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 9, 30), 10m, 8m, 8m, true, false);

        var input = new CancellationCalculationInput(
            AllDays: new[] { day, countedDay },
            CancellationRate: 0.05m, // on 20 = 1.000m
            OtherFees: new[] { new OtherFeeItem("FEE1", 4.000m) });

        var result = CancellationFinancialCalculator.Calculate(input);

        result.NetUnused.Should().Be(10.000m);
        result.AppliedReprice.Should().Be(8.000m);
        result.AppliedOtherFees.Should().Be(2.000m);
        result.UnappliedOtherFees.Should().Be(2.000m);
        result.AppliedCancelFee.Should().Be(0.000m);
        result.UnappliedCancelFee.Should().Be(1.000m);
        result.RefundDue.Should().Be(0.000m);
        result.IsZeroRefund.Should().BeTrue();
    }

    [Fact]
    public void T38_Waterfall_RepriceExceedsAvailable_LeavesUnapplied5AndZeroRefund()
    {
        // available = 10, reprice = 12, other = 2, cancel = 1
        var unused = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 1), 20m, 10m, 0m, false, false);
        var counted = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 9, 30), 15m, 10m, 12m, true, false);

        var input = new CancellationCalculationInput(
            AllDays: new[] { unused, counted },
            CancellationRate: 0.05m, // 1.000m
            OtherFees: new[] { new OtherFeeItem("FEE1", 2.000m) });

        var result = CancellationFinancialCalculator.Calculate(input);

        result.AppliedReprice.Should().Be(10.000m);
        result.UnappliedReprice.Should().Be(2.000m);
        result.AppliedOtherFees.Should().Be(0m);
        result.UnappliedOtherFees.Should().Be(2.000m);
        result.AppliedCancelFee.Should().Be(0m);
        result.UnappliedCancelFee.Should().Be(1.000m);
        result.RefundDue.Should().Be(0m);
        result.IsZeroRefund.Should().BeTrue();
    }

    [Fact]
    public void T39_Waterfall_SufficientFunds_LeavesZeroUnappliedAndRefund3()
    {
        // available = 10, reprice = 4, other = 2, cancel = 1 -> refund 3
        var unused = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 1), 20m, 10m, 0m, false, false);
        var counted = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 9, 30), 10m, 8m, 4m, true, false);

        var input = new CancellationCalculationInput(
            AllDays: new[] { unused, counted },
            CancellationRate: 0.05m, // 1.000m
            OtherFees: new[] { new OtherFeeItem("FEE1", 2.000m) });

        var result = CancellationFinancialCalculator.Calculate(input);

        result.AppliedReprice.Should().Be(4.000m);
        result.AppliedOtherFees.Should().Be(2.000m);
        result.AppliedCancelFee.Should().Be(1.000m);
        result.RefundDue.Should().Be(3.000m);
        result.IsZeroRefund.Should().BeFalse();
    }

    [Fact]
    public void T44_RepriceDistribution_WithLargestRemainder_Splits6AppliedInto4Point5And1Point5()
    {
        // Total reprice diff is 8 (6 executed + 2 locked). Applied reprice is 6.
        // Executed share = 6/8 * 6 = 4.500; Locked share = 2/8 * 6 = 1.500
        var executed = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 9, 29), 10m, 8m, 6m, true, false);
        var locked = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 9, 30), 10m, 8m, 2m, false, true);
        var unused = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 1), 10m, 6m, 0m, false, false);

        var input = new CancellationCalculationInput(
            AllDays: new[] { executed, locked, unused },
            CancellationRate: 0m);

        var result = CancellationFinancialCalculator.Calculate(input);

        result.AppliedReprice.Should().Be(6.000m);
        result.ExecutedRepriceShare.Should().Be(4.500m);
        result.LockedRepriceShare.Should().Be(1.500m);
    }

    [Fact]
    public void ApologyCreditSource_RestoresWithMinimumThirtyDaysValidity()
    {
        var unused = new DayAllocationItem(Guid.NewGuid(), new DateOnly(2026, 10, 1), 10m, 10m, 0m, false, false);
        var apologySourceId = Guid.NewGuid();

        var input = new CancellationCalculationInput(
            AllDays: new[] { unused },
            CancellationRate: 0m,
            SourceRights: new[]
            {
                new SourceRightItem(apologySourceId, FundingSourceType.ApologyCredit, 10.000m)
            });

        var result = CancellationFinancialCalculator.Calculate(input);

        result.RefundDue.Should().Be(10.000m);
        result.SourceAllocations.Should().HaveCount(1);
        var alloc = result.SourceAllocations[0];
        alloc.SourceId.Should().Be(apologySourceId);
        alloc.SourceType.Should().Be(FundingSourceType.ApologyCredit);
        alloc.Amount.Should().Be(10.000m);
        alloc.RestoredExpiryUtc.Should().NotBeNull();
        alloc.RestoredExpiryUtc.Value.Should().BeOnOrAfter(DateTime.UtcNow.AddDays(29));
    }
}
