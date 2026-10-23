using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Constants;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.VoiceCalls;
using MealMate.Infrastructure.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MealMate.Infrastructure.Push;
using NSubstitute;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallV2PushTransportTests
{
    [Theory]
    [InlineData("android", "en", true, "Yahya", "Delivery Driver", "Customer")]
    [InlineData("android", "ar", true, "يحيى", "مندوب التوصيل", "Customer")]
    [InlineData("ios", "ar", true, "يحيى", "مندوب التوصيل", "Customer")]
    [InlineData("ios", "en", true, "Yahya", "Delivery Driver", "Customer")]
    [InlineData("android", "ar", false, "", "مندوب التوصيل", "Customer")]
    [InlineData("ios", "en", false, "", "Delivery Driver", "Customer")]
    [InlineData("android", "en", true, null, null, "Driver")]
    [InlineData("ios", "ar", true, null, null, "Driver")]
    public async Task Incoming_push_contains_persisted_display_aliases_in_envelope_and_payload(
        string platform, string language, bool withDriver, string? expectedName, string? expectedRole, string recipientRole)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var driverUser = Guid.NewGuid();
        var customerUser = Guid.NewGuid();
        var trip = new MealMate.Domain.DriverDelivery.Trip { Code = "TRP-9546" };
        var stop = new MealMate.Domain.DriverDelivery.TripStop
        {
            TripId = trip.Id, OrderCode = "#BX-9546", CustomerMaskedId = "CUST-****",
            ZoneNameAr = "المنطقة", ZoneNameEn = "Zone", MealSummaryAr = "وجبة", MealSummaryEn = "Meal",
            SlotLabel = "12:00 - 15:00"
        };
        var call = new CallSession(stop.Id, driverUser, CallParticipantType.Driver,
            customerUser, CallParticipantType.Customer, Guid.NewGuid(), Now);
        db.Trips.Add(trip);
        db.TripStops.Add(stop);
        db.CallSessions.Add(call);
        db.CustomerProfiles.Add(new MealMate.Domain.IdentityAccess.CustomerProfile
            { UserId = customerUser, PreferredLanguage = language });
        if (withDriver)
        {
            var driver = new MealMate.Domain.IdentityAccess.DriverProfile
            {
                UserId = driverUser, FullNameAr = "يحيى", FullNameEn = "Yahya",
                ProfileImageStorageKey = "drivers/يحيى photo.jpg"
            };
            db.DriverProfiles.Add(driver);
            db.DriverAssignments.Add(new MealMate.Domain.DriverDelivery.DriverAssignment
                { TripId = trip.Id, DriverProfileId = driver.Id, Status = "Active", AssignedAtUtc = Now });
        }
        var token = new MealMate.Domain.IdentityAccess.CustomerDeviceToken
            { UserId = customerUser, Token = "owned-display-route", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(token);
        await db.SaveChangesAsync();
        var messages = NSubstitute.Substitute.For<MealMate.Application.Common.Localization.ILocalizedMessageProvider>();
        messages.GetMessage(MealMate.Application.Common.Localization.MessageCodes.VoiceCalls.CallScreenDriverRole,
            Arg.Any<System.Globalization.CultureInfo>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => ci.Arg<System.Globalization.CultureInfo>().TwoLetterISOLanguageName == "ar"
                ? "مندوب التوصيل" : "Delivery Driver");
        var envelope = Envelope() with { CallId = call.Id };
        envelope = envelope with { Payload = envelope.Payload with
            { CallId = call.Id, TripStopId = stop.Id, LocalParticipantRole = recipientRole } };
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        string? body = null;
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                var outgoing = ci.Arg<CallEventEnvelope<CallSnapshotDto>>();
                var message = FirebaseVoiceCallV2Provider.BuildMessage("owned-display-route", outgoing, TimeSpan.FromSeconds(25));
                Assert.Equal("incoming", message.Data["type"]);
                body = message.Data["envelope"];
                return new VoiceCallPushResult(VoiceCallPushOutcome.Accepted);
            });
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var capture = new CaptureHandler();
        var apns = new ApnsVoiceCallV2Provider(new HttpClient(capture), Options.Create(new ApnsVoiceCallOptions
            { TeamId = "team", KeyId = "key", BundleId = "com.mealmate.customer", DriverBundleId = "com.mealmate.driver",
                PrivateKeyPem = key.ExportPkcs8PrivateKeyPem() }), Clock());
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(messages).BuildServiceProvider();
        var transport = Microsoft.Extensions.DependencyInjection.ActivatorUtilities
            .CreateInstance<DeviceVoiceCallV2PushTransport>(services, db, fcm, apns, Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession
        {
            UserId = customerUser, Platform = platform, FcmDeviceTokenId = token.Id,
            ApnsVoipToken = new string('a', 64), ExpiresAtUtc = Now.AddHours(1)
        }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Accepted, result.Outcome);
        if (platform == "ios")
        {
            using var apnsBody = JsonDocument.Parse(capture.Body!);
            Assert.Equal("incoming", apnsBody.RootElement.GetProperty("type").GetString());
            body = apnsBody.RootElement.GetProperty("envelope").GetRawText();
        }
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal(call.Id, root.GetProperty("callId").GetGuid());
        Assert.Equal(17, root.GetProperty("sequence").GetInt64());
        Assert.Equal("incoming", root.GetProperty("eventType").GetString());
        foreach (var location in new[] { root, root.GetProperty("payload") })
        {
            Assert.Equal(expectedName, location.GetProperty("driverName").GetString());
            Assert.Equal(expectedName, location.GetProperty("callerName").GetString());
            Assert.Equal(expectedRole, location.GetProperty("driverRole").GetString());
            Assert.Equal(expectedRole, location.GetProperty("callerRole").GetString());
            var image = withDriver && recipientRole == "Customer"
                ? "/api/v1/uploads/content?key=" + Uri.EscapeDataString("drivers/يحيى photo.jpg") : null;
            Assert.Equal(image, location.GetProperty("avatarUrl").GetString());
            Assert.Equal(image, location.GetProperty("driverImageUrl").GetString());
            var orderCode = recipientRole == "Customer" ? "#BX-9546" : null;
            Assert.Equal(orderCode, location.GetProperty("orderCode").GetString());
            Assert.Equal(orderCode, location.GetProperty("orderNumber").GetString());
            Assert.False(location.TryGetProperty("deliveryAddress", out _));
            Assert.False(location.TryGetProperty("phoneNumber", out _));
        }
    }

    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static IDateTimeProvider Clock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return clock;
    }
    private static CallEventEnvelope<CallSnapshotDto> Envelope(string type = "incoming")
    {
        var callId = Guid.NewGuid();
        return new(Guid.NewGuid(), 2, callId, 17, type, Now, Now, Now.AddSeconds(25),
            new(callId, Guid.NewGuid(), 2, 17, type == "ended" ? "Ended" : "Ringing", null, Now, null, null,
                type == "ended" ? Now : null, null, Now.AddSeconds(25), null, "Customer"));
    }

    [Fact]
    public void Android_message_is_data_only_high_priority_with_authoritative_envelope_and_deadline()
    {
        var envelope = Envelope();
        var message = FirebaseVoiceCallV2Provider.BuildMessage("owned-token", envelope, TimeSpan.FromSeconds(25));
        Assert.Null(message.Notification);
        Assert.Null(message.Android.Notification);
        Assert.Equal(FirebaseAdmin.Messaging.Priority.High, message.Android.Priority);
        Assert.Equal(TimeSpan.FromSeconds(25), message.Android.TimeToLive);
        using var json = JsonDocument.Parse(message.Data["envelope"]);
        Assert.Equal(envelope.EventId, json.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(17, json.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(2, json.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(envelope.Payload.DeadlineAtUtc, json.RootElement.GetProperty("payload").GetProperty("deadlineAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Device_without_route_is_not_reported_as_accepted()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var apns = Substitute.For<IVoiceCallV2ApnsProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, apns, Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "android", ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Missing_token_has_a_specific_reason_and_no_provider_call()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(
            db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(
            new VoiceCallDeviceSession
            {
                Platform = "android", ExpiresAtUtc = Now.AddHours(1)
            }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal("missing_fcm_reference", result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Revoked_session_has_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "android", ExpiresAtUtc = Now.AddHours(1), RevokedAtUtc = Now }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.SessionRevoked, result.ReasonCode);
    }

    [Fact]
    public async Task Expired_session_has_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "android", ExpiresAtUtc = Now }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.SessionExpired, result.ReasonCode);
    }

    [Fact]
    public async Task Unsupported_platform_has_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "web", ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.UnsupportedPlatform, result.ReasonCode);
    }

    [Fact]
    public async Task Incoming_without_any_deadline_is_suppressed_with_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var envelope = Envelope();
        envelope = envelope with { ExpiresAtUtc = null, Payload = envelope.Payload with { DeadlineAtUtc = null } };
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "android", ExpiresAtUtc = Now.AddHours(1) }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Suppressed, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.MissingDeadline, result.ReasonCode);
    }

    [Fact]
    public async Task Ios_incoming_without_voip_token_has_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var apns = Substitute.For<IVoiceCallV2ApnsProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), apns, Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "ios", ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.MissingVoipRoute, result.ReasonCode);
        Assert.Empty(apns.ReceivedCalls());
    }

    [Fact]
    public async Task Reference_without_any_owned_registry_row_has_a_specific_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = Guid.NewGuid(), Platform = "android", FcmDeviceTokenId = Guid.NewGuid(), ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.MissingOwnedFcmRoute, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Inactive_owned_route_is_distinguished_from_a_missing_one_and_never_sent()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var inactive = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "inactive-token", Platform = MealMate.Domain.Enums.MobilePlatform.Android, IsActive = false };
        db.CustomerDeviceTokens.Add(inactive);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = inactive.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.InactiveFcmRoute, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
        Assert.False(inactive.IsActive);
    }

    [Fact]
    public async Task Registry_platform_mismatch_has_a_specific_reason_and_is_not_sent()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var iosRow = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "ios-row", Platform = MealMate.Domain.Enums.MobilePlatform.Ios };
        db.CustomerDeviceTokens.Add(iosRow);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = iosRow.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.PlatformMismatch, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Registry_row_with_blank_token_has_a_specific_reason_and_is_not_sent()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var blank = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "  ", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(blank);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = blank.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.EmptyFcmToken, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Provider_rejected_route_reports_the_unregistered_reason()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.InvalidRoute));
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var user = Guid.NewGuid();
        var token = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "stale-route", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(token);
        await db.SaveChangesAsync();
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = token.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.InvalidRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.ProviderUnregistered, result.ReasonCode);
    }

    [Theory]
    [InlineData("connecting")]
    [InlineData("connected")]
    public async Task Realtime_only_events_are_suppressed(string type)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession(), Envelope(type), default);
        Assert.Equal(VoiceCallPushOutcome.Suppressed, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.RealtimeOnly, result.ReasonCode);
    }

    [Fact]
    public async Task Apns_request_uses_voip_http2_stable_id_topic_and_ring_expiration()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new CaptureHandler();
        var provider = new ApnsVoiceCallV2Provider(new HttpClient(handler), Options.Create(new ApnsVoiceCallOptions
        { TeamId = "team", KeyId = "key", BundleId = "com.mealmate.customer", PrivateKeyPem = key.ExportPkcs8PrivateKeyPem() }), Clock());
        var envelope = Envelope();
        var result = await provider.SendAsync(new string('a', 64), envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Accepted, result.Outcome);
        Assert.Equal("provider-id", result.ProviderMessageId);
        Assert.Equal(HttpVersion.Version20, handler.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, handler.Policy);
        Assert.Equal("voip", handler.Headers["apns-push-type"]);
        Assert.Equal("com.mealmate.customer.voip", handler.Headers["apns-topic"]);
        Assert.Equal(envelope.EventId.ToString(), handler.Headers["apns-id"]);
        Assert.Equal(new DateTimeOffset(envelope.ExpiresAtUtc!.Value).ToUnixTimeSeconds().ToString(), handler.Headers["apns-expiration"]);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(envelope.EventId, body.RootElement.GetProperty("envelope").GetProperty("eventId").GetGuid());
        var jwt = handler.Headers["Authorization"].Split(' ')[1].Split('.');
        static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        Assert.True(key.VerifyData(System.Text.Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), Decode(jwt[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task Only_owned_registry_reference_is_sent_and_invalid_route_deactivates_that_row()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var owned = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "owned", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        var other = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "other-installation", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.AddRange(owned, other);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        fcm.SendAsync(owned.Token, Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.InvalidRoute));
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var envelope = Envelope();
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = owned.Id, ExpiresAtUtc = Now.AddHours(1) }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.InvalidRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.ProviderUnregistered, result.ReasonCode);
        Assert.False(owned.IsActive);
        Assert.True(other.IsActive);
        await fcm.Received(1).SendAsync("owned", envelope, TimeSpan.FromSeconds(25), default);
        await fcm.DidNotReceive().SendAsync("other-installation", Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_owned_reference_is_no_route_and_never_sent()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var foreign = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = Guid.NewGuid(), Token = "foreign", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(foreign);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = Guid.NewGuid(), Platform = "android", FcmDeviceTokenId = foreign.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.MissingOwnedFcmRoute, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
    }

    [Fact]
    public async Task Ios_incoming_uses_voip_token_and_provider_failure_remains_retryable()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var apns = Substitute.For<IVoiceCallV2ApnsProvider>();
        var envelope = Envelope();
        apns.SendAsync("voip-token", envelope, Arg.Any<CancellationToken>()).Returns(Task.FromException<VoiceCallPushResult>(new HttpRequestException("provider unavailable")));
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, apns, Clock());
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(new VoiceCallDeviceSession { Platform = "ios", ApnsVoipToken = "voip-token", ExpiresAtUtc = Now.AddHours(1) }, envelope, default));
        Assert.Empty(fcm.ReceivedCalls());
        await apns.Received(1).SendAsync("voip-token", envelope, default);
    }

    [Fact]
    public async Task Existing_apns_route_without_credentials_throws_instead_of_claiming_success()
    {
        var provider = new ApnsVoiceCallV2Provider(new HttpClient(new CaptureHandler()), Options.Create(new ApnsVoiceCallOptions()), Clock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync(new string('a', 64), Envelope(), default));
    }

    [Fact]
    public async Task Expired_incoming_is_suppressed_before_any_provider_call()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var apns = Substitute.For<IVoiceCallV2ApnsProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, Substitute.For<IVoiceCallV2FcmProvider>(), apns, Clock());
        var envelope = Envelope();
        envelope = envelope with { Payload = envelope.Payload with { DeadlineAtUtc = Now } };
        var result = await transport.SendAsync(new VoiceCallDeviceSession { Platform = "ios", ApnsVoipToken = "route", ExpiresAtUtc = Now.AddHours(1) }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Suppressed, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.IncomingExpired, result.ReasonCode);
        Assert.Empty(apns.ReceivedCalls());
    }
    [Theory]
    [InlineData("IncomingCall", "Incoming Voice Call", "مكالمة صوتية واردة")]
    [InlineData("CallCancelled", "Call Cancelled", "أُلغيت المكالمة")]
    [InlineData("CallMissed", "Missed Call", "مكالمة فائتة")]
    public void Legacy_catalog_uses_recipient_locale_and_can_switch_back(string eventKey, string english, string arabic)
    {
        Assert.Equal(arabic, VoiceCallNotificationCatalog.ForEvent(eventKey, "ar").Title);
        Assert.Equal(english, VoiceCallNotificationCatalog.ForEvent(eventKey, "en").Title);
        Assert.Equal(arabic, VoiceCallNotificationCatalog.ForEvent(eventKey, "ar").Title);
        Assert.Equal(arabic, VoiceCallNotificationCatalog.ForEvent(eventKey, null).Title);
        Assert.False(string.IsNullOrWhiteSpace(VoiceCallNotificationCatalog.ForEvent(eventKey, "en").Body));
        Assert.False(string.IsNullOrWhiteSpace(VoiceCallNotificationCatalog.ForEvent(eventKey, "ar").Body));
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "Unregistered", true)]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken", true)]
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests", false)]
    [InlineData(HttpStatusCode.Forbidden, "InvalidProviderToken", false)]
    public async Task Apns_deletes_only_confirmed_invalid_routes_and_preserves_provider_failures(HttpStatusCode status, string reason, bool invalid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var provider = new ApnsVoiceCallV2Provider(new HttpClient(new CaptureHandler { Status = status, Reason = reason }),
            Options.Create(new ApnsVoiceCallOptions { TeamId = "team", KeyId = "key", BundleId = "com.mealmate.customer", PrivateKeyPem = key.ExportPkcs8PrivateKeyPem() }), Clock());
        if (invalid)
            Assert.Equal(VoiceCallPushOutcome.InvalidRoute, (await provider.SendAsync(new string('a', 64), Envelope(), default)).Outcome);
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.SendAsync(new string('a', 64), Envelope(), default));
    }

    [Fact]
    public async Task Existing_fcm_route_without_credentials_throws_instead_of_claiming_success()
    {
        using var provider = new FirebaseVoiceCallV2Provider(Options.Create(new FirebasePushOptions { ProjectId = "", CredentialPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json") }), Substitute.For<IHostEnvironment>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync("owned", Envelope(), TimeSpan.FromSeconds(25), default));
    }

    [Fact]
    public async Task Ios_terminal_targets_owned_driver_fcm_route_and_preserves_provider_identifier()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var token = new MealMate.Domain.IdentityAccess.DriverDeviceToken { UserId = user, Token = "driver-installation", Platform = MealMate.Domain.Enums.MobilePlatform.Ios };
        db.DriverDeviceTokens.Add(token);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var envelope = Envelope("ended");
        fcm.SendAsync(token.Token, envelope, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "projects/provider/messages/accepted"));
        var apns = Substitute.For<IVoiceCallV2ApnsProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, apns, Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "ios", FcmDeviceTokenId = token.Id, ApnsVoipToken = "voip", ExpiresAtUtc = Now.AddHours(1) }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Accepted, result.Outcome);
        Assert.Equal("projects/provider/messages/accepted", result.ProviderMessageId);
        Assert.Empty(apns.ReceivedCalls());
        await fcm.Received(1).SendAsync(token.Token, envelope, TimeSpan.FromSeconds(25), default);
    }

    [Fact]
    public async Task Late_invalid_route_for_a_superseded_route_spares_the_newer_active_route()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var superseded = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "superseded", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        var newer = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "newer", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.AddRange(superseded, newer);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        fcm.SendAsync(superseded.Token, Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.InvalidRoute));
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        // A dispatch that captured the binding before the re-registration still
        // targets only the row it read — the newer route is never touched.
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = superseded.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.InvalidRoute, result.Outcome);
        Assert.False(superseded.IsActive);
        Assert.True(newer.IsActive);
    }

    [Fact]
    public async Task Unregistered_route_is_reported_inactive_and_never_revived()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var loggedOut = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "logged-out", Platform = MealMate.Domain.Enums.MobilePlatform.Android, IsActive = false };
        db.CustomerDeviceTokens.Add(loggedOut);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = loggedOut.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default);
        Assert.Equal(VoiceCallPushOutcome.NoRoute, result.Outcome);
        Assert.Equal(VoiceCallPushReasons.InactiveFcmRoute, result.ReasonCode);
        Assert.Empty(fcm.ReceivedCalls());
        Assert.False(loggedOut.IsActive, "an inactive route is never reactivated by the transport");
    }

    [Fact]
    public async Task Ttl_never_outlives_the_ring_deadline()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var token = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "ttl-route", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(token);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "ttl-check"));
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        // Incoming arriving one second before the deadline carries exactly one
        // second of TTL — never more than the remaining ring window.
        var envelope = Envelope();
        envelope = envelope with { Payload = envelope.Payload with { DeadlineAtUtc = Now.AddSeconds(1) } };
        var result = await transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = token.Id, ExpiresAtUtc = Now.AddHours(1) }, envelope, default);
        Assert.Equal(VoiceCallPushOutcome.Accepted, result.Outcome);
        await fcm.Received(1).SendAsync("ttl-route", envelope, TimeSpan.FromSeconds(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provider_availability_failure_retries_without_disabling_the_route()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var user = Guid.NewGuid();
        var token = new MealMate.Domain.IdentityAccess.CustomerDeviceToken { UserId = user, Token = "unavailable-route", Platform = MealMate.Domain.Enums.MobilePlatform.Android };
        db.CustomerDeviceTokens.Add(token);
        await db.SaveChangesAsync();
        var fcm = Substitute.For<IVoiceCallV2FcmProvider>();
        fcm.SendAsync(Arg.Any<string>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<VoiceCallPushResult>(new HttpRequestException("provider temporarily unavailable")));
        var transport = new DeviceVoiceCallV2PushTransport(db, fcm, Substitute.For<IVoiceCallV2ApnsProvider>(), Clock());
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            transport.SendAsync(new VoiceCallDeviceSession { UserId = user, Platform = "android", FcmDeviceTokenId = token.Id, ExpiresAtUtc = Now.AddHours(1) }, Envelope(), default));
        Assert.True(token.IsActive, "an availability failure is a retry, never a route disable");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Reason;
        public Version? Version;
        public HttpVersionPolicy Policy;
        public Dictionary<string,string> Headers = new();
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Version = request.Version;
            Policy = request.VersionPolicy;
            Headers = request.Headers.ToDictionary(x => x.Key, x => x.Value.Single());
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(Status) { Content = new StringContent(JsonSerializer.Serialize(new { reason = Reason })) };
            response.Headers.Add("apns-id", "provider-id");
            return response;
        }
    }
}




