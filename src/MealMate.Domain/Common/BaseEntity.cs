namespace MealMate.Domain.Common;

/// <summary>
/// Base type for all aggregate roots / entities. Identity uses <see cref="Guid"/>
/// per the database conventions. Carries a domain-event buffer that the
/// persistence layer drains and dispatches after save.
/// </summary>
public abstract class BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public Guid Id { get; protected set; } = Guid.NewGuid();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
