using MealMate.Domain.Common;

namespace MealMate.Domain.Offers;

public sealed class OfferAuditLog : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;

    public string ActorId { get; set; } = default!;
    public string Action { get; set; } = default!;

    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public string? DetailsJson { get; set; }
    public string? JustificationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
