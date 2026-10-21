namespace MealMate.Domain.Enums;

public enum CapacityCommitmentSource
{
    PaidSubscription = 1,
    IndependentDayCompensation = 2,
    IndependentDayFreeze = 3,
    ProtectedRenewal = 4
}

public enum CapacityCommitmentStatus
{
    Reserved = 1,
    Confirmed = 2,
    Cancelled = 3,
    Fulfilled = 4,
    Rescheduled = 5
}

public enum CapacityReductionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4
}
