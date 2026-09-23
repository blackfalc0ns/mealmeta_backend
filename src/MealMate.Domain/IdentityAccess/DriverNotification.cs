using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class DriverNotification : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? DriverProfileId { get; set; }

    public string Type { get; set; } = default!; // box-assigned, order-ready, trip-update, emergency-alert, shift-kpi

    public string TitleAr { get; set; } = default!;

    public string TitleEn { get; set; } = default!;

    public string BodyAr { get; set; } = default!;

    public string BodyEn { get; set; } = default!;

    public string? Section { get; set; } // orders, shift, support

    public Guid? SubjectId { get; set; }

    public string? DeepLink { get; set; }

    public bool IsRead { get; set; }

    public DateTime SentAtUtc { get; set; }
}
