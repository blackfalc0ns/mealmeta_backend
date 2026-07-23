using MealMate.Domain.Common;

namespace MealMate.Domain.Influencers;

public sealed class InfluencerCustomerLink : AuditableEntity
{
    public Guid InfluencerProfileId { get; set; }

    public Guid CustomerUserId { get; set; }

    public DateTime LinkedAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public string? AttributionSource { get; set; }
}
