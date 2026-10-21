using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class RestaurantBundlePricing : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public Guid BundleId { get; set; }

    public Bundle Bundle { get; set; } = default!;

    public Money ReferencePrice26 { get; set; } = default!;

    public Money ContractedSupplyPrice26 { get; set; } = default!;

    public PriceLockStatus LockStatus { get; set; } = PriceLockStatus.Draft;

    public DateTime EffectiveFromUtc { get; set; }

    public DateTime? EffectiveToUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public int RowVersion { get; set; }

    public string? ChangeRequestReason { get; set; }

    public Guid? PreviousPricingId { get; set; }

    public RestaurantBundlePricing? PreviousPricing { get; set; }
}
