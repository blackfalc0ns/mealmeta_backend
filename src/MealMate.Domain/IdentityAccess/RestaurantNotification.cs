using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantNotification : AuditableEntity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = default!;
    public string TitleAr { get; set; } = default!;
    public string TitleEn { get; set; } = default!;
    public string BodyAr { get; set; } = default!;
    public string BodyEn { get; set; } = default!;
    public string? Section { get; set; }
    public Guid? SubjectId { get; set; }
    public bool IsRead { get; set; }
    public DateTime SentAtUtc { get; set; }
}
