using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MediatR;

namespace MealMate.Persistence.Common;

/// <summary>
/// Publishes each <see cref="IDomainEvent"/> wrapped in a strongly-typed
/// <see cref="DomainEventNotification{TDomainEvent}"/> through MediatR.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;

    public DomainEventDispatcher(IPublisher publisher) => _publisher = publisher;

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
            var notification = (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
            await _publisher.Publish(notification, cancellationToken);
        }
    }
}
