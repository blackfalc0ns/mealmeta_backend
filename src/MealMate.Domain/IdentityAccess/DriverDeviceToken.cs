using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class DriverDeviceToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? DriverProfileId { get; set; }

    public string Token { get; set; } = default!;

    public MobilePlatform Platform { get; set; } = MobilePlatform.Android;

    public string? DeviceId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime LastSeenAtUtc { get; set; }
}
