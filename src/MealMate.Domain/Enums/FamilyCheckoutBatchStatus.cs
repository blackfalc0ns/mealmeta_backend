namespace MealMate.Domain.Enums;

/// <summary>
/// Lifecycle of one family checkout batch. A batch is an immutable financial
/// snapshot: once <see cref="Paid"/> it is never reopened; later additions go
/// into a new batch.
/// </summary>
public enum FamilyCheckoutBatchStatus
{
    Draft = 0,
    PaymentPending = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4,
    Cancelled = 5,
    ReviewRequired = 6
}
