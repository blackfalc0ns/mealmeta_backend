using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// A formal workflow request submitted by a restaurant to reduce its fixed daily capacity limit.
/// Requires admin review and safety scheduling to ensure no existing customer commitments are violated.
/// </summary>
public sealed class CapacityReductionRequest : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public int CurrentCapacity { get; set; }

    public int RequestedCapacity { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateOnly RequestedEffectiveDate { get; set; }

    public CapacityReductionStatus Status { get; set; } = CapacityReductionStatus.Pending;

    public Guid? ReviewedByAdminUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? ReviewNotes { get; set; }

    public DateOnly? ApprovedEffectiveDate { get; set; }

    public bool CanWithdraw => Status == CapacityReductionStatus.Pending;

    public Result Withdraw()
    {
        if (!CanWithdraw)
            return Result.Failure($"Cannot withdraw request with status {Status}.");

        Status = CapacityReductionStatus.Withdrawn;
        return Result.Success();
    }

    public Result Approve(Guid adminUserId, DateOnly approvedEffectiveDate, string? notes = null)
    {
        if (Status != CapacityReductionStatus.Pending)
            return Result.Failure($"Cannot approve request with status {Status}.");

        if (adminUserId == Guid.Empty)
            return Result.Failure("Valid admin user ID is required.");

        Status = CapacityReductionStatus.Approved;
        ReviewedByAdminUserId = adminUserId;
        ReviewedAtUtc = DateTime.UtcNow;
        ApprovedEffectiveDate = approvedEffectiveDate;
        ReviewNotes = notes;

        return Result.Success();
    }

    public Result Reject(Guid adminUserId, string? notes = null)
    {
        if (Status != CapacityReductionStatus.Pending)
            return Result.Failure($"Cannot reject request with status {Status}.");

        if (adminUserId == Guid.Empty)
            return Result.Failure("Valid admin user ID is required.");

        Status = CapacityReductionStatus.Rejected;
        ReviewedByAdminUserId = adminUserId;
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewNotes = notes;

        return Result.Success();
    }
}
