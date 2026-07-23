using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.AdminDashboard;

public sealed class ProfitabilityAlert : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid ProgramId { get; set; }

    public Guid BundleId { get; set; }

    public Guid? ClassificationRunId { get; set; }

    public decimal ReferenceDailyBox { get; set; }

    public decimal DailySupplyGross { get; set; }

    public decimal MeanReference { get; set; }

    public decimal CustomerDailyPrice { get; set; }

    public decimal ExpectedProfit { get; set; }

    public decimal MinimumProfit { get; set; }

    public ProfitabilityAlertStatus Status { get; set; } = ProfitabilityAlertStatus.Open;

    public Guid? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public string? Reason { get; set; }
}
