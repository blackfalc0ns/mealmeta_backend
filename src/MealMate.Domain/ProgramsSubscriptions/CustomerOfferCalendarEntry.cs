using MealMate.Domain.Common;
using MealMate.Domain.Offers;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>Lifecycle state of an offer scheduled on the customer's offer calendar.</summary>
public enum CustomerOfferCalendarEntryStatus
{
    Active = 1,
    Completed = 2
}

/// <summary>
/// Marks a day on the customer's offer calendar where a given offer was scheduled.
/// Unique per (customer, offer, scheduled date).
/// </summary>
public sealed class CustomerOfferCalendarEntry : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Guid OfferId { get; set; }

    public Offer Offer { get; set; } = default!;

    public DateOnly ScheduledDate { get; set; }

    public CustomerOfferCalendarEntryStatus Status { get; set; } = CustomerOfferCalendarEntryStatus.Active;

    /// <summary>"offer-detail" (default) | "post-payment-offer"</summary>
    public string? Source { get; set; }
}
