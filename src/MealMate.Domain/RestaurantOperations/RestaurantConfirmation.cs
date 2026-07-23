using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantConfirmation : AuditableEntity
{
    public Guid DailyOrderId { get; set; }

    public RestaurantDailyOrder DailyOrder { get; set; } = default!;

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public Guid? ConfirmedByUserId { get; set; }

    public DateTime RespondedAtUtc { get; set; }

    public string? Notes { get; set; }
}
