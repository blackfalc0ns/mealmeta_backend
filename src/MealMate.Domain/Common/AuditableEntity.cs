namespace MealMate.Domain.Common;

/// <summary>
/// Convenience base for the common case: an entity that is auditable and
/// soft-deletable. Use <see cref="BaseEntity"/> directly for append-only
/// financial records that must not be soft-deleted.
/// </summary>
public abstract class AuditableEntity : BaseEntity, IAuditableEntity, ISoftDeletable
{
    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public Guid? ModifiedByUserId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public Guid? DeletedByUserId { get; set; }
}
