using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Domain.Offers.Events;

public sealed record OfferStatusChangedDomainEvent(
    Guid OfferId,
    OfferStatus OldStatus,
    OfferStatus NewStatus,
    string ActorId,
    string Reason
) : DomainEvent;

public sealed record OfferRedeemedDomainEvent(
    Guid OfferId,
    Guid RedemptionId,
    Guid CustomerId,
    Guid SubscriptionId,
    decimal DiscountAmount,
    DateTime RedeemedAtUtc
) : DomainEvent;
