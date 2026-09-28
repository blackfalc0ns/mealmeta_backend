using MediatR;

namespace MealMate.Application.Common.Outbox;

public sealed record OutboxLifecycleNotification(
    Guid MessageId,
    string EventType,
    string? AggregateType,
    string? AggregateId,
    string? CorrelationId,
    string? DeduplicationKey,
    string PayloadJson,
    DateTime CreatedAtUtc) : INotification;

public interface IOutboxService
{
    Task<int> ProcessPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default);
}
