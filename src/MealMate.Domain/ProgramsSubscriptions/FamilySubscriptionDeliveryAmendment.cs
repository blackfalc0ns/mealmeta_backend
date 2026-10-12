using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilySubscriptionDeliveryAmendment : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = default!;
    public long Version { get; set; }
    public Guid ActorUserId { get; set; }
    public DateOnly EffectiveFromDate { get; set; }
    public Guid AddressId { get; set; }
    public string AddressLabel { get; set; } = default!;
    public string AddressLine { get; set; } = default!;
    public Guid RegionId { get; set; }
    public Guid? BlockRegionId { get; set; }
    public string? Governorate { get; set; }
    public string? Area { get; set; }
    public string? Block { get; set; }
    public string AffectedDayIdsJson { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}
