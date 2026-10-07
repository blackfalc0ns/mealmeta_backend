using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>Routes an outbox delivery to exactly the authenticated installation's owned registry row.</summary>
public sealed class DeviceVoiceCallV2PushTransport(
    IAppDbContext db, IVoiceCallV2FcmProvider fcm, IVoiceCallV2ApnsProvider apns,
    IDateTimeProvider clock) : IVoiceCallV2PushTransport
{
    public async Task<VoiceCallPushResult> SendAsync(VoiceCallDeviceSession device,
        CallEventEnvelope<CallSnapshotDto> envelope, CancellationToken cancellationToken)
    {
        if (envelope.EventType is "connecting" or "connected" or "ringing")
            return new(VoiceCallPushOutcome.Suppressed);
        if (device.RevokedAtUtc is not null || device.ExpiresAtUtc <= clock.UtcNow)
            return new(VoiceCallPushOutcome.NoRoute);
        if (!string.Equals(device.Platform, "android", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(device.Platform, "ios", StringComparison.OrdinalIgnoreCase))
            return new(VoiceCallPushOutcome.NoRoute);
        var incoming = envelope.EventType == "incoming";
        var deadline = incoming ? envelope.Payload.DeadlineAtUtc ?? envelope.ExpiresAtUtc : envelope.ExpiresAtUtc;
        if (incoming && deadline is null) return new(VoiceCallPushOutcome.Suppressed);
        if (envelope.ExpiresAtUtc is { } envelopeExpiry && (deadline is null || envelopeExpiry < deadline))
            deadline = envelopeExpiry;
        if (deadline is { } expiry && expiry <= clock.UtcNow)
            return new(VoiceCallPushOutcome.Suppressed);
        var ttl = deadline is { } until ? until - clock.UtcNow : TimeSpan.Zero;
        if (device.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase) && incoming)
        {
            if (string.IsNullOrWhiteSpace(device.ApnsVoipToken)) return new(VoiceCallPushOutcome.NoRoute);
            var result = await apns.SendAsync(device.ApnsVoipToken, envelope, cancellationToken);
            if (result.Outcome == VoiceCallPushOutcome.InvalidRoute)
            {
                var registry = await db.VoiceCallDeviceSessions.SingleOrDefaultAsync(
                    d => d.Id == device.Id && d.UserId == device.UserId && d.ApnsVoipToken == device.ApnsVoipToken,
                    cancellationToken);
                if (registry is not null) { registry.ApnsVoipToken = null; registry.ModifiedAtUtc = clock.UtcNow; }
                await db.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
        if (device.FcmDeviceTokenId is not { } tokenId) return new(VoiceCallPushOutcome.NoRoute);
        var customer = await db.CustomerDeviceTokens.SingleOrDefaultAsync(
            t => t.Id == tokenId && t.UserId == device.UserId && t.IsActive, cancellationToken);
        var driver = customer is null ? await db.DriverDeviceTokens.SingleOrDefaultAsync(
            t => t.Id == tokenId && t.UserId == device.UserId && t.IsActive, cancellationToken) : null;
        var token = customer?.Token ?? driver?.Token;
        var platform = customer?.Platform ?? driver?.Platform;
        var expected = device.Platform.Equals("android", StringComparison.OrdinalIgnoreCase) ? MobilePlatform.Android : MobilePlatform.Ios;
        if (platform != expected || string.IsNullOrWhiteSpace(token)) return new(VoiceCallPushOutcome.NoRoute);
        var sent = await fcm.SendAsync(token, envelope, ttl, cancellationToken);
        if (sent.Outcome == VoiceCallPushOutcome.InvalidRoute)
        {
            if (customer is not null) { customer.IsActive = false; customer.ModifiedAtUtc = clock.UtcNow; }
            if (driver is not null) { driver.IsActive = false; driver.ModifiedAtUtc = clock.UtcNow; }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }
}


