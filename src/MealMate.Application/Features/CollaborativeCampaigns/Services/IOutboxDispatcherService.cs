using MediatR;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record OutboxMessageNotification(
    Guid MessageId,
    string EventType,
    string PayloadJson,
    DateTime CreatedAtUtc) : INotification;

public sealed record OutboxDispatchResult(
    int ProcessedCount,
    int FailedCount,
    int DeadLetteredCount);

public interface IOutboxDispatcherService
{
    Task<OutboxDispatchResult> DispatchPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default);
}
