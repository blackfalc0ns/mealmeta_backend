using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignRestaurantApproval : AuditableEntity
{
    public Guid EnrollmentId { get; set; }

    public CampaignRestaurantEnrollment Enrollment { get; set; } = default!;

    public Guid ApprovedByUserId { get; set; }

    public VerificationStatus Decision { get; set; } = VerificationStatus.Approved;

    public string? Notes { get; set; }

    public DateTime DecidedAtUtc { get; set; }
}
