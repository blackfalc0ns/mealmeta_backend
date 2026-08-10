using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantReviewHistory : BaseEntity
{
    public Guid ReviewItemId { get; set; }
    public VerificationStatus FromStatus { get; set; }
    public VerificationStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
