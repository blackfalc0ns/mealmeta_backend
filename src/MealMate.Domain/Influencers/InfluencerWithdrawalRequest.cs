using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Influencers;

public sealed class InfluencerWithdrawalRequest : AuditableEntity
{
    public Guid InfluencerProfileId { get; set; }

    public Money Amount { get; set; } = default!;

    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Requested;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public string ReferenceNumber { get; set; } = default!;

    public string WithdrawalMethod { get; set; } = "BankTransfer";

    public string? BankName { get; set; }

    public string? AccountHolderName { get; set; }

    public string? Iban { get; set; }

    public string? RejectionReason { get; set; }

    public ICollection<InfluencerWithdrawalApproval> Approvals { get; set; } = new List<InfluencerWithdrawalApproval>();
}
