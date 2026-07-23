using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantContractTermination : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? ProgramId { get; set; }

    public Guid? BundleId { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? EffectiveAtUtc { get; set; }

    public string? Reason { get; set; }
}
