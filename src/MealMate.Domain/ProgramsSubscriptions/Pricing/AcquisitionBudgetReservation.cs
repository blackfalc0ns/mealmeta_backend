using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

public enum AcquisitionBudgetReservationStatus : byte
{
    Reserved = 1,
    Consumed = 2,
    Released = 3,
    Expired = 4,
}

/// <summary>One durable campaign-budget reservation for a unique accepted quote.</summary>
public sealed class AcquisitionBudgetReservation : BaseEntity
{
    public Guid BudgetId { get; set; }
    public AcquisitionBudget Budget { get; set; } = default!;
    public Guid QuoteId { get; set; }
    public decimal AmountKwd { get; set; }
    public string QuoteSnapshotHash { get; set; } = default!;
    public int ReservedSales { get; set; } = 1;
    public AcquisitionBudgetReservationStatus Status { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RowVersion { get; set; }
}
