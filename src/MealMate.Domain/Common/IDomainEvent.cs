namespace MealMate.Domain.Common;

/// <summary>
/// Marker for something that happened in the domain and may trigger side effects
/// (notifications, accounting business events, audit). Dispatched after the
/// owning transaction is committed.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredOnUtc { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
