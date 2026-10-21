using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// Represents a guaranteed unit of service (one box for one individual) on a specific service date.
/// Strictly enforces the rule: one box per individual per service day.
/// Distinguishes between paid subscriptions, independent compensation days, independent freeze days, and protected renewals.
/// </summary>
public sealed class ServiceCapacityCommitment : AuditableEntity
{
    public DateOnly ServiceDate { get; set; }

    /// <summary>
    /// The owning customer user account ID.
    /// </summary>
    public Guid CustomerUserId { get; set; }

    /// <summary>
    /// The specific family member beneficiary ID (null for individual subscriptions).
    /// </summary>
    public Guid? FamilyMemberId { get; set; }

    /// <summary>
    /// Associated subscription ID (or originating subscription ID for independent days).
    /// </summary>
    public Guid SubscriptionId { get; set; }

    public CapacityCommitmentSource Source { get; set; } = CapacityCommitmentSource.PaidSubscription;

    public CapacityCommitmentStatus Status { get; set; } = CapacityCommitmentStatus.Reserved;

    /// <summary>
    /// The allocated restaurant ID, or null if platform-guaranteed and awaiting allocation.
    /// </summary>
    public Guid? RestaurantId { get; set; }

    public Guid RegionId { get; set; }

    public Guid? DeliveryAddressId { get; set; }

    public string? DietaryRestrictionsSnapshot { get; set; }

    public long LeadPolicyVersion { get; set; } = 1;

    public bool IsActiveCommitment =>
        Status is CapacityCommitmentStatus.Reserved or CapacityCommitmentStatus.Confirmed;

    public bool CanTransitionTo(CapacityCommitmentStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            CapacityCommitmentStatus.Reserved => target is CapacityCommitmentStatus.Confirmed
                                                        or CapacityCommitmentStatus.Cancelled
                                                        or CapacityCommitmentStatus.Rescheduled,
            CapacityCommitmentStatus.Confirmed => target is CapacityCommitmentStatus.Fulfilled
                                                         or CapacityCommitmentStatus.Cancelled
                                                         or CapacityCommitmentStatus.Rescheduled,
            CapacityCommitmentStatus.Fulfilled => false,
            CapacityCommitmentStatus.Cancelled => false,
            CapacityCommitmentStatus.Rescheduled => false,
            _ => false
        };
    }

    public Result TransitionTo(CapacityCommitmentStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_CAPACITY_COMMITMENT_TRANSITION: Cannot transition commitment from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
