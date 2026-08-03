using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class SubscriptionOperation : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; } = default!;

    public string OperationType { get; set; } = default!;

    public SubscriptionStatus? PreviousStatus { get; set; }

    public SubscriptionStatus NewStatus { get; set; }

    public string? Reason { get; set; }

    public Guid PerformedByUserId { get; set; }

    public UserType PerformedByUserType { get; set; }

    public string? CorrelationId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
