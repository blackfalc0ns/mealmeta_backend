using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed record JournalValidation(bool IsValid, string? ErrorCode, string? CurrencyCode,
    decimal TotalDebit, decimal TotalCredit)
{
    public static JournalValidation Invalid(string code) => new(false, code, null, 0m, 0m);
}

public static class JournalInvariant
{
    private const decimal MaxNumeric18Scale3 = 999_999_999_999_999.999m;

    public static JournalValidation ValidateLines(IReadOnlyCollection<JournalEntryLine> lines,
        DateOnly? businessDate = null, Guid? legalEntityId = null)
    {
        if (lines.Count < 2) return JournalValidation.Invalid("ledger.lines_required");

        string? currency = null;
        decimal debit = 0m;
        decimal credit = 0m;
        foreach (var line in lines)
        {
            if ((line.Debit is null) == (line.Credit is null))
                return JournalValidation.Invalid("ledger.line_sides_invalid");

            var amount = line.Debit?.Amount ?? line.Credit!.Amount;
            var lineCurrency = line.Debit?.CurrencyCode ?? line.Credit!.CurrencyCode;
            if (amount <= 0m || amount > MaxNumeric18Scale3)
                return JournalValidation.Invalid("ledger.line_amount_invalid");
            if (decimal.Round(amount, 3, MidpointRounding.ToZero) != amount)
                return JournalValidation.Invalid("ledger.line_precision_invalid");
            if (string.IsNullOrWhiteSpace(lineCurrency) || lineCurrency.Length != 3)
                return JournalValidation.Invalid("ledger.currency_invalid");
            if (!string.Equals(lineCurrency, "KWD", StringComparison.Ordinal))
                return JournalValidation.Invalid("ledger.currency_unsupported");
            if (currency is not null && currency != lineCurrency)
                return JournalValidation.Invalid("ledger.currency_mismatch");
            currency = lineCurrency;

            var account = line.Account;
            if (account is null || !account.IsActive || !account.IsPostable || account.Children.Count != 0)
                return JournalValidation.Invalid("ledger.account_not_postable");
            if (businessDate is { } date && ((account.EffectiveFrom is { } from && date < from)
                || (account.EffectiveToExclusive is { } to && date >= to)))
                return JournalValidation.Invalid("ledger.account_not_effective");
            if (legalEntityId is { } entityId && account.LegalEntityId != entityId)
                return JournalValidation.Invalid("ledger.account_entity_mismatch");
            if (account.RequiresParty && (line.PartyId is null || line.PartyId == Guid.Empty))
                return JournalValidation.Invalid("ledger.control_party_required");

            if (line.Debit is not null) debit += amount;
            else credit += amount;
            if (debit > MaxNumeric18Scale3 || credit > MaxNumeric18Scale3)
                return JournalValidation.Invalid("ledger.total_amount_invalid");
        }

        if (debit != credit) return JournalValidation.Invalid("ledger.journal_unbalanced");
        return new JournalValidation(true, null, currency, debit, credit);
    }
}
