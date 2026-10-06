using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class DeliveryManagerNotification : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? RestaurantId { get; set; }

    public string Type { get; set; } = default!; // delivery-stop-delayed, trip-update, ...

    public string TitleAr { get; set; } = default!;

    public string TitleEn { get; set; } = default!;

    public string BodyAr { get; set; } = default!;

    public string BodyEn { get; set; } = default!;

    public string? Section { get; set; } // delivery, support

    public Guid? SubjectId { get; set; }

    public string? DeepLink { get; set; }

    public bool IsRead { get; set; }

    public DateTime SentAtUtc { get; set; }
}
