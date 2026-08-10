using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>Programs and bundles that a restaurant has elected to offer.</summary>
public sealed class RestaurantProgramBundleOffer : AuditableEntity
{
    public Guid RestaurantId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid BundleId { get; set; }
    public bool IsActive { get; set; } = true;
}
