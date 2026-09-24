namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public enum CancellationFeeType
{
    None = 0,
    Percentage = 1,
    FixedAmount = 2
}

public sealed record CutoffPolicySettings(
    int CutoffLeadHours = 72,
    bool EnableFixedDailyHour = false,
    int FixedDailyHourKuwait = 14,
    int KitchenConfirmSlaHours = 24
);

public sealed record CancellationPolicySettings(
    CancellationFeeType FeeType = CancellationFeeType.Percentage,
    decimal FeeValue = 10.0m,
    bool RefundToCashWallet = true,
    decimal KitchenLockedRefundPercentage = 0.0m
);

public sealed record FreezePolicySettings(
    int LeadDaysNotice = 3,
    int MinDays = 3,
    int MaxDays = 14,
    int MaxOccurrences = 2,
    bool ExtendSubscriptionEnd = true
);

public sealed record OperationalPolicySettings(
    CutoffPolicySettings Cutoff,
    CancellationPolicySettings Cancellation,
    FreezePolicySettings Freeze)
{
    public OperationalPolicySettings() 
        : this(new CutoffPolicySettings(), new CancellationPolicySettings(), new FreezePolicySettings())
    {
    }
}
