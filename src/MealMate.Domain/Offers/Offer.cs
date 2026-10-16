using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Offers;

public sealed class Offer : AuditableEntity
{
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? BannerImageUrl { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Draft;
    public OfferType Type { get; set; } = OfferType.StandardDiscount;
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public OfferFundingMode FundingMode { get; set; } = OfferFundingMode.PlatformFull;

    public Percentage? DiscountRate { get; set; }
    public Money? DiscountAmount { get; set; }
    public Percentage? PlatformShareRate { get; set; }
    public Percentage? RestaurantShareRate { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }

    public Money? MinOrderAmount { get; set; }
    public int? MinSubscriptionDays { get; set; }
    public int? MaxSubscriptionDays { get; set; }

    public int? TotalUsageLimit { get; set; }
    public int CurrentUsageCount { get; set; }
    public int PerCustomerLimit { get; set; } = 1;

    public CustomerSegmentFilter CustomerSegment { get; set; } = CustomerSegmentFilter.All;
    public string? AllowedGovernoratesJson { get; set; }

    public bool AllowPromoCodeStacking { get; set; } = false;

    public string? PromoCode { get; set; }
    public string? TermsAndConditionsAr { get; set; }
    public string? TermsAndConditionsEn { get; set; }

    public Guid? CampaignId { get; set; }

    public ICollection<OfferProgram> EligiblePrograms { get; set; } = new List<OfferProgram>();
    public ICollection<OfferBundle> EligibleBundles { get; set; } = new List<OfferBundle>();
    public ICollection<OfferPackageTarget> PackageTargets { get; set; } = new List<OfferPackageTarget>();
    public ICollection<OfferRestaurant> EligibleRestaurants { get; set; } = new List<OfferRestaurant>();
    public ICollection<OfferRedemption> Redemptions { get; set; } = new List<OfferRedemption>();
    public ICollection<OfferAuditLog> AuditLogs { get; set; } = new List<OfferAuditLog>();

    public Result CanTransitionTo(OfferStatus targetStatus)
    {
        if (Status == targetStatus)
        {
            return Result.Failure($"Offer is already in state '{targetStatus}'.");
        }

        var isAllowed = Status switch
        {
            OfferStatus.Draft => targetStatus is OfferStatus.Scheduled or OfferStatus.Cancelled,
            OfferStatus.Scheduled => targetStatus is OfferStatus.Active or OfferStatus.Cancelled,
            OfferStatus.Active => targetStatus is OfferStatus.Paused or OfferStatus.Stopped or OfferStatus.Ended,
            OfferStatus.Paused => targetStatus is OfferStatus.Active or OfferStatus.Stopped or OfferStatus.Cancelled,
            OfferStatus.Stopped => targetStatus is OfferStatus.Ended,
            OfferStatus.Ended => false,
            OfferStatus.Cancelled => false,
            _ => false
        };

        return isAllowed
            ? Result.Success()
            : Result.Failure($"Transition from '{Status}' to '{targetStatus}' is not allowed.");
    }

    public void TransitionTo(OfferStatus targetStatus, string actorId, string reason)
    {
        var check = CanTransitionTo(targetStatus);
        if (check.IsFailure)
        {
            throw new InvalidOfferStateTransitionException(Status, targetStatus);
        }

        var oldStatus = Status;
        Status = targetStatus;

        Raise(new OfferStatusChangedDomainEvent(Id, oldStatus, targetStatus, actorId, reason));
    }
}
