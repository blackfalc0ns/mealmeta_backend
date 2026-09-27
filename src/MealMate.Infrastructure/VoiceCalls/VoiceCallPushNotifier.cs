using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>
/// Infrastructure service for sending VoIP push notifications via FCM to wake backgrounded/killed mobile devices.
/// Handles multi-device token resolution, automatic invalid token deactivation, and safe error suppression.
/// </summary>
public sealed class VoiceCallPushNotifier : IVoiceCallPushNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly ILogger<VoiceCallPushNotifier> _logger;

    public VoiceCallPushNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        ILogger<VoiceCallPushNotifier> logger)
    {
        _db = db;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task SendIncomingCallPushAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = "incoming_call",
            ["callId"] = callId.ToString(),
            ["callerType"] = callerType,
            ["tripStopId"] = tripStopId?.ToString() ?? string.Empty,
            ["sentAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["eventId"] = Guid.NewGuid().ToString("N")
        };

        const string title = "Incoming Voice Call";
        const string body = "Incoming call regarding your delivery";

        await SendPushToUserDevicesAsync(
            receiverUserId,
            callId,
            "IncomingCall",
            title,
            body,
            data,
            cancellationToken);
    }

    public async Task SendCallCancelledPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = "call_cancelled",
            ["callId"] = callId.ToString(),
            ["sentAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["eventId"] = Guid.NewGuid().ToString("N")
        };

        const string title = "Call Cancelled";
        const string body = "The incoming call was cancelled";

        await SendPushToUserDevicesAsync(
            receiverUserId,
            callId,
            "CallCancelled",
            title,
            body,
            data,
            cancellationToken);
    }

    public async Task SendCallMissedPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = "call_missed",
            ["callId"] = callId.ToString(),
            ["sentAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["eventId"] = Guid.NewGuid().ToString("N")
        };

        const string title = "Missed Call";
        const string body = "You missed a voice call";

        await SendPushToUserDevicesAsync(
            receiverUserId,
            callId,
            "CallMissed",
            title,
            body,
            data,
            cancellationToken);
    }

    private async Task SendPushToUserDevicesAsync(
        Guid userId,
        Guid callId,
        string actionName,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve active tokens across customer and driver device registries
            var customerTokens = await _db.CustomerDeviceTokens
                .Where(t => t.UserId == userId && t.IsActive)
                .ToListAsync(cancellationToken);

            var driverTokens = await _db.DriverDeviceTokens
                .Where(t => t.UserId == userId && t.IsActive)
                .ToListAsync(cancellationToken);

            var totalTokens = customerTokens.Count + driverTokens.Count;
            if (totalTokens == 0)
            {
                _logger.LogInformation(
                    "No active push tokens found for user {UserId} during {ActionName} for call {CallId}",
                    userId, actionName, callId);
                return;
            }

            _logger.LogInformation(
                "IncomingPushAttempted: Action={ActionName}, CallId={CallId}, ReceiverUserId={ReceiverUserId}, TokenCount={TokenCount}",
                actionName, callId, userId, totalTokens);

            var hasInvalidTokens = false;
            var anySucceeded = false;
            var now = DateTime.UtcNow;

            foreach (var device in customerTokens)
            {
                var result = await _fcm.SendAsync(device.Token, title, body, data, cancellationToken);
                if (result.IsSuccess)
                {
                    anySucceeded = true;
                    device.LastSeenAtUtc = now;
                }
                else if (result.IsInvalidToken)
                {
                    hasInvalidTokens = true;
                    device.IsActive = false;
                    device.ModifiedAtUtc = now;
                    _logger.LogInformation(
                        "InvalidTokenDetected: Deactivated customer token for UserId={UserId}, Reason={Reason}",
                        userId, result.ErrorMessage);
                }
                else
                {
                    _logger.LogWarning(
                        "Push delivery failed for customer UserId={UserId}, CallId={CallId}: {Error}",
                        userId, callId, result.ErrorMessage);
                }
            }

            foreach (var device in driverTokens)
            {
                var result = await _fcm.SendAsync(device.Token, title, body, data, cancellationToken);
                if (result.IsSuccess)
                {
                    anySucceeded = true;
                    device.LastSeenAtUtc = now;
                }
                else if (result.IsInvalidToken)
                {
                    hasInvalidTokens = true;
                    device.IsActive = false;
                    device.ModifiedAtUtc = now;
                    _logger.LogInformation(
                        "InvalidTokenDetected: Deactivated driver token for UserId={UserId}, Reason={Reason}",
                        userId, result.ErrorMessage);
                }
                else
                {
                    _logger.LogWarning(
                        "Push delivery failed for driver UserId={UserId}, CallId={CallId}: {Error}",
                        userId, callId, result.ErrorMessage);
                }
            }

            if (hasInvalidTokens)
            {
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "Failed to persist deactivated invalid tokens for user {UserId}", userId);
                }
            }

            if (anySucceeded)
            {
                _logger.LogInformation(
                    "IncomingPushSucceeded: Action={ActionName}, CallId={CallId}, ReceiverUserId={ReceiverUserId}",
                    actionName, callId, userId);
            }
            else
            {
                _logger.LogWarning(
                    "IncomingPushFailed: Action={ActionName}, CallId={CallId}, ReceiverUserId={ReceiverUserId}",
                    actionName, callId, userId);
            }
        }
        catch (Exception ex)
        {
            // Push transport failure must NEVER fail call workflow or bubble unhandled errors
            _logger.LogError(ex,
                "Unexpected failure sending VoIP push notification for user {UserId}, call {CallId}",
                userId, callId);
        }
    }
}
