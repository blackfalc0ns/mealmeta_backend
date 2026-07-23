using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Influencers;

/// <summary>
/// Append-only influencer payout history record.
/// </summary>
public sealed class InfluencerPayoutHistory : BaseEntity
{
    public Guid InfluencerProfileId { get; set; }

    public Guid? WithdrawalRequestId { get; set; }

    public InfluencerWithdrawalRequest? WithdrawalRequest { get; set; }

    public Money Amount { get; set; } = default!;

    public DateTime PaidAtUtc { get; set; }

    public string? BankReference { get; set; }

    public string? ReceiptPath { get; set; }
}
