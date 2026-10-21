using FluentAssertions;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Finance.Ledger;

public sealed class JournalInvariantTests
{
    [Fact]
    public void RejectsAnUnbalancedJournalInFils()
    {
        var result = JournalInvariant.ValidateLines([
            Line("1000", debit: 10m),
            Line("4000", credit: 9.999m),
        ]);

        result.ErrorCode.Should().Be("ledger.journal_unbalanced");
    }

    [Fact]
    public void RejectsMixedCurrencies()
    {
        var result = JournalInvariant.ValidateLines([
            Line("1000", debit: 10m, currency: "KWD"),
            Line("4000", credit: 10m, currency: "KWD"),
        ]);

        result.IsValid.Should().BeTrue();
        JournalInvariant.ValidateLines([
            Line("1000", debit: 10m, currency: "USD"),
            Line("4000", credit: 10m, currency: "USD"),
        ]).ErrorCode.Should().Be("ledger.currency_unsupported");
    }

    [Fact]
    public void RejectsZeroNegativeDualSidedAndUnquantizedLineAmounts()
    {
        JournalInvariant.ValidateLines([Line("1000", debit: 0m), Line("4000", credit: 0m)])
            .ErrorCode.Should().Be("ledger.line_amount_invalid");
        JournalInvariant.ValidateLines([Line("1000", debit: -1m), Line("4000", credit: -1m)])
            .ErrorCode.Should().Be("ledger.line_amount_invalid");
        JournalInvariant.ValidateLines([Line("1000", debit: 1m, credit: 1m), Line("4000", credit: 2m)])
            .ErrorCode.Should().Be("ledger.line_sides_invalid");
        JournalInvariant.ValidateLines([Line("1000", debit: 1.0001m), Line("4000", credit: 1.0001m)])
            .ErrorCode.Should().Be("ledger.line_precision_invalid");
    }

    [Fact]
    public void ControlAccountRequiresPartyAndHeaderNeedsTwoLines()
    {
        var control = Line("1100", debit: 1m, requiresParty: true);
        var other = Line("4000", credit: 1m);

        JournalInvariant.ValidateLines([control, other]).ErrorCode.Should().Be("ledger.control_party_required");
        control.PartyId = Guid.NewGuid();
        JournalInvariant.ValidateLines([control]).ErrorCode.Should().Be("ledger.lines_required");
    }

    [Fact]
    public void RejectsParentAccountButAllowsPostableLeafWithParent()
    {
        var parent = Line("1000", debit: 1m);
        parent.Account.Children.Add(new ChartOfAccount { Code = "1001", NameAr = "child", NameEn = "child" });
        var other = Line("4000", credit: 1m);
        JournalInvariant.ValidateLines([parent, other]).ErrorCode.Should().Be("ledger.account_not_postable");

        var leaf = Line("1001", debit: 1m);
        leaf.Account.ParentId = Guid.NewGuid();
        JournalInvariant.ValidateLines([leaf, other]).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AcceptsBalancedKwdLinesWithRequiredParty()
    {
        var result = JournalInvariant.ValidateLines([
            Line("1100", debit: 123.456m, requiresParty: true, partyId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            Line("4000", credit: 123.456m),
        ]);

        result.IsValid.Should().BeTrue();
        result.CurrencyCode.Should().Be("KWD");
        result.TotalDebit.Should().Be(123.456m);
        result.TotalCredit.Should().Be(123.456m);
    }

    private static JournalEntryLine Line(string code, decimal? debit = null, decimal? credit = null,
        string currency = "KWD", bool requiresParty = false, Guid? partyId = null)
    {
        var account = new ChartOfAccount
        {
            Code = code,
            NameAr = code,
            NameEn = code,
            AccountType = AccountType.Asset,
            NormalBalance = AccountNormalBalance.Debit,
            IsPostable = true,
            RequiresParty = requiresParty,
        };
        return new JournalEntryLine
        {
            Account = account,
            AccountId = account.Id,
            Debit = debit is null ? null : new Money(debit.Value, currency),
            Credit = credit is null ? null : new Money(credit.Value, currency),
            PartyId = partyId,
        };
    }
}
