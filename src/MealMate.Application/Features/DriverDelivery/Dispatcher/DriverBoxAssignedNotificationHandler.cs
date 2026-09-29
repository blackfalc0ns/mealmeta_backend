using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed class DriverBoxAssignedNotificationHandler : INotificationHandler<OutboxLifecycleNotification>
{
    private readonly IDriverPushNotifier _driverPushNotifier;
    private readonly ILogger<DriverBoxAssignedNotificationHandler>? _logger;

    public DriverBoxAssignedNotificationHandler(
        IDriverPushNotifier driverPushNotifier,
        ILogger<DriverBoxAssignedNotificationHandler>? logger = null)
    {
        _driverPushNotifier = driverPushNotifier;
        _logger = logger;
    }

    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (!string.Equals(notification.EventType, "DriverBoxAssigned", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(notification.PayloadJson);
            var root = doc.RootElement;

            Guid driverUserId = Guid.Empty;
            if (root.TryGetProperty("DriverUserId", out var dProp) && dProp.TryGetGuid(out var dGuid))
            {
                driverUserId = dGuid;
            }
            else if (root.TryGetProperty("driverUserId", out var dProp2) && dProp2.TryGetGuid(out var dGuid2))
            {
                driverUserId = dGuid2;
            }

            Guid boxId = Guid.Empty;
            if (root.TryGetProperty("BoxId", out var bProp) && bProp.TryGetGuid(out var bGuid))
            {
                boxId = bGuid;
            }
            else if (root.TryGetProperty("boxId", out var bProp2) && bProp2.TryGetGuid(out var bGuid2))
            {
                boxId = bGuid2;
            }

            string boxCode = string.Empty;
            if (root.TryGetProperty("BoxCode", out var cProp))
            {
                boxCode = cProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("boxCode", out var cProp2))
            {
                boxCode = cProp2.GetString() ?? string.Empty;
            }

            string zoneName = string.Empty;
            if (root.TryGetProperty("ZoneName", out var zProp))
            {
                zoneName = zProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("zoneName", out var zProp2))
            {
                zoneName = zProp2.GetString() ?? string.Empty;
            }

            await _driverPushNotifier.NotifyBoxAssignedAsync(
                driverUserId,
                boxId,
                boxCode,
                zoneName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse or dispatch DriverBoxAssigned outbox notification for message {MessageId}.", notification.MessageId);
            throw;
        }
    }
}
