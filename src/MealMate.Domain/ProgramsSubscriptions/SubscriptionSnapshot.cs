using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// Append-only pricing snapshot captured at subscription creation/renewal.
/// Must not be soft-deleted (financial audit trail).
/// </summary>
public sealed class SubscriptionSnapshot : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; } = default!;

    public string SnapshotJson { get; set; } = "{}";

    public decimal A_t { get; set; }

    public Percentage ROfD { get; set; } = default!;

    public int NSnapshot { get; set; }

    public int LimitValue { get; set; }

    public int LMin { get; set; }

    public int LMax { get; set; }

    public string ReferencePricesJson { get; set; } = "[]";

    public string SupplyPricesJson { get; set; } = "[]";

    public string RuleVersionsJson { get; set; } = "{}";

    public DateTime CapturedAtUtc { get; set; }
}
