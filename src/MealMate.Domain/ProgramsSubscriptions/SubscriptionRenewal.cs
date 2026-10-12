using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class SubscriptionRenewal : AuditableEntity
{
    public Guid PreviousSubscriptionId { get; set; }

    public Subscription PreviousSubscription { get; set; } = default!;

    public Guid? NewSubscriptionId { get; set; }

    public Subscription? NewSubscription { get; set; }

    public string Status { get; set; } = default!;

    public Guid? FamilyGroupId { get; set; }

    public Guid? FamilyMemberId { get; set; }

    public DateOnly? RequestedStartDate { get; set; }

    public Guid? RequestedAddressId { get; set; }

    public Guid? BatchId { get; set; }

    public long? BatchVersion { get; set; }

    public string? CheckoutFingerprint { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
