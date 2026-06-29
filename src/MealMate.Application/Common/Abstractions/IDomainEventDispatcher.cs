using MealMate.Domain.Common;
using MediatR;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Publishes domain events raised by aggregates after they are persisted, so
/// side effects (notifications, accounting business events, audit) stay decoupled.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// MediatR wrapper so a single <see cref="IDomainEvent"/> can be published and
/// handled by <c>INotificationHandler&lt;DomainEventNotification&lt;TEvent&gt;&gt;</c>.
/// </summary>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;
