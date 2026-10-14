using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class PromoCode : AuditableEntity
{
    public string Code { get; set; } = default!;

    public Percentage? DiscountPercent { get; set; }

    public Money? DiscountAmount { get; set; }

    public int? MaxUses { get; set; }

    public int? PerCustomerLimit { get; set; }

    public int UsedCount { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public string? EligibleDurationDaysJson { get; set; }

    /// <summary>Server-owned targeting contract: customer, program, bundle and subscription type.</summary>
    public string? TargetingJson { get; set; }
}
