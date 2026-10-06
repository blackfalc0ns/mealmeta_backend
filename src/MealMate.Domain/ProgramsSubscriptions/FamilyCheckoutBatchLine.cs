using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// One participant line of a family checkout batch. Draft lines carry the
/// explicit checkout selection plus the member's current plan reference;
/// the money/address/restaurant snapshot freezes when the batch moves to
/// PaymentPending and then never tracks later profile/plan/address edits.
/// Amounts are real currency decimals; allocations must satisfy
/// NetPaidAmount = PriceBeforeFamilyDiscount - FamilyDiscountAllocated.
/// </summary>
public sealed class FamilyCheckoutBatchLine : BaseEntity
{
    public Guid BatchId { get; set; }
    public FamilyCheckoutBatch Batch { get; set; } = default!;

    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;

    /// <summary>Subscription created by this batch's payment; null until paid.</summary>
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    /// <summary>Explicit subscribe-now choice; deferred members keep their plan with this false.</summary>
    public bool CheckoutSelected { get; set; }

    // Frozen plan snapshot (nullable while the batch is still a draft).
    public Guid? ProgramId { get; set; }
    public Guid? BundleId { get; set; }
    public RestaurantTier? Tier { get; set; }
    public int? DurationDays { get; set; }
    public bool IncludeFriday { get; set; }
    public DateOnly? StartDate { get; set; }

    // Frozen delivery address snapshot (independent of the member's profile later changing).
    public Guid? DeliveryAddressId { get; set; }
    public string? AddressLabel { get; set; }
    public string? AddressLine { get; set; }
    public Guid? RegionId { get; set; }
    public string? Governorate { get; set; }
    public string? Area { get; set; }
    public string? Block { get; set; }
    public string? Street { get; set; }
    public string? Building { get; set; }
    public string? Unit { get; set; }
    public string? Floor { get; set; }

    // Frozen restaurant resolution used for capacity reservation.
    public Guid? ResolvedRestaurantId { get; set; }
    public string? MenuPlanVersionsJson { get; set; }

    // Frozen money.
    public decimal PriceBeforeFamilyDiscount { get; set; }
    public decimal FamilyDiscountAllocated { get; set; }
    public decimal NetPaidAmount { get; set; }
    public string CurrencyCode { get; set; } = "KWD";

    public DateTime CreatedAtUtc { get; set; }
}
