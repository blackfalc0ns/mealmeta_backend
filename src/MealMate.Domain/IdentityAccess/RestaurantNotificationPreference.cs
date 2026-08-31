using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantNotificationPreference : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public string EventKey { get; set; } = default!;

    public bool EmailEnabled { get; set; }

    public bool PushEnabled { get; set; }
}
