namespace MealMate.Domain.Common;

/// <summary>
/// Operational entities are soft-deleted. Financial and historical records must
/// never be soft-deleted; do not apply this to ledgers, journal entries, or
/// settlement rows.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }

    Guid? DeletedByUserId { get; set; }
}
