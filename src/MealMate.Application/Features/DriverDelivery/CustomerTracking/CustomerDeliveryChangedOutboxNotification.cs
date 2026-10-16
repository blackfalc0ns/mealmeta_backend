using MealMate.Application.Common.Outbox;
using MediatR;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>Isolates customer delivery outbox handling from unrelated lifecycle handlers in Worker hosts.</summary>
public sealed record CustomerDeliveryChangedOutboxNotification(OutboxLifecycleNotification Message) : INotification;
