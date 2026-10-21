namespace MealMate.Application.Features.CalendarMeals;

public sealed record DaySettlementAllocation(
    decimal GrossValue,
    decimal DiscountAmount,
    decimal DeliveryShare,
    decimal NetValue);

public sealed record CompensationCalculationResult(
    decimal RefundAmount,
    decimal ApologyCreditAmount,
    int ApologyCreditValidityDays,
    DateTime ChoiceDeadlineUtc,
    DateTime RefundInitiationDeadlineUtc);

/// <summary>
/// Implements deterministic financial settlement and compensation rules for failed service days.
/// Ensures:
/// 1. Base compensation reflects the customer's net daily value after discounts and including proportional delivery fee.
/// 2. Apology credit is computed as a percentage (default 10%) of the net day value.
/// 3. Choice window (default 48h) is enforced before automated cash refund initiation (default 24h).
/// </summary>
public static class ServiceCompensationSettlement
{
    public const int DefaultChoiceWindowHours = 48;
    public const int DefaultRefundInitiationHours = 24;
    public const int DefaultApologyValidityDays = 90;
    public const decimal DefaultApologyCreditPercent = 10.0m;

    /// <summary>
    /// Calculates the proportional net value for an individual service day within a multi-day or multi-person purchase.
    /// Formula: Net = (Gross * (1 - DiscountPercent)) + (SharedDelivery / BoxCount)
    /// </summary>
    public static DaySettlementAllocation CalculateDayAllocation(
        decimal grossValue,
        decimal discountPercent,
        decimal sharedDeliveryShare)
    {
        if (grossValue < 0) throw new ArgumentOutOfRangeException(nameof(grossValue), "Gross value cannot be negative.");
        if (discountPercent < 0 || discountPercent > 1) throw new ArgumentOutOfRangeException(nameof(discountPercent), "Discount percent must be between 0 and 1.");
        if (sharedDeliveryShare < 0) throw new ArgumentOutOfRangeException(nameof(sharedDeliveryShare), "Delivery share cannot be negative.");

        var discountAmount = Math.Round(grossValue * discountPercent, 3, MidpointRounding.AwayFromZero);
        var netValue = Math.Round((grossValue - discountAmount) + sharedDeliveryShare, 3, MidpointRounding.AwayFromZero);

        return new DaySettlementAllocation(grossValue, discountAmount, sharedDeliveryShare, netValue);
    }

    /// <summary>
    /// Computes refund amount, apology credit, and statutory deadlines for a failed service commitment.
    /// </summary>
    public static CompensationCalculationResult CalculateCompensation(
        decimal netDayValue,
        DateTime failureConfirmedAtUtc,
        decimal apologyCreditPercent = DefaultApologyCreditPercent,
        int choiceWindowHours = DefaultChoiceWindowHours,
        int refundInitiationHours = DefaultRefundInitiationHours,
        int apologyValidityDays = DefaultApologyValidityDays)
    {
        if (netDayValue < 0) throw new ArgumentOutOfRangeException(nameof(netDayValue), "Net day value cannot be negative.");

        var apologyCredit = apologyCreditPercent > 0
            ? Math.Round(netDayValue * (apologyCreditPercent / 100.0m), 3, MidpointRounding.AwayFromZero)
            : 0.0m;

        var choiceDeadline = failureConfirmedAtUtc.AddHours(choiceWindowHours);
        var refundInitiationDeadline = choiceDeadline.AddHours(refundInitiationHours);

        return new CompensationCalculationResult(
            RefundAmount: netDayValue,
            ApologyCreditAmount: apologyCredit,
            ApologyCreditValidityDays: apologyValidityDays,
            ChoiceDeadlineUtc: choiceDeadline,
            RefundInitiationDeadlineUtc: refundInitiationDeadline);
    }
}
