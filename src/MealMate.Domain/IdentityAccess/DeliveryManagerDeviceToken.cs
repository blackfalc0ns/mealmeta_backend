using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class DeliveryManagerDeviceToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? RestaurantId { get; set; }

    public string Token { get; set; } = default!;

    public MobilePlatform Platform { get; set; } = MobilePlatform.Android;

    public string? DeviceId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime LastSeenAtUtc { get; set; }
}
