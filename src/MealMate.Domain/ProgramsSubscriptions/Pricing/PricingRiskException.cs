using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

/// <summary>Maker/checker exception for a bounded campaign acquisition loss.</summary>
public sealed class PricingRiskException : BaseEntity
{
    public Guid QuoteId { get; set; }
    public Guid BudgetReservationId { get; set; }
    public Guid MakerUserId { get; set; }
    public Guid? CheckerUserId { get; set; }
    public decimal AuthorizedLossKwd { get; set; }
    public string Reason { get; set; } = default!;
    public string QuoteSnapshotHash { get; set; } = default!;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public bool? IsApproved { get; set; }
    public int RowVersion { get; set; }
}
