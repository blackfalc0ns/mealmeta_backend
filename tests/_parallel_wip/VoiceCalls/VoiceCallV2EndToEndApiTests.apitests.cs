using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Api.Realtime;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MealMate.Application.Features.VoiceCalls.Commands;
using MediatR;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// Production JWT, PostgreSQL, REST mutations, outbox dispatcher and SignalR
/// roundtrip. Push is recorded by the isolated host, not sent to a real device.
/// SDP/ICE relay success does not assert microphone audio or TURN connectivity.
/// </summary>
public sealed class VoiceCallV2EndToEndApiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JwtBoundDevicesReceiveCommittedLifecycleAndRtc_CallerHangupAndReconnectReplayAgree(bool callerEnds)
    {
        await using var host = await VoiceCallTestHost.CreateAsync(extraSettings:
            new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });
        var driverDevice = await host.RegisterDeviceAsync(host.DriverDeviceA, "e2e-driver");
        var customerDevice = await host.RegisterDeviceAsync(host.CustomerDeviceA, "e2e-customer-a");
        var secondDevice = await host.RegisterDeviceAsync(host.CustomerDeviceB, "e2e-customer-b");
        using var caller = await WireClient.Connect(host, host.DriverDeviceA, driverDevice);
        using var receiver = await WireClient.Connect(host, host.CustomerDeviceA, customerDevice);
        using var otherReceiver = await WireClient.Connect(host, host.CustomerDeviceB, secondDevice);

        var initiated = await host.DriverDeviceA.PostAsJsonAsync("/api/v2/voice-calls/initiate",
            new { tripStopId = host.TripStopId, clientRequestId = Guid.NewGuid() });
        initiated.EnsureSuccessStatusCode();
        var initial = await initiated.Content.ReadFromJsonAsync<JsonElement>();
        initial.GetProperty("localParticipantRole").GetString().Should().Be("Driver");
        var callId = initial.GetProperty("callId").GetGuid();
        var receiverEvents = new List<JsonElement>();

        await host.RunOutboxAsync();
        var incoming = await Lifecycle(receiver, callId, "incoming", "Ringing", "Customer");
        receiverEvents.Add(incoming);
        var otherIncoming = await Lifecycle(otherReceiver, callId, "incoming", "Ringing", "Customer");
        SameIdentity(otherIncoming, incoming);
        PushIdentity(host, customerDevice.DeviceSessionId, incoming);
        PushIdentity(host, secondDevice.DeviceSessionId, incoming);

        (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", null)).EnsureSuccessStatusCode();
        var losingAnswer = await host.CustomerDeviceB.PostAsync($"/api/v2/voice-calls/{callId}/answer", null);
        losingAnswer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await losingAnswer.Content.ReadAsStringAsync()).Should().Contain("calls.answered_elsewhere");
        await host.RunOutboxAsync();
        var answered = await Lifecycle(receiver, callId, "answered", "Accepted", "Customer");
        receiverEvents.Add(answered);
        SameIdentity(await Lifecycle(caller, callId, "answered", "Accepted", "Driver"), answered);
        var elsewhere = await Lifecycle(otherReceiver, callId, "answered", "Accepted", "Customer");
        SameIdentity(elsewhere, answered);
        elsewhere.GetProperty("payload").GetProperty("winningDeviceSessionId").GetGuid().Should().Be(customerDevice.DeviceSessionId);

        (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connecting", null)).EnsureSuccessStatusCode();
        await host.RunOutboxAsync();
        var connecting = await Lifecycle(receiver, callId, "connecting", "Connecting", "Customer");
        receiverEvents.Add(connecting);
        SameIdentity(await Lifecycle(caller, callId, "connecting", "Connecting", "Driver"), connecting);
        SameIdentity(await Lifecycle(otherReceiver, callId, "connecting", "Connecting", "Customer"), connecting);

        var offerId = Guid.NewGuid();
        // Worst-case JSON escaping exercises the production Program receive limit,
        // not only the hub handler's UTF-8 check. Read concurrently for backpressure.
        var boundarySdp = new string('\u0001', VoiceCallV2Hub.MaxSdpPayloadBytes);
        var offerReceived = receiver.ReadInvocation("rtc:offer");
        await caller.Invoke("SendOffer", callId, new { type = "offer", sdp = boundarySdp }, offerId, 2);
        var offer = await offerReceived;
        offer.GetProperty("messageId").GetGuid().Should().Be(offerId);
        offer.GetProperty("negotiationGeneration").GetInt32().Should().Be(2);
        offer.GetProperty("senderDeviceSessionId").GetGuid().Should().Be(driverDevice.DeviceSessionId);
        offer.GetProperty("payload").GetProperty("sdp").GetString().Should().Be(boundarySdp);
        var answerId = Guid.NewGuid();
        await receiver.Invoke("SendAnswer", callId, new { type = "answer", sdp = "v=0\r\ns=-\r\nt=0 0\r\n" }, answerId, 2);
        (await caller.ReadInvocation("rtc:answer")).GetProperty("messageId").GetGuid().Should().Be(answerId);
        var iceId = Guid.NewGuid();
        await caller.Invoke("SendIceCandidate", callId,
            new { candidate = "candidate:1 1 UDP 1 192.0.2.1 5000 typ host", sdpMid = "audio", sdpMLineIndex = 0 }, iceId, 2);
        (await receiver.ReadInvocation("rtc:ice-candidate")).GetProperty("messageId").GetGuid().Should().Be(iceId);

        var firstReport = await host.DriverDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", null);
        firstReport.EnsureSuccessStatusCode();
        (await firstReport.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Connecting");
        (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", null)).EnsureSuccessStatusCode();
        await host.RunOutboxAsync();
        var connected = await Lifecycle(receiver, callId, "connected", "Active", "Customer");
        receiverEvents.Add(connected);
        SameIdentity(await Lifecycle(caller, callId, "connected", "Active", "Driver"), connected);
        SameIdentity(await Lifecycle(otherReceiver, callId, "connected", "Active", "Customer"), connected);
        var beforeDisconnect = connected.GetProperty("sequence").GetInt64();
        otherReceiver.Dispose();

        var endingDevice = callerEnds ? host.DriverDeviceA : host.CustomerDeviceA;
        (await endingDevice.PostAsync($"/api/v2/voice-calls/{callId}/end", null)).EnsureSuccessStatusCode();
        await host.RunOutboxAsync();
        var ended = await Lifecycle(receiver, callId, "ended", "Ended", "Customer");
        receiverEvents.Add(ended);
        SameIdentity(await Lifecycle(caller, callId, "ended", "Ended", "Driver"), ended);
        ended.GetProperty("payload").GetProperty("endReason").GetString().Should().Be(callerEnds ? "CallerEnded" : "ReceiverEnded");

        // Reconnect with the production JWT and original proof; reconcile from
        // durable REST replay because ephemeral RTC is intentionally not replayed.
        using var reconnected = await WireClient.Connect(host, host.CustomerDeviceB, secondDevice);
        var replayResponse = await host.CustomerDeviceB.GetAsync($"/api/v2/voice-calls/{callId}/events?afterSequence={beforeDisconnect}");
        replayResponse.EnsureSuccessStatusCode();
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        replay.GetArrayLength().Should().Be(1);
        SameIdentity(replay[0], ended);
        var snapshotResponse = await host.CustomerDeviceB.GetAsync($"/api/v2/voice-calls/{callId}");
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        snapshot.GetProperty("status").GetString().Should().Be("Ended");
        snapshot.GetProperty("sequence").GetInt64().Should().Be(ended.GetProperty("sequence").GetInt64());

        foreach (var envelope in receiverEvents) PushIdentity(host, customerDevice.DeviceSessionId, envelope);
        receiverEvents.Select(e => e.GetProperty("sequence").GetInt64()).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        var pushCount = host.PushNotifier.V2Events.Count;
        await host.RunOutboxAsync();
        host.PushNotifier.V2Events.Count.Should().Be(pushCount, "completed outbox deliveries must not be sent again");
        await using var db = await host.OpenDbAsync();
        (await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId)).Should().Be(0);
        (await db.CallSessions.SingleAsync(c => c.Id == callId)).Status.Should().Be(CallStatus.Ended);
    }

    [Theory]
    [InlineData("reject", "rejected", "Rejected")]
    [InlineData("cancel", "cancelled", "Cancelled")]
    [InlineData("expire", "missed", "Missed")]
    public async Task RingingTermination_ReachesBothPartiesAndReleasesBusy(string action, string eventType, string status)
    {
        await using var host = await VoiceCallTestHost.CreateAsync(extraSettings:
            new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });
        var driver = await host.RegisterDeviceAsync(host.DriverDeviceA, "terminal-driver");
        var customer = await host.RegisterDeviceAsync(host.CustomerDeviceA, "terminal-customer");
        using var caller = await WireClient.Connect(host, host.DriverDeviceA, driver);
        using var receiver = await WireClient.Connect(host, host.CustomerDeviceA, customer);
        var initiated = await host.DriverDeviceA.PostAsJsonAsync("/api/v2/voice-calls/initiate",
            new { tripStopId = host.TripStopId, clientRequestId = Guid.NewGuid() });
        initiated.EnsureSuccessStatusCode();
        var callId = (await initiated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
        await host.RunOutboxAsync();
        await Lifecycle(receiver, callId, "incoming", "Ringing", "Customer");
        (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/ringing-ack", null)).EnsureSuccessStatusCode();
        if (action == "expire")
        {
            DateTime deadline;
            long sequence;
            await using (var db = await host.OpenDbAsync())
            {
                var call = await db.CallSessions.SingleAsync(c => c.Id == callId);
                typeof(CallSession).GetProperty(nameof(CallSession.RingExpiresAtUtc))!
                    .SetValue(call, DateTime.UtcNow.AddSeconds(-1));
                await db.SaveChangesAsync();
                // Read PostgreSQL's stored precision for the conditional recovery predicate.
                deadline = await db.CallSessions.AsNoTracking().Where(c => c.Id == callId)
                    .Select(c => c.RingExpiresAtUtc!.Value).SingleAsync();
                sequence = call.Sequence;
            }
            using var scope = host.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ExpireVoiceCallCommand(callId, CallStatus.Ringing, sequence, deadline, CallEndReason.NoAnswer));
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be("Missed");
        }
        else
        {
            var actor = action == "reject" ? host.CustomerDeviceA : host.DriverDeviceA;
            (await actor.PostAsync($"/api/v2/voice-calls/{callId}/{action}", null)).EnsureSuccessStatusCode();
        }
        await host.RunOutboxAsync();
        var terminal = await Lifecycle(receiver, callId, eventType, status, "Customer");
        SameIdentity(await Lifecycle(caller, callId, eventType, status, "Driver"), terminal);
        PushIdentity(host, customer.DeviceSessionId, terminal);
        PushIdentity(host, driver.DeviceSessionId, terminal);
        var count = host.PushNotifier.V2Events.Count;
        await host.RunOutboxAsync();
        host.PushNotifier.V2Events.Count.Should().Be(count);
        await using var verify = await host.OpenDbAsync();
        (await verify.VoiceCallBusyParticipants.AnyAsync(b => b.CallId == callId)).Should().BeFalse();
        (await verify.CallSessions.SingleAsync(c => c.Id == callId)).Status.ToString().Should().Be(status);
    }

    private static async Task<JsonElement> Lifecycle(WireClient socket, Guid callId, string eventType, string status, string role)
    {
        var envelope = await socket.ReadInvocation("call:event");
        envelope.GetProperty("callId").GetGuid().Should().Be(callId);
        envelope.GetProperty("protocolVersion").GetInt32().Should().Be(2);
        envelope.GetProperty("eventType").GetString().Should().Be(eventType);
        envelope.GetProperty("payload").GetProperty("status").GetString().Should().Be(status);
        envelope.GetProperty("payload").GetProperty("localParticipantRole").GetString().Should().Be(role);
        return envelope;
    }

    private static void SameIdentity(JsonElement actual, JsonElement expected)
    {
        actual.GetProperty("eventId").GetGuid().Should().Be(expected.GetProperty("eventId").GetGuid());
        actual.GetProperty("sequence").GetInt64().Should().Be(expected.GetProperty("sequence").GetInt64());
        actual.GetProperty("eventType").GetString().Should().Be(expected.GetProperty("eventType").GetString());
    }

    private static void PushIdentity(VoiceCallTestHost host, Guid deviceId, JsonElement expected)
    {
        var eventId = expected.GetProperty("eventId").GetGuid();
        var push = host.PushNotifier.V2Events.Where(e => e.DeviceId == deviceId && e.Envelope.EventId == eventId).Should().ContainSingle().Which;
        push.Envelope.Sequence.Should().Be(expected.GetProperty("sequence").GetInt64());
        push.Envelope.EventType.Should().Be(expected.GetProperty("eventType").GetString());
    }

    private sealed class WireClient(WebSocket socket) : IDisposable
    {
        private readonly List<byte> _pending = new();

        public static async Task<WireClient> Connect(VoiceCallTestHost host, HttpClient http,
            (Guid DeviceSessionId, string ControlProof) device)
        {
            var client = host.Server.CreateWebSocketClient();
            client.ConfigureRequest = request => request.Headers["Authorization"] = http.DefaultRequestHeaders.Authorization!.ToString();
            var wire = new WireClient(await client.ConnectAsync(new Uri("wss://localhost/hubs/voice-call-v2"), default));
            await wire.Write(new { protocol = "json", version = 1 });
            await wire.Read();
            await wire.Invoke("BindDeviceSession", device.DeviceSessionId, device.ControlProof);
            return wire;
        }

        public async Task Invoke(string target, params object[] args)
        {
            var invocationId = Guid.NewGuid().ToString();
            await Write(new { type = 1, invocationId, target, arguments = args });
            while (true)
            {
                var message = await Read();
                if (!message.TryGetProperty("type", out var type) || type.GetInt32() != 3) continue;
                message.GetProperty("invocationId").GetString().Should().Be(invocationId);
                message.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
                return;
            }
        }

        public async Task<JsonElement> ReadInvocation(string target)
        {
            JsonElement message;
            do { message = await Read(); } while (message.GetProperty("type").GetInt32() == 6);
            message.GetProperty("type").GetInt32().Should().Be(1);
            message.GetProperty("target").GetString().Should().Be(target);
            return message.GetProperty("arguments")[0];
        }

        private Task Write(object value) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\u001e")), WebSocketMessageType.Text, true, default);

        private async Task<JsonElement> Read()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[8192];
            while (true)
            {
                var separator = _pending.IndexOf(0x1e);
                if (separator >= 0)
                {
                    using var document = JsonDocument.Parse(_pending.Take(separator).ToArray());
                    _pending.RemoveRange(0, separator + 1);
                    return document.RootElement.Clone();
                }
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("JWT SignalR connection closed before expected message.");
                _pending.AddRange(buffer.Take(result.Count));
            }
        }

        public void Dispose() => socket.Dispose();
    }
}
