using MealMate.Domain.ValueObjects;

namespace MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

public sealed record UpgradeFinancialInput(
    decimal OldCredit,
    decimal NewList,
    decimal ContinuingDiscountRate = 0m,
    bool IsContinuingDiscountEligible = false);

public sealed record UpgradeFinancialResult(
    decimal OldCredit,
    decimal NewList,
    decimal GrossDifference,
    decimal UpgradeDiscount,
    decimal AdditionalPayment,
    decimal NewUnearnedLiability);

/// <summary>
/// F06.7 & Section 7 of Financial Plan:
/// Pure calculation engine for subscription tier upgrades.
/// Enforces:
///   OldCredit = sum(paid_unearned_right[AmendableDays])
///   GrossDifference = max(0, NewList - OldCredit)
///   UpgradeDiscount = EligibleContinuingDiscount(GrossDifference)
///   AdditionalPayment = GrossDifference - UpgradeDiscount
///   NewUnearned = OldCredit + AdditionalPayment
/// Continuing discount applies ONLY to difference, temporary discounts (e.g. welcome) do not transfer.
/// No cancellation fee is ever charged on upgrade.
/// </summary>
public static class UpgradeFinancialCalculator
{
    public static UpgradeFinancialResult Calculate(UpgradeFinancialInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var oldCredit = MoneyRoundingPolicy.RoundFinalKwd(input.OldCredit);
        var newList = MoneyRoundingPolicy.RoundFinalKwd(input.NewList);
        var grossDifference = Math.Max(0m, newList - oldCredit);

        decimal upgradeDiscount = 0m;
        if (input.IsContinuingDiscountEligible && input.ContinuingDiscountRate > 0m && grossDifference > 0m)
        {
            upgradeDiscount = MoneyRoundingPolicy.RoundFinalKwd(grossDifference * input.ContinuingDiscountRate);
        }

        var additionalPayment = Math.Max(0m, grossDifference - upgradeDiscount);
        var newUnearned = oldCredit + additionalPayment;

        return new UpgradeFinancialResult(
            oldCredit,
            newList,
            grossDifference,
            upgradeDiscount,
            additionalPayment,
            newUnearned);
    }
}
