using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignRestaurantEnrollment : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public int? CapacityOffered { get; set; }

    public ICollection<CampaignRestaurantApproval> Approvals { get; set; } = new List<CampaignRestaurantApproval>();
}
