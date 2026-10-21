using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

/// <summary>Unique record proving that a quote was accepted by one checkout operation.</summary>
public sealed class PricingQuoteConsumption : BaseEntity
{
    public Guid QuoteId { get; set; }
    public PricingQuote Quote { get; set; } = default!;
    public string? ConsumerType { get; set; }
    public Guid? ConsumerId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public string AcceptedSnapshotHash { get; set; } = default!;
}
