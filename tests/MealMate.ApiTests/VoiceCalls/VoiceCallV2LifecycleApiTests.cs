using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T09 acceptance (AC34–AC38 partial: REST surface; Hub transport lands in T12):
/// device-winning answer, answered-elsewhere, reports to Active, terminal transitions
/// with Busy release, ringing-ack receipts and participant-scoped snapshots.
/// </summary>
public sealed class VoiceCallV2LifecycleApiTests
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

    private static async Task<JsonElement> AnswerAsync(HttpClient client, Guid callId)
    {
        var response = await client.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/answer", new { clientRequestId = Guid.NewGuid() });
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Answer_ByReceiverDevice_ClaimsWinner_AndRejectsOtherDevice()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        var first = await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first answering device wins atomically");
        var snapshot = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        snapshot.GetProperty("status").GetString().Should().Be("Accepted");
        snapshot.GetProperty("winningDeviceSessionId").GetGuid()
            .Should().Be(h.RegisteredDeviceSessions["customer-v2-a"], "the winner is recorded on the call");

        // A second receiver device must not take over control.
        var second = await h.CustomerDeviceB.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("calls.answered_elsewhere");

        // The winner's retry is idempotent — same snapshot, no reset.
        var retry = await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Accepted);
        call.ReceiverDeviceSessionId.Should().Be(h.RegisteredDeviceSessions["customer-v2-a"]);
    }

    [Fact]
    public async Task Reports_BothDevices_ActivateCall_Once()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // ReportConnecting from either owner → Connecting.
        var connecting = await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connecting", content: null);
        connecting.StatusCode.Should().Be(HttpStatusCode.OK);

        // First connected report → still Connecting (not Active yet).
        var firstConnected = await h.DriverDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", content: null);
        firstConnected.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterFirst = JsonDocument.Parse(await firstConnected.Content.ReadAsStringAsync()).RootElement;
        afterFirst.GetProperty("status").GetString().Should().Be("Connecting");

        // Second connected report → Active exactly once, with no deadline reset on repeats.
        var secondConnected = await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", content: null);
        secondConnected.StatusCode.Should().Be(HttpStatusCode.OK);
        var active = JsonDocument.Parse(await secondConnected.Content.ReadAsStringAsync()).RootElement;
        active.GetProperty("status").GetString().Should().Be("Active");

        var repeat = await h.DriverDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", content: null);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await repeat.Content.ReadAsStringAsync()).RootElement
            .GetProperty("status").GetString().Should().Be("Active");

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Active);
        call.ConnectedAtUtc.Should().NotBeNull();
        call.CallerConnectedReportedAtUtc.Should().NotBeNull();
        call.ReceiverConnectedReportedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Reports_FromNonOwnerDevice_AreDenied()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The loser receiver device cannot report media state.
        var denied = await h.CustomerDeviceB.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", content: null);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_ByReceiverDevice_TerminalsCall_AndReleasesBusy()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        var reject = await h.CustomerDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/reject", new { });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Rejected);
        (await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId)).Should().Be(0);
        (await db.OutboxMessages.AnyAsync(o => o.AggregateId == callId.ToString()
                                               && o.EventType == "voice-call.lifecycle.rejected")).Should().BeTrue();

        // Next call can start immediately.
        var next = await InitiateV2Async(h);
        next.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Cancel_ByCallerDevice_TerminalsCall_AndReleasesBusy()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        var cancel = await h.DriverDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Cancelled);
        (await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId)).Should().Be(0);
    }

    [Fact]
    public async Task TerminalReject_IsIdempotent_AndNonOwnerCannotReject()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        (await h.CustomerDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/reject", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Rejecting again returns the same terminal snapshot without a new event.
        var again = await h.CustomerDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/reject", new { });
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await h.OpenDbAsync();
        var terminalEvents = await db.OutboxMessages.CountAsync(o => o.AggregateId == callId.ToString()
                                                                     && o.EventType == "voice-call.lifecycle.rejected");
        terminalEvents.Should().Be(1, "the terminal event is stored exactly once");

        // The caller device cannot reject (reject is the receiver's action).
        var secondCallId = await InitiateV2Async(h);
        var wrongSide = await h.DriverDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{secondCallId}/reject", new { });
        wrongSide.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RingingAck_RecordsReceipt_ButDoesNotClaimOwnership()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        var ack = await h.CustomerDeviceB.PostAsync($"/api/v2/voice-calls/{callId}/ringing-ack", content: null);
        ack.StatusCode.Should().Be(HttpStatusCode.OK, "any owned receiver device may acknowledge the ring");

        await using var db = await h.OpenDbAsync();
        var receipt = await db.VoiceCallDeviceReceipts.FirstOrDefaultAsync(r => r.CallId == callId);
        receipt.Should().NotBeNull();
        receipt!.RingingAckAtUtc.Should().NotBeNull();
        // No ownership claim: the call is still unclaimed.
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.ReceiverDeviceSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Heartbeat_FromOwnerDevice_RefreshesLease_NonOwnerDenied()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var beat = await h.CustomerDeviceA.PostAsJsonAsync(
            $"/api/v2/voice-calls/{callId}/heartbeat", new { mediaState = "active", lastSequenceSeen = 1 });
        beat.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await beat.Content.ReadAsStringAsync()).RootElement;
        body.TryGetProperty("leaseExpiresAtUtc", out var leaseEl).Should().BeTrue(
            "the owner heartbeat returns its refreshed lease; got {0}", await beat.Content.ReadAsStringAsync());
        leaseEl.GetDateTime().Should().BeAfter(DateTime.UtcNow);

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.ReceiverLastHeartbeatAtUtc.Should().NotBeNull();
        call.MediaLeaseExpiresAtUtc.Should().NotBeNull();

        // The unclaimed caller's device may also beat; a non-owner JWT may not.
        var outsiderBeat = await h.OutsiderClient.PostAsJsonAsync(
            $"/api/v2/voice-calls/{callId}/heartbeat", new { mediaState = "active" });
        outsiderBeat.StatusCode.Should().Match(x => x == HttpStatusCode.Forbidden || x == HttpStatusCode.NotFound,
            "an out-of-scope account learns nothing about the call");
    }

    [Fact]
    public async Task Snapshots_AreParticipantScoped_AndTerminalByIdWorks()
    {
        var h = await CreateV2HostAsync();
        var callId = await InitiateV2Async(h);

        var byId = await h.CustomerClient.GetAsync($"/api/v2/voice-calls/{callId}");
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await byId.Content.ReadAsStringAsync()).RootElement
            .GetProperty("callId").GetGuid().Should().Be(callId);

        var outsider = await h.OutsiderClient.GetAsync($"/api/v2/voice-calls/{callId}");
        outsider.StatusCode.Should().Be(HttpStatusCode.NotFound, "out-of-scope accounts learn nothing");

        (await h.CustomerDeviceA.PostAsJsonAsync($"/api/v2/voice-calls/{callId}/reject", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Terminal by-id supports reconciliation after reconnect.
        var terminal = await h.CustomerDeviceA.GetAsync($"/api/v2/voice-calls/{callId}");
        terminal.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await terminal.Content.ReadAsStringAsync()).RootElement
            .GetProperty("status").GetString().Should().Be("Rejected");

        var history = await h.DriverClient.GetAsync($"/api/v2/voice-calls/history/{h.TripStopId}");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
