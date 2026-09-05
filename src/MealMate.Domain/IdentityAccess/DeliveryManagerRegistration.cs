using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class DeliveryManagerRegistration : AuditableEntity
{
    public Guid? SubmittedByUserId { get; set; }

    public Guid RestaurantId { get; set; }

    public string FullName { get; set; } = default!;

    public string Phone { get; set; } = default!;

    public string Email { get; set; } = default!;

    public string NationalId { get; set; } = default!;

    public DateTime? NationalIdExpiryUtc { get; set; }

    public DateTime? ContractExpiryUtc { get; set; }

    public DeliveryManagerCoverageType CoverageType { get; set; } = DeliveryManagerCoverageType.AllRestaurantCoverage;

    public string? CoverageScopeId { get; set; }

    public string? CoverageScopeName { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public string? RejectionReason { get; set; }

    public string? ChangeRequestNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ResultingUserId { get; set; }
}
