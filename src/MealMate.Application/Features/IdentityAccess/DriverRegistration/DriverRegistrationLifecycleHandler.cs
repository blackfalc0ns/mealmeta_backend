using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed class DriverRegistrationLifecycleHandler : INotificationHandler<OutboxLifecycleNotification>
{
    public const string AdminApprovedConsumer = "DriverRegistrationAdminApprovedNotifier";
    public const string AdminRejectedConsumer = "DriverRegistrationAdminRejectedNotifier";
    public const string AdminChangesRequestedConsumer = "DriverRegistrationAdminChangesRequestedNotifier";
    public const string RestaurantApprovedConsumer = "DriverRegistrationRestaurantApprovedNotifier";
    public const string RestaurantRejectedConsumer = "DriverRegistrationRestaurantRejectedNotifier";
    public const string RestaurantChangesRequestedConsumer = "DriverRegistrationRestaurantChangesRequestedNotifier";
    public const string SubmittedConsumer = "DriverRegistrationSubmittedNotifier";
    public const string ResubmittedConsumer = "DriverRegistrationResubmittedNotifier";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly IDriverRegistrationNotificationService _driverNotifier;
    private readonly ILogger<DriverRegistrationLifecycleHandler>? _logger;

    public DriverRegistrationLifecycleHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        IDriverRegistrationNotificationService driverNotifier,
        ILogger<DriverRegistrationLifecycleHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _driverNotifier = driverNotifier;
        _logger = logger;
    }

    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.EventType)
        {
            case "DriverRegistrationAdminApproved":
                await HandleAdminApproved(notification, cancellationToken);
                break;
            case "DriverRegistrationAdminRejected":
                await HandleAdminRejected(notification, cancellationToken);
                break;
            case "DriverRegistrationAdminChangesRequested":
                await HandleAdminChangesRequested(notification, cancellationToken);
                break;
            case "DriverRegistrationRestaurantApproved":
                await HandleRestaurantApproved(notification, cancellationToken);
                break;
            case "DriverRegistrationRestaurantRejected":
                await HandleRestaurantRejected(notification, cancellationToken);
                break;
            case "DriverRegistrationRestaurantChangesRequested":
                await HandleRestaurantChangesRequested(notification, cancellationToken);
                break;
            case "DriverRegistrationSubmitted":
                await HandleSubmitted(notification, cancellationToken);
                break;
            case "DriverRegistrationResubmitted":
                await HandleResubmitted(notification, cancellationToken);
                break;
        }
    }

    private async Task HandleAdminApproved(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(AdminApprovedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";

        await _driverNotifier.NotifyDriverAdminApprovedAsync(regId, driverUserId, phone, driverName, restaurantName, cancellationToken);
        await RecordProcessed(AdminApprovedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleAdminRejected(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(AdminRejectedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var reason = root.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "" : "";

        await _driverNotifier.NotifyDriverAdminRejectedAsync(regId, driverUserId, phone, driverName, reason, cancellationToken);
        await RecordProcessed(AdminRejectedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleAdminChangesRequested(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(AdminChangesRequestedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var reason = root.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "" : "";

        await _driverNotifier.NotifyDriverAdminChangesRequestedAsync(regId, driverUserId, phone, driverName, reason, cancellationToken);
        await RecordProcessed(AdminChangesRequestedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleRestaurantApproved(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(RestaurantApprovedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";
        var restaurantId = root.TryGetProperty("RestaurantId", out var ridProp) && ridProp.ValueKind == JsonValueKind.String
            ? ridProp.GetGuid()
            : Guid.Empty;

        await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(regId, restaurantId, restaurantName, driverName, phone, cancellationToken);
        await _driverNotifier.NotifyDriverRestaurantApprovedAsync(regId, driverUserId, phone, driverName, restaurantName, cancellationToken);
        await RecordProcessed(RestaurantApprovedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleRestaurantRejected(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(RestaurantRejectedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";
        var reason = root.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "" : "";

        await _driverNotifier.NotifyDriverRestaurantRejectedAsync(regId, driverUserId, phone, driverName, restaurantName, reason, cancellationToken);
        await RecordProcessed(RestaurantRejectedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleRestaurantChangesRequested(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(RestaurantChangesRequestedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";
        var reason = root.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "" : "";

        await _driverNotifier.NotifyDriverRestaurantChangesRequestedAsync(regId, driverUserId, phone, driverName, restaurantName, reason, cancellationToken);
        await RecordProcessed(RestaurantChangesRequestedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleSubmitted(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(SubmittedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";
        var restaurantId = root.TryGetProperty("RestaurantId", out var ridProp) && ridProp.ValueKind == JsonValueKind.String
            ? ridProp.GetGuid()
            : (Guid?)null;
        var hasRestaurant = root.TryGetProperty("HasRestaurant", out var hrProp) && hrProp.GetBoolean();

        if (hasRestaurant && restaurantId.HasValue)
        {
            await _driverNotifier.NotifyRestaurantDriverApplicationSubmittedAsync(restaurantId.Value, regId, driverName, phone, cancellationToken);
            await _driverNotifier.NotifyDriverApplicationSubmittedAsync(regId, driverUserId, phone, driverName, restaurantName, restaurantId, reviewStage: 1, applicationStatus: "Submitted", eventKey: "driver.registration.submitted", cancellationToken: cancellationToken);
        }
        else
        {
            await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(regId, Guid.Empty, restaurantName, driverName, phone, cancellationToken);
            await _driverNotifier.NotifyDriverApplicationSubmittedAsync(regId, driverUserId, phone, driverName, restaurantName, restaurantId: null, reviewStage: 2, applicationStatus: "Submitted", eventKey: "driver.registration.independent_submitted", cancellationToken: cancellationToken);
        }

        await RecordProcessed(SubmittedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task HandleResubmitted(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(ResubmittedConsumer, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var regId = root.GetProperty("RegistrationId").GetGuid();
        var driverUserId = root.TryGetProperty("DriverUserId", out var dProp) && dProp.ValueKind == JsonValueKind.String
            ? dProp.GetGuid()
            : (Guid?)null;
        var phone = root.TryGetProperty("Phone", out var pProp) ? pProp.GetString() ?? "" : "";
        var driverName = root.TryGetProperty("DriverName", out var nProp) ? nProp.GetString() ?? "" : "";
        var restaurantName = root.TryGetProperty("RestaurantName", out var rnProp) ? rnProp.GetString() ?? "" : "";
        var restaurantId = root.TryGetProperty("RestaurantId", out var ridProp) && ridProp.ValueKind == JsonValueKind.String
            ? ridProp.GetGuid()
            : (Guid?)null;
        var preserve = root.TryGetProperty("PreserveRestaurantApproval", out var pResProp) && pResProp.GetBoolean();
        var preserveOrIndependent = preserve || !restaurantId.HasValue || restaurantId.Value == Guid.Empty;

        if (preserveOrIndependent)
        {
            await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(regId, restaurantId ?? Guid.Empty, restaurantName, driverName, phone, cancellationToken);
            await _driverNotifier.NotifyDriverApplicationSubmittedAsync(regId, driverUserId, phone, driverName, restaurantName, restaurantId, reviewStage: 2, applicationStatus: "Submitted", eventKey: "driver.registration.resubmitted", cancellationToken: cancellationToken);
        }
        else
        {
            await _driverNotifier.NotifyRestaurantDriverApplicationResubmittedAsync(restaurantId!.Value, regId, driverName, phone, cancellationToken);
            await _driverNotifier.NotifyDriverApplicationSubmittedAsync(regId, driverUserId, phone, driverName, restaurantName, restaurantId, reviewStage: 1, applicationStatus: "Submitted", eventKey: "driver.registration.resubmitted", cancellationToken: cancellationToken);
        }

        await RecordProcessed(ResubmittedConsumer, notification.MessageId, cancellationToken);
    }

    private async Task<bool> IsAlreadyProcessed(string consumerName, Guid messageId, CancellationToken cancellationToken)
    {
        var processed = await _context.InboxProcessedEvents
            .AnyAsync(e => e.ConsumerName == consumerName && e.EventId == messageId, cancellationToken);

        if (processed)
        {
            _logger?.LogDebug("Consumer {Consumer} already processed event {EventId}, skipping duplicate execution.", consumerName, messageId);
        }

        return processed;
    }

    private async Task RecordProcessed(string consumerName, Guid messageId, CancellationToken cancellationToken)
    {
        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = consumerName,
            EventId = messageId,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
