using System.Text.Json;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class LedgerSourceOutboxHandler(IServiceScopeFactory scopeFactory) :
    INotificationHandler<OutboxLifecycleNotification>, INotificationHandler<OutboxMessageNotification>
{
    public Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken) =>
        notification.EventType == "LedgerSourceApproved"
            ? ProcessInScopeAsync(notification.MessageId, notification.PayloadJson, cancellationToken)
            : Task.CompletedTask;

    public Task Handle(OutboxMessageNotification notification, CancellationToken cancellationToken) =>
        notification.EventType == "LedgerSourceApproved"
            ? ProcessInScopeAsync(notification.MessageId, notification.PayloadJson, cancellationToken)
            : Task.CompletedTask;

    private async Task ProcessInScopeAsync(Guid eventId, string payloadJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("id", out var sourceIdElement)
            && !document.RootElement.TryGetProperty("Id", out sourceIdElement))
            throw new InvalidOperationException("ledger.outbox_source_id_missing");
        var sourceId = sourceIdElement.GetGuid();
        if (!document.RootElement.TryGetProperty("fingerprint", out var fingerprintElement)
            && !document.RootElement.TryGetProperty("Fingerprint", out fingerprintElement))
            throw new InvalidOperationException("ledger.outbox_fingerprint_missing");
        var fingerprint = fingerprintElement.GetString();
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new InvalidOperationException("ledger.outbox_fingerprint_missing");
        await using var scope = scopeFactory.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<ILedgerEventPostingProcessor>();
        var result = await posting.ProcessApprovedSourceDeliveryAsync(sourceId, eventId, fingerprint, ct);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Code);
    }
}
