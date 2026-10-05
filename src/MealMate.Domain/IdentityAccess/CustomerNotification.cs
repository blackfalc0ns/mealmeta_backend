using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Customer in-app notification inbox row for a real business event. Deliberately
/// separate from <see cref="CustomerPushDeliveryLog"/>: the inbox entry exists even when
/// the customer owns no device token or FCM delivery failed, while the delivery log
/// only records successful push sends. Unread state is ReadAtUtc == null.
/// </summary>
public sealed class CustomerNotification : AuditableEntity
{
    public Guid UserId { get; set; }

    public string EventKey { get; set; } = default!;

    public Guid SubjectId { get; set; }

    public Guid? SubscriptionId { get; set; }

    public string TitleAr { get; set; } = default!;

    public string TitleEn { get; set; } = default!;

    public string BodyAr { get; set; } = default!;

    public string BodyEn { get; set; } = default!;

    public string? DeepLink { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
