using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MediatR;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// Red-first regressions for the 2026-10-06 follow-up review: V03 (Busy release on
/// terminal), V04 (v1 must not control v2 calls), V05 (outbox delivery honesty),
/// V08 (dispatcher location forwarding).
/// </summary>
public sealed class VoiceCallFollowupFixTests
{
    private static IReadOnlyDictionary<string, string?> V2On { get; } =
        new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" };

    private static async Task<VoiceCallTestHost> CreateV2HostAsync()
    {
        var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceB, "customer-v2-b");
        return h;
    }

    private static async Task<Guid> InitiateV2Async(VoiceCallTestHost h)
    {
        var response = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
    }

    /// <summary>V03: a terminal v2 call must release both Busy rows in the same
    /// transaction as the terminal state, so the next call can start.</summary>
    [Fact]
    public async Task TerminalV2Call_ReleasesBusyRows_AndNextCallStarts()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        // Overlap guard sanity: while ringing, a second initiation is rejected.
        var overlap = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict, "two live calls per user are forbidden");

        // Recovery path terminates the ringing call (ring timeout → missed).
        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var missed = await sender.Send(new MarkCallMissedCommand(callId));
        missed.IsSuccess.Should().BeTrue();

        await using var db = await h.OpenDbAsync();
        var busyAfter = await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId);
        busyAfter.Should().Be(0, "terminal mutation, Busy release and lifecycle event commit together");

        // The next initiation must succeed — no permanent reservations.
        var next = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        next.StatusCode.Should().Be(HttpStatusCode.OK,
            "one attempted call must never prevent the next one");
    }

    /// <summary>V04: a JWT-only client of the same account must not control a v2
    /// device-bound call through the legacy surface.</summary>
    [Fact]
    public async Task V1Routes_CannotControlV2Calls()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        // Legacy cancel by the caller's bare JWT (no device proof) must be refused.
        var cancel = await h.DriverClient.PostAsJsonAsync(
            $"/api/v1/voice-calls/{callId}/cancel",
            new { reason = "caller-cancelled" });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "legacy mutations may serve legacy sessions only");
        (await cancel.Content.ReadAsStringAsync()).Should().Contain("calls.v2_device_control_required");

        // Legacy answer by the receiver's bare JWT — same boundary.
        var answer = await h.CustomerClient.PostAsync($"/api/v1/voice-calls/{callId}/answer", content: null);
        answer.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Ringing, "the bypass attempt must not mutate the call");
        var busy = await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId);
        busy.Should().Be(2, "reservations survive the refused bypass");
    }

    /// <summary>V05a: processed voice lifecycle events must have real per-recipient
    /// delivery records and an actual dispatch — never a bare ProcessedAtUtc.</summary>
    [Fact]
    public async Task OutboxWorker_ProcessesVoiceEvent_WithDeliveryRecordsAndDispatch()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        h.PushNotifier.Incoming.Should().BeEmpty("dispatch happens via the outbox consumer, not inline");
        await h.RunOutboxAsync();

        await using var db = await h.OpenDbAsync();
        var deliveryRows = await db.VoiceCallEventDeliveries
            .Where(d => d.CallId == callId && d.Channel == VoiceCallEventDelivery.ChannelPush)
            .ToListAsync();
        deliveryRows.Should().NotBeEmpty("per-recipient/channel delivery records are durable evidence");
        h.PushNotifier.Incoming.Count(p => p.CallId == callId).Should().Be(deliveryRows.Count,
            "the consumer dispatches once per durable delivery target");
        deliveryRows.Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusAcceptedByProvider,
            "successful dispatch is recorded distinctly from device acknowledgement");

        var outboxRow = await db.OutboxMessages.FirstAsync(o => o.AggregateId == callId.ToString());
        outboxRow.ProcessedAtUtc.Should().NotBeNull();
    }

    /// <summary>V05b: a failing dispatch must leave the event unprocessed and retryable.</summary>
    [Fact]
    public async Task OutboxWorker_DispatchFailure_KeepsEventUnprocessed()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);
        h.PushNotifier.OnIncomingCallAsync = () => throw new HttpRequestException("FCM unavailable");

        await h.RunOutboxAsync();

        await using var db = await h.OpenDbAsync();
        var row = await db.OutboxMessages.FirstAsync(o => o.AggregateId == callId.ToString());
        row.ProcessedAtUtc.Should().BeNull("an unacknowledged event must never be marked complete");
        row.RetryCount.Should().BeGreaterThanOrEqualTo(1);

        // Recovery: the transient failure clears, the next worker run delivers. The
        // worker schedules attempt 2 after its 1-second backoff, so wait it out. The
        // receiver has two registered devices, so the retry delivers one push each.
        h.PushNotifier.OnIncomingCallAsync = null;
        await Task.Delay(1500);
        await h.RunOutboxAsync();
        h.PushNotifier.Incoming.Count(p => p.CallId == callId).Should().Be(2);

        await using var verify = await h.OpenDbAsync();
        var done = await verify.OutboxMessages.FirstAsync(o => o.AggregateId == callId.ToString());
        done.ProcessedAtUtc.Should().NotBeNull();
    }

    /// <summary>V05c: a terminal call's pending incoming is suppressed, never rung.</summary>
    [Fact]
    public async Task OutboxWorker_TerminalBeforeIncoming_SuppressesDelivery()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        (await sender.Send(new MarkCallMissedCommand(callId))).IsSuccess.Should().BeTrue();

        await h.RunOutboxAsync();

        h.PushNotifier.Incoming.Should().BeEmpty("a terminal call must never ring");
        await using var db = await h.OpenDbAsync();
        var deliveries = await db.VoiceCallEventDeliveries.Where(d => d.CallId == callId).ToListAsync();
        deliveries.Should().NotBeEmpty();
        deliveries.Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusSuppressed);
    }

    /// <summary>V08: the dispatcher route must forward the real measured fields.</summary>
    [Fact]
    public async Task DispatcherLocation_ForwardsAccuracyAndCaptureTime()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var capturedAt = DateTime.UtcNow.AddSeconds(-4);
        var response = await h.CreateDispatcherClient()
            .PostAsJsonAsync("/api/v1/dispatcher/drivers/location", new
            {
                driverId = h.DriverProfileId,
                tripId = h.TripId,
                latitude = 29.3974m,
                longitude = 47.9774m,
                accuracyMeters = 17d,
                capturedAtUtc = capturedAt
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the dispatcher route forwards the driver's real measurements; got {0}",
            await response.Content.ReadAsStringAsync());

        await using var db = await h.OpenDbAsync();
        var latest = await db.DeliveryTrackingPoints
            .Where(p => p.TripId == h.TripId)
            .OrderByDescending(p => p.ReceivedAtUtc ?? p.RecordedAtUtc)
            .FirstAsync();
        latest.AccuracyMeters.Should().Be(17d);
        latest.CapturedAtUtc.Should().BeCloseTo(capturedAt, TimeSpan.FromSeconds(2));
    }
}
