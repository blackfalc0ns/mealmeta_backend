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
    int KitchenConfirmSlaHours = 24,
    int? CutoffLeadMinutes = null,
    int? KitchenConfirmSlaMinutes = null,
    int FirstDaySelectionWindowHours = 24
)
{
    public int EffectiveLeadMinutes => CutoffLeadMinutes ?? CutoffLeadHours * 60;
    public int EffectiveConfirmationMinutes => KitchenConfirmSlaMinutes ?? KitchenConfirmSlaHours * 60;

    /// <summary>
    /// Approved first-day exception: the first delivery day's meal-selection window runs from the
    /// verified payment time plus this many hours (default 24), shrinking the usual 72h lead, and
    /// applies to the first service day only. Later days keep the standard cutoff.
    /// </summary>
    public int EffectiveFirstDayWindowHours => Math.Clamp(FirstDaySelectionWindowHours, 1, 72);
}

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
    bool ExtendSubscriptionEnd = true,
    int MinGapDays = 0,
    int? MaxTotalDays = null,
    bool Enabled = true,
    int? FreezeDeadlineHours = null
)
{
    public int EffectiveFreezeDeadlineHours => FreezeDeadlineHours ?? (LeadDaysNotice * 24);
}

public sealed record AutoSelectionPolicySettings(bool Enabled = true, int MinutesBeforeCutoff = 0);
public sealed record RenewalPolicySettings(bool Enabled = true, int? AvailableDaysBeforeEnd = null);
public sealed record DeliveryDelayPolicySettings(int SevereDelayThresholdMinutes = 15);
public sealed record PolicyPublication(long Version, Guid ActorId, DateTime PublishedAtUtc, string Reason,
    OperationalPolicySettings Before, OperationalPolicySettings After);

public sealed record OperationalPolicySettings(
    CutoffPolicySettings Cutoff,
    CancellationPolicySettings Cancellation,
    FreezePolicySettings Freeze,
    AutoSelectionPolicySettings? AutoSelection = null,
    RenewalPolicySettings? Renewal = null,
    DeliveryDelayPolicySettings? DeliveryDelay = null,
    long Version = 0,
    string? ChangeReason = null,
    Guid? PublishedBy = null,
    DateTime? PublishedAtUtc = null)
{
    public OperationalPolicySettings() 
        : this(new CutoffPolicySettings(), new CancellationPolicySettings(), new FreezePolicySettings())
    {
    }
}
