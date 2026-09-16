using MealMate.Domain.CollaborativeCampaigns.Exceptions;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class Campaign : AuditableEntity
{
    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public Percentage TotalDiscountRate { get; set; } = default!;

    public Percentage MealMateShareRate { get; set; } = default!;

    public Percentage RestaurantShareRate { get; set; } = default!;

    public CampaignFundingMode FundingMode { get; set; } = CampaignFundingMode.Shared;

    public int? MaxSubscribers { get; set; }

    public int CurrentSubscribers { get; set; }

    public DateTime? EnrollmentOpensAtUtc { get; set; }

    public DateTime? EnrollmentClosesAtUtc { get; set; }

    public ICollection<CampaignDiscountSplit> DiscountSplits { get; set; } = new List<CampaignDiscountSplit>();

    public ICollection<CampaignProgram> Programs { get; set; } = new List<CampaignProgram>();

    public ICollection<CampaignBundle> Bundles { get; set; } = new List<CampaignBundle>();

    public ICollection<CampaignRestaurantEnrollment> RestaurantEnrollments { get; set; } = new List<CampaignRestaurantEnrollment>();

    public ICollection<CampaignRestaurantCapacity> RestaurantCapacities { get; set; } = new List<CampaignRestaurantCapacity>();

    public ICollection<CampaignSubscriberCap> SubscriberCaps { get; set; } = new List<CampaignSubscriberCap>();

    public ICollection<CampaignCapacityReview> CapacityReviews { get; set; } = new List<CampaignCapacityReview>();

    public ICollection<CampaignAccountingEntry> AccountingEntries { get; set; } = new List<CampaignAccountingEntry>();

    public Result CanTransitionTo(CampaignStatus targetStatus)
    {
        if (Status == targetStatus)
        {
            return Result.Failure($"Campaign is already in state '{targetStatus}'.");
        }

        var isAllowed = Status switch
        {
            CampaignStatus.Draft => targetStatus is CampaignStatus.OpenForJoining or CampaignStatus.Cancelled,
            CampaignStatus.OpenForJoining => targetStatus is CampaignStatus.JoiningClosed or CampaignStatus.Cancelled,
            CampaignStatus.JoiningClosed => targetStatus is CampaignStatus.UnderReview or CampaignStatus.Cancelled,
            CampaignStatus.UnderReview => targetStatus is CampaignStatus.NeedsChanges or CampaignStatus.Scheduled or CampaignStatus.Cancelled,
            CampaignStatus.NeedsChanges => targetStatus is CampaignStatus.UnderReview or CampaignStatus.Cancelled,
            CampaignStatus.Scheduled => targetStatus is CampaignStatus.Active or CampaignStatus.Cancelled,
            CampaignStatus.Active => targetStatus is CampaignStatus.Stopped or CampaignStatus.Ended,
            CampaignStatus.Stopped => targetStatus is CampaignStatus.Ended,
            CampaignStatus.Ended => false,
            CampaignStatus.Cancelled => false,
            _ => false
        };

        return isAllowed
            ? Result.Success()
            : Result.Failure($"Transition from '{Status}' to '{targetStatus}' is not allowed.");
    }

    public void TransitionTo(CampaignStatus targetStatus, string actorId, string reason)
    {
        var canTransition = CanTransitionTo(targetStatus);
        if (canTransition.IsFailure)
        {
            throw new InvalidCampaignStateTransitionException(Status, targetStatus);
        }

        var oldStatus = Status;
        Status = targetStatus;

        Raise(new Events.CampaignStatusChangedDomainEvent(Id, oldStatus, targetStatus, actorId, reason));
    }
}
