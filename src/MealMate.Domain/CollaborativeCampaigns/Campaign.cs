using MealMate.Domain.CollaborativeCampaigns.Exceptions;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
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

    public DateTime? ReviewStartsAtUtc { get; set; }

    public DateTime? ReviewEndsAtUtc { get; set; }

    public int CurrentSnapshotVersion { get; set; } = 1;

    public int? ApprovedSnapshotVersion { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token; guards approval races.</summary>
    public uint RowVersion { get; set; }

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

    public Result CanApproveAt(DateTime nowUtc)
    {
        if (Status != CampaignStatus.UnderReview)
        {
            return Result.Failure("Campaign is not under review.");
        }

        if (!ReviewStartsAtUtc.HasValue || !ReviewEndsAtUtc.HasValue)
        {
            return Result.Failure("Campaign does not have an active review window.");
        }

        var window = new ReviewWindow(ReviewStartsAtUtc.Value, ReviewEndsAtUtc.Value);
        if (!window.IsOpen(nowUtc))
        {
            return Result.Failure("Current time is outside the campaign review window.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Records an explicit admin approval bound to the current snapshot version.
    /// Expiry of the review window never implies approval; only this method persists one.
    /// </summary>
    public Result Approve(Guid approverUserId, DateTime nowUtc)
    {
        var canApprove = CanApproveAt(nowUtc);
        if (canApprove.IsFailure)
        {
            return canApprove;
        }

        ApprovedSnapshotVersion = CurrentSnapshotVersion;
        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = nowUtc;

        return Result.Success();
    }

    /// <summary>
    /// A scheduled campaign may activate only while its stored approval is still bound
    /// to the current snapshot version and to a known approver.
    /// </summary>
    public Result CanActivate()
    {
        if (Status != CampaignStatus.Scheduled)
        {
            return Result.Failure("Campaign is not scheduled.");
        }

        if (!ApprovedSnapshotVersion.HasValue || !ApprovedByUserId.HasValue || !ApprovedAtUtc.HasValue)
        {
            return Result.Failure("Campaign has no recorded approval.");
        }

        if (ApprovedSnapshotVersion.Value != CurrentSnapshotVersion)
        {
            return Result.Failure(
                $"Approval was granted for snapshot v{ApprovedSnapshotVersion.Value} but current snapshot is v{CurrentSnapshotVersion}.");
        }

        return Result.Success();
    }

    /// <summary>Drops a stored approval; required after any material change to terms, capacity or pricing.</summary>
    public void InvalidateApproval()
    {
        ApprovedSnapshotVersion = null;
        ApprovedByUserId = null;
        ApprovedAtUtc = null;
    }

    /// <summary>Bumps the snapshot version and invalidates any stored approval.</summary>
    public void BumpSnapshotVersion()
    {
        CurrentSnapshotVersion++;
        InvalidateApproval();
    }

    public Result CanOpenForJoining()
    {
        if (Status != CampaignStatus.Draft)
        {
            return Result.Failure("Campaign is not in draft status.");
        }

        return ValidateScheduleOrdering(
            StartsAtUtc,
            EndsAtUtc,
            EnrollmentOpensAtUtc,
            EnrollmentClosesAtUtc,
            ReviewStartsAtUtc,
            ReviewEndsAtUtc,
            requireComplete: true);
    }

    public static Result ValidateScheduleOrdering(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime? enrollmentOpensAtUtc,
        DateTime? enrollmentClosesAtUtc,
        DateTime? reviewStartsAtUtc,
        DateTime? reviewEndsAtUtc,
        bool requireComplete = false)
    {
        if (startsAtUtc >= endsAtUtc)
        {
            return Result.Failure("StartsAtUtc must be before EndsAtUtc.");
        }

        var hasReviewStart = reviewStartsAtUtc.HasValue;
        var hasReviewEnd = reviewEndsAtUtc.HasValue;

        if (requireComplete)
        {
            if (!hasReviewStart && !hasReviewEnd)
            {
                return Result.Failure("Campaign does not have an active review window.");
            }

            if (!hasReviewStart || !hasReviewEnd)
            {
                return Result.Failure("Both review start and end dates must be provided.");
            }

            if (!enrollmentOpensAtUtc.HasValue || !enrollmentClosesAtUtc.HasValue)
            {
                return Result.Failure("Enrollment dates must be provided before opening for joining.");
            }

            if (enrollmentOpensAtUtc.Value >= enrollmentClosesAtUtc.Value)
            {
                return Result.Failure("EnrollmentOpensAtUtc must be before EnrollmentClosesAtUtc.");
            }

            if (enrollmentClosesAtUtc.Value > reviewStartsAtUtc!.Value)
            {
                return Result.Failure("EnrollmentClosesAtUtc must be before or equal to ReviewStartsAtUtc.");
            }

            if (reviewStartsAtUtc.Value >= reviewEndsAtUtc!.Value)
            {
                return Result.Failure("ReviewStartsAtUtc must be strictly before ReviewEndsAtUtc.");
            }

            if (reviewEndsAtUtc.Value > startsAtUtc)
            {
                return Result.Failure("ReviewEndsAtUtc must be before or equal to StartsAtUtc.");
            }

            return Result.Success();
        }

        if (hasReviewStart != hasReviewEnd)
        {
            return Result.Failure("Both review start and end dates must be provided.");
        }

        if (hasReviewStart && hasReviewEnd)
        {
            if (reviewStartsAtUtc!.Value >= reviewEndsAtUtc!.Value)
            {
                return Result.Failure("ReviewStartsAtUtc must be strictly before ReviewEndsAtUtc.");
            }

            if (reviewEndsAtUtc.Value > startsAtUtc)
            {
                return Result.Failure("ReviewEndsAtUtc must be before or equal to StartsAtUtc.");
            }

            if (enrollmentClosesAtUtc.HasValue && enrollmentClosesAtUtc.Value > reviewStartsAtUtc.Value)
            {
                return Result.Failure("EnrollmentClosesAtUtc must be before or equal to ReviewStartsAtUtc.");
            }
        }

        if (enrollmentOpensAtUtc.HasValue && enrollmentClosesAtUtc.HasValue)
        {
            if (enrollmentOpensAtUtc.Value >= enrollmentClosesAtUtc.Value)
            {
                return Result.Failure("EnrollmentOpensAtUtc must be before EnrollmentClosesAtUtc.");
            }

            if (!hasReviewStart && enrollmentClosesAtUtc.Value > startsAtUtc)
            {
                return Result.Failure("EnrollmentClosesAtUtc must be before or equal to StartsAtUtc.");
            }
        }

        return Result.Success();
    }
}
