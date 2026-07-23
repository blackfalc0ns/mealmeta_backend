using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantFinance;

public sealed class TransferReceipt : AuditableEntity
{
    public Guid PayoutId { get; set; }

    public RestaurantPayout Payout { get; set; } = default!;

    public string StoragePath { get; set; } = default!;

    public DateTime UploadedAtUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }
}
