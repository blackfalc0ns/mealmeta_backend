using MealMate.Domain.Common;

namespace MealMate.Domain.Offers;

public sealed class OfferProgram : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;
    public Guid ProgramId { get; set; }
}
