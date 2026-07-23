using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Influencers;

public sealed class InfluencerWithdrawalApproval : AuditableEntity
{
    public Guid WithdrawalRequestId { get; set; }

    public InfluencerWithdrawalRequest WithdrawalRequest { get; set; } = default!;

    public Guid ApprovedByUserId { get; set; }

    public VerificationStatus Decision { get; set; } = VerificationStatus.Approved;

    public string? Notes { get; set; }

    public DateTime DecidedAtUtc { get; set; }
}
