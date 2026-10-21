using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

public enum FundingSourceType
{
    OriginalCash = 1,
    WalletCash = 2,
    PromoGrant = 3,
    ApologyCredit = 4,
}

public enum RefundSourceAllocationState
{
    Reserved = 1,
    Settled = 2,
    Released = 3,
    Cancelled = 4,
}

/// <summary>
/// F06.1 — Tracks the exact funding source and liability lot for each refund segment,
/// preventing double-refund of captured cash, non-refundable promo grant leakage,
/// and ensuring apology credits are restored with minimum 30-day validity.
/// </summary>
public sealed class RefundSourceAllocation : BaseEntity
{
    public Guid RefundOperationId { get; set; }
    public Guid OriginalSourceId { get; set; }
    public FundingSourceType SourceType { get; set; } = FundingSourceType.OriginalCash;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KWD";
    public RefundSourceAllocationState State { get; set; } = RefundSourceAllocationState.Reserved;
    public string? JournalRef { get; set; }
    public string? ProviderRef { get; set; }
    public DateTime? RestoredExpiryUtc { get; set; }
    public long ConcurrencyVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAtUtc { get; set; }

    public void Settle(string? journalRef = null, string? providerRef = null, DateTime? settledAtUtc = null)
    {
        if (State != RefundSourceAllocationState.Reserved)
            throw new InvalidOperationException($"Cannot settle allocation in state '{State}'. Must be Reserved.");
        State = RefundSourceAllocationState.Settled;
        JournalRef = journalRef ?? JournalRef;
        ProviderRef = providerRef ?? ProviderRef;
        SettledAtUtc = settledAtUtc ?? DateTime.UtcNow;
        ConcurrencyVersion++;
    }

    public void Release()
    {
        if (State != RefundSourceAllocationState.Reserved)
            throw new InvalidOperationException($"Cannot release allocation in state '{State}'. Must be Reserved.");
        State = RefundSourceAllocationState.Released;
        ConcurrencyVersion++;
    }
}
