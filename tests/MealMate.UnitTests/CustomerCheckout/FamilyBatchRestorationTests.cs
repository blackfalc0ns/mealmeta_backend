using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyBatchRestorationTests
{
    [Fact]
    public void Empty_selection_reports_no_selection_and_cannot_charge()
    {
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft };
        var quote = new FamilyQuoteResult([], 0m, 0m, 0m, 0m, "KWD", false);
        var result = FamilyCheckoutGate.Evaluate(group, [], quote);
        Assert.False(result.CanCheckout);
        Assert.Equal("family.checkout_no_selection", result.BlockReason);
    }

    [Theory]
    [InlineData("KWD", 3, 0.001)]
    [InlineData("USD", 2, 0.01)]
    public void Largest_remainder_preserves_invoice_and_stable_member_order(string currency, int digits, decimal unit)
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var lines = new[] { new FamilyQuoteLine(second, "Second", unit, 0, 0, 0), new FamilyQuoteLine(first, "First", unit, 0, 0, 0) };
        var allocations = new FamilyPaymentAllocationService().Allocate(lines, unit, digits);
        Assert.Equal(digits, FamilyPaymentAllocationService.CurrencyMinorDigits(currency));
        Assert.Equal(unit, allocations.Sum(x => x.NetPaidAmount));
        Assert.Equal(unit, allocations.Single(x => x.MemberId == first).FamilyDiscountAllocated);
        Assert.Equal(0m, allocations.Single(x => x.MemberId == first).NetPaidAmount);
        Assert.All(allocations, x => Assert.Equal(x.PriceBeforeFamilyDiscount, x.FamilyDiscountAllocated + x.NetPaidAmount));
    }
}
