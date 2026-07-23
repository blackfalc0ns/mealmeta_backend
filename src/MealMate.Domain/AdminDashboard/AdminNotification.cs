using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.AdminDashboard;

public sealed class AdminNotification : AuditableEntity
{
    public Guid? UserId { get; set; }

    public string TitleAr { get; set; } = default!;

    public string TitleEn { get; set; } = default!;

    public string? BodyAr { get; set; }

    public string? BodyEn { get; set; }

    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    public bool IsRead { get; set; }

    public DateTime SentAtUtc { get; set; }

    public string? PayloadJson { get; set; }
}
