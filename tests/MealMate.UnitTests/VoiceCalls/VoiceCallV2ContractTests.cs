using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

/// <summary>
/// T05 golden serialization contract (AC19): the v2 wire shapes are frozen —
/// camelCase field names, `payload`/`sentAtUtc` (never `data`), protocolVersion 2,
/// and no phone/SDP/ICE/secret-bearing fields anywhere in lifecycle envelopes.
/// </summary>
public sealed class VoiceCallV2ContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static JsonDocument Serialize<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Web));

    private static readonly DateTime T0 = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CallSnapshotDto_UsesFrozenCamelCaseNames()
    {
        var snapshot = new CallSnapshotDto(
            CallId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TripStopId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ProtocolVersion: 2,
            Sequence: 7,
            Status: "Ringing",
            EndReason: null,
            StartedAtUtc: T0,
            AcceptedAtUtc: null,
            ConnectedAtUtc: null,
            EndedAtUtc: null,
            DurationSeconds: null,
            DeadlineAtUtc: T0.AddSeconds(30),
            WinningDeviceSessionId: null,
            LocalParticipantRole: "Driver");

        using var doc = Serialize(snapshot);
        var root = doc.RootElement;

        root.GetProperty("callId").GetGuid().Should().Be(snapshot.CallId);
        root.GetProperty("tripStopId").GetGuid().Should().Be(snapshot.TripStopId);
        root.GetProperty("protocolVersion").GetInt32().Should().Be(2);
        root.GetProperty("sequence").GetInt64().Should().Be(7);
        root.GetProperty("status").GetString().Should().Be("Ringing");
        root.GetProperty("startedAtUtc").GetDateTime().Should().Be(T0);
        root.GetProperty("deadlineAtUtc").GetDateTime().Should().Be(T0.AddSeconds(30));
        root.GetProperty("localParticipantRole").GetString().Should().Be("Driver");
        root.TryGetProperty("winningDeviceSessionId", out _).Should().BeTrue();
    }

    [Fact]
    public void CallEventEnvelope_CarriesPayloadAndSentAtUtc_NeverData()
    {
        var envelope = new CallEventEnvelope<CallSnapshotDto>(
            EventId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProtocolVersion: 2,
            CallId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Sequence: 8,
            EventType: "incoming",
            OccurredAtUtc: T0,
            SentAtUtc: T0.AddMilliseconds(120),
            ExpiresAtUtc: T0.AddSeconds(30),
            Payload: Snapshot());

        using var doc = Serialize(envelope);
        var root = doc.RootElement;

        root.GetProperty("eventId").GetGuid().Should().Be(envelope.EventId);
        root.GetProperty("protocolVersion").GetInt32().Should().Be(2);
        root.GetProperty("callId").GetGuid().Should().Be(envelope.CallId);
        root.GetProperty("sequence").GetInt64().Should().Be(8);
        root.GetProperty("eventType").GetString().Should().Be("incoming");
        root.GetProperty("occurredAtUtc").GetDateTime().Should().Be(T0);
        root.GetProperty("sentAtUtc").GetDateTime().Should().Be(T0.AddMilliseconds(120));
        root.GetProperty("expiresAtUtc").GetDateTime().Should().Be(T0.AddSeconds(30));

        // The F09 drift guard: RTC lifecycle payloads travel as `payload`, never `data`.
        root.TryGetProperty("payload", out _).Should().BeTrue();
        root.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Fact]
    public void RtcRelayEnvelope_UsesFrozenEphemeralShape()
    {
        var envelope = new RtcRelayEnvelope<string>(
            MessageId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            CallId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            NegotiationGeneration: 3,
            SenderDeviceSessionId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            SentAtUtc: T0,
            Payload: "sdp-or-ice-blob");

        using var doc = Serialize(envelope);
        var root = doc.RootElement;

        root.GetProperty("messageId").GetGuid().Should().Be(envelope.MessageId);
        root.GetProperty("callId").GetGuid().Should().Be(envelope.CallId);
        root.GetProperty("negotiationGeneration").GetInt32().Should().Be(3);
        root.GetProperty("senderDeviceSessionId").GetGuid().Should().Be(envelope.SenderDeviceSessionId);
        root.GetProperty("sentAtUtc").GetDateTime().Should().Be(T0);
        root.TryGetProperty("payload", out _).Should().BeTrue();
    }

    [Fact]
    public void LifecycleEnvelopes_NeverCarryPhoneOrMediaOrSecretFields()
    {
        var envelope = new CallEventEnvelope<CallSnapshotDto>(
            Guid.NewGuid(), 2, Guid.NewGuid(), 1, "incoming", T0, T0, T0.AddSeconds(30), Snapshot());

        using var doc = Serialize(envelope);
        var json = doc.RootElement.GetRawText();

        var banned = new[]
        {
            "phone", "Phone", "customerPhone", "sdp", "Sdp", "SDP",
            "iceCandidate", "IceCandidate", "candidate", "token", "Token",
            "proof", "Proof", "jwt", "secret", "Secret"
        };

        json.Should().NotContainAny(banned,
            "lifecycle envelopes must never expose phone numbers, media payloads or credentials");
    }

    private static CallSnapshotDto Snapshot() => new(
        Guid.NewGuid(), Guid.NewGuid(), 2, 1, "Ringing", null,
        T0, null, null, null, null, T0.AddSeconds(30), null, "Driver");
}
