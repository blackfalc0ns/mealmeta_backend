using MealMate.Domain.Common;
using MealMate.Domain.Offers;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// An offer the customer explicitly saved to their list. Unique per (customer, offer).
/// </summary>
public sealed class CustomerSavedOffer : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Guid OfferId { get; set; }

    public Offer Offer { get; set; } = default!;
}
