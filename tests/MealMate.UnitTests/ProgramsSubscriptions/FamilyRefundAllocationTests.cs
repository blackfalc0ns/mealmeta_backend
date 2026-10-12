using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyRefundAllocationTests
{
    [Fact]
    public void Kwd_minimum_contributions_conserve_fee_and_refund_units()
    {
        var payers = Enumerable.Range(1, 5).Select(i => (Id: Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"), Gross: 0.001m)).ToArray();

        var allocated = FamilyCancellationFeeAllocationService.Allocate(
            payers.ToDictionary(x => x.Id, x => x.Gross), 0.002m, 3);

        Assert.Equal(0.002m, allocated.Values.Sum());
        Assert.Equal(new[] { 0.001m, 0.001m, 0m, 0m, 0m }, payers.Select(x => allocated[x.Id]));
        Assert.All(payers, payer => Assert.InRange(allocated[payer.Id], 0m, payer.Gross));
        Assert.Equal(0.003m, payers.Sum(x => x.Gross - allocated[x.Id]));
    }

    [Fact]
    public void Equal_remainders_use_payer_guid_order_and_ignore_zero_gross_payer()
    {
        var high = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var zeroFinal = Guid.Parse("00000000-0000-0000-0000-000000000009");
        var input = new Dictionary<Guid, decimal> { [high] = 0.001m, [zeroFinal] = 0m, [low] = 0.001m };

        var allocated = FamilyCancellationFeeAllocationService.Allocate(input, 0.001m, 3);

        Assert.Equal(0.001m, allocated[low]);
        Assert.Equal(0m, allocated[high]);
        Assert.Equal(0m, allocated[zeroFinal]);
        Assert.Equal(0.001m, allocated.Values.Sum());
    }

    [Fact]
    public void Usd_allocation_is_bounded_and_reordering_does_not_change_result()
    {
        var first = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var third = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var ordered = new Dictionary<Guid, decimal> { [first] = 0.01m, [second] = 0.01m, [third] = 0.02m };
        var reversed = ordered.Reverse().ToDictionary(x => x.Key, x => x.Value);

        var firstResult = FamilyCancellationFeeAllocationService.Allocate(ordered, 0.02m, 2);
        var secondResult = FamilyCancellationFeeAllocationService.Allocate(reversed, 0.02m, 2);

        Assert.Equal(firstResult.OrderBy(x => x.Key), secondResult.OrderBy(x => x.Key));
        Assert.Equal(0.02m, firstResult.Values.Sum());
        Assert.Equal(0.02m, firstResult[first] + firstResult[second] + firstResult[third]);
        Assert.All(ordered, payer => Assert.InRange(firstResult[payer.Key], 0m, payer.Value));
    }

    [Fact]
    public void Zero_fee_and_zero_gross_are_conserved()
    {
        var payer = Guid.NewGuid();

        var noFee = FamilyCancellationFeeAllocationService.Allocate(new Dictionary<Guid, decimal> { [payer] = 1.234m }, 0m, 3);
        var noGross = FamilyCancellationFeeAllocationService.Allocate(new Dictionary<Guid, decimal> { [payer] = 0m }, 0m, 3);

        Assert.Equal(0m, noFee[payer]);
        Assert.Equal(0m, noGross[payer]);
    }

    [Theory]
    [InlineData(0.0011, 3)]
    [InlineData(-0.001, 3)]
    [InlineData(0.002, 3)]
    public void Invalid_currency_precision_or_fee_is_rejected(decimal fee, int digits)
    {
        var payer = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => FamilyCancellationFeeAllocationService.Allocate(
            new Dictionary<Guid, decimal> { [payer] = 0.001m }, fee, digits));
    }
}
