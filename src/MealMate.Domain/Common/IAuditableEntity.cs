namespace MealMate.Domain.Common;

/// <summary>
/// Entities implementing this get created/modified stamps filled automatically
/// by the persistence audit interceptor. Times are always stored in UTC.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAtUtc { get; set; }

    Guid? CreatedByUserId { get; set; }

    DateTime? ModifiedAtUtc { get; set; }

    Guid? ModifiedByUserId { get; set; }
}
