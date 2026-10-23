using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>Routes an outbox delivery to exactly the authenticated installation's owned registry row.</summary>
public sealed class DeviceVoiceCallV2PushTransport(
    IAppDbContext db, IVoiceCallV2FcmProvider fcm, IVoiceCallV2ApnsProvider apns,
    IDateTimeProvider clock, ILocalizedMessageProvider? messages = null) : IVoiceCallV2PushTransport
{
    public async Task<VoiceCallPushResult> SendAsync(VoiceCallDeviceSession device,
        CallEventEnvelope<CallSnapshotDto> envelope, CancellationToken cancellationToken)
    {
        if (envelope.EventType is "connecting" or "connected" or "ringing")
            return new(VoiceCallPushOutcome.Suppressed, ReasonCode: VoiceCallPushReasons.RealtimeOnly);
        if (device.RevokedAtUtc is not null)
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.SessionRevoked);
        if (device.ExpiresAtUtc <= clock.UtcNow)
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.SessionExpired);
        if (!string.Equals(device.Platform, "android", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(device.Platform, "ios", StringComparison.OrdinalIgnoreCase))
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.UnsupportedPlatform);
        var incoming = envelope.EventType == "incoming";
        var deadline = incoming ? envelope.Payload.DeadlineAtUtc ?? envelope.ExpiresAtUtc : envelope.ExpiresAtUtc;
        if (incoming && deadline is null)
            return new(VoiceCallPushOutcome.Suppressed, ReasonCode: VoiceCallPushReasons.MissingDeadline);
        if (envelope.ExpiresAtUtc is { } envelopeExpiry && (deadline is null || envelopeExpiry < deadline))
            deadline = envelopeExpiry;
        if (deadline is { } expiry && expiry <= clock.UtcNow)
            return new(VoiceCallPushOutcome.Suppressed, ReasonCode: VoiceCallPushReasons.IncomingExpired);
        var ttl = deadline is { } until ? until - clock.UtcNow : TimeSpan.Zero;
        if (device.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase) && incoming)
        {
            if (string.IsNullOrWhiteSpace(device.ApnsVoipToken))
                return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.MissingVoipRoute);
            envelope = await WithIncomingDisplayAsync(device, envelope, cancellationToken);
            var result = await apns.SendAsync(device.ApnsVoipToken, envelope, cancellationToken);
            if (result.Outcome == VoiceCallPushOutcome.InvalidRoute)
            {
                var registry = await db.VoiceCallDeviceSessions.SingleOrDefaultAsync(
                    d => d.Id == device.Id && d.UserId == device.UserId && d.ApnsVoipToken == device.ApnsVoipToken,
                    cancellationToken);
                if (registry is not null) { registry.ApnsVoipToken = null; registry.ModifiedAtUtc = clock.UtcNow; }
                await db.SaveChangesAsync(cancellationToken);
                return result with { ReasonCode = VoiceCallPushReasons.ProviderUnregistered };
            }
            return result;
        }
        if (device.FcmDeviceTokenId is not { } tokenId)
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.MissingFcmReference);
        // The owner-scoped read deliberately includes inactive rows so diagnostics can
        // distinguish a deactivated route from a reference that never existed; an
        // inactive route is still never sent to.
        var customer = await db.CustomerDeviceTokens.SingleOrDefaultAsync(
            t => t.Id == tokenId && t.UserId == device.UserId, cancellationToken);
        var driver = customer is null ? await db.DriverDeviceTokens.SingleOrDefaultAsync(
            t => t.Id == tokenId && t.UserId == device.UserId, cancellationToken) : null;
        if (customer is null && driver is null)
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.MissingOwnedFcmRoute);
        if (customer is { IsActive: false } || driver is { IsActive: false })
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute);
        var token = customer?.Token ?? driver?.Token;
        var platform = customer?.Platform ?? driver?.Platform;
        var expected = device.Platform.Equals("android", StringComparison.OrdinalIgnoreCase) ? MobilePlatform.Android : MobilePlatform.Ios;
        if (platform != expected)
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.PlatformMismatch);
        if (string.IsNullOrWhiteSpace(token))
            return new(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.EmptyFcmToken);
        if (incoming)
            envelope = await WithIncomingDisplayAsync(device, envelope, cancellationToken);
        // Display reads consume part of the ring window; never extend the FCM TTL.
        if (deadline is { } refreshedDeadline)
        {
            ttl = refreshedDeadline - clock.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return new(VoiceCallPushOutcome.Suppressed, ReasonCode: VoiceCallPushReasons.IncomingExpired);
        }
        var sent = await fcm.SendAsync(token, envelope, ttl, cancellationToken);
        if (sent.Outcome == VoiceCallPushOutcome.InvalidRoute)
        {
            if (customer is not null) { customer.IsActive = false; customer.ModifiedAtUtc = clock.UtcNow; }
            if (driver is not null) { driver.IsActive = false; driver.ModifiedAtUtc = clock.UtcNow; }
            await db.SaveChangesAsync(cancellationToken);
            return sent with { ReasonCode = VoiceCallPushReasons.ProviderUnregistered };
        }
        return sent;
    }

    private async Task<CallEventEnvelope<CallSnapshotDto>> WithIncomingDisplayAsync(
        VoiceCallDeviceSession device, CallEventEnvelope<CallSnapshotDto> envelope, CancellationToken cancellationToken)
    {
        // These aliases identify the driver calling a customer. Reverse calls must
        // not label the receiving driver as their own incoming caller.
        if (envelope.Payload.LocalParticipantRole != "Customer")
            return envelope;
        var language = await db.CustomerProfiles.AsNoTracking()
            .Where(p => p.UserId == device.UserId).Select(p => p.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);
        // Match the persisted recipient locale and Arabic default of voice notifications.
        var culture = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? SupportedCultures.EnglishCulture : SupportedCultures.ArabicCulture;
        var localized = new RecipientMessages(messages ?? NullLocalizedMessageProvider.Instance, culture);
        var display = await new VoiceCallV2CallDisplayQueryHandler(db, localized)
            .Handle(new(envelope.CallId, device.UserId), cancellationToken);
        if (display.IsFailure)
            return envelope; // Missing context must not prevent the incoming call itself.
        return envelope with
        {
            Payload = envelope.Payload with
            {
                DriverName = display.Value.DriverName,
                DriverRole = display.Value.DriverRole,
                DriverImageUrl = display.Value.DriverImageUrl,
                OrderCode = display.Value.OrderCode
            }
        };
    }

    private sealed class RecipientMessages(ILocalizedMessageProvider source,
        System.Globalization.CultureInfo culture) : ILocalizedMessageProvider
    {
        public System.Globalization.CultureInfo CurrentCulture => culture;
        public string GetMessage(string code, string? fallback = null, params object[] arguments)
            => source.GetMessage(code, culture, fallback, arguments);
    }
}
