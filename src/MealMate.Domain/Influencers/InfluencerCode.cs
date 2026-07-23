using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Influencers;

public sealed class InfluencerCode : AuditableEntity
{
    public Guid InfluencerProfileId { get; set; }

    public string Code { get; set; } = default!;

    public Percentage? DiscountPercent { get; set; }

    public Money? DiscountAmount { get; set; }

    public Percentage CommissionRate { get; set; } = default!;

    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public string? EligibleDurationsJson { get; set; }
}
