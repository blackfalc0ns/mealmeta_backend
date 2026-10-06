using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T08 acceptance (AC29–AC33): atomic device-owned v2 initiation over real HTTP —
/// one call + two Busy rows + receipt + lifecycle outbox event in one commit,
/// concurrent-race convergence, idempotent retry, key misuse, and receiver-busy
/// rejection with no partial rows.
/// </summary>
public sealed class VoiceCallV2InitiationApiTests
{
    private static IReadOnlyDictionary<string, string?> V2On { get; } =
        new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" };

    private static async Task<HttpResponseMessage> InitiateAsync(HttpClient client, Guid stopId, Guid clientRequestId) =>
        await client.PostAsJsonAsync("/api/v2/voice-calls/initiate", new { tripStopId = stopId, clientRequestId });

    private async Task<(VoiceCallTestHost Host, Guid CallId)> CreateRacingHostAsync()
    {
        var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceB, "customer-v2-b");
        return (h, Guid.Empty);
    }

    [Fact]
    public async Task Initiate_CreatesCallBusyReceiptAndOutboxAtomically()
    {
        var (h, _) = await CreateRacingHostAsync();
        var clientRequestId = Guid.NewGuid();

        var response = await InitiateAsync(h.DriverDeviceA, h.TripStopId, clientRequestId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Ringing");
        body.GetProperty("protocolVersion").GetInt32().Should().Be(2);
        body.GetProperty("localParticipantRole").GetString().Should().Be("Driver");
        var callId = body.GetProperty("callId").GetGuid();

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.ProtocolVersion.Should().Be(2);
        call.CallerDeviceSessionId.Should().NotBeNull();
        call.RingExpiresAtUtc.Should().NotBeNull();
        call.Sequence.Should().Be(1);

        var busyCount = await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId);
        busyCount.Should().Be(2, "caller and receiver each hold exactly one busy slot");

        var receipt = await db.IdempotencyRecords.SingleAsync(
            r => r.Scope == "voice-calls:v2:initiate" && r.UserId == h.DriverUserId);
        receipt.ResourceUri.Should().Be(callId.ToString());

        var outboxCount = await db.OutboxMessages.CountAsync(
            o => o.DeduplicationKey == $"voice-call:{callId}:1:incoming");
        outboxCount.Should().Be(1, "the incoming lifecycle event is staged in the same transaction");
    }

    [Fact]
    public async Task Initiate_UnregisteredDevice_IsForbidden()
    {
        var (h, _) = await CreateRacingHostAsync();

        // A client with a valid JWT but no device session headers.
        var response = await InitiateAsync(h.DriverClient, h.TripStopId, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "v2 mutations require a resolved device-control context");
    }

    [Fact]
    public async Task Initiate_ConcurrentOpposingRequests_ConvergeToOneCall()
    {
        var (h, _) = await CreateRacingHostAsync();
        var stopId = h.TripStopId;

        var driverTask = InitiateAsync(h.DriverDeviceA, stopId, Guid.NewGuid());
        var customerTask = InitiateAsync(h.CustomerDeviceA, stopId, Guid.NewGuid());
        await Task.WhenAll(driverTask, customerTask);

        var statuses = new[] { (int)driverTask.Result.StatusCode, (int)customerTask.Result.StatusCode };
        statuses.Should().Contain((int)HttpStatusCode.OK, "one side wins creation");
        statuses.Should().Contain((int)HttpStatusCode.Conflict, "the other side is busy on the winning call");

        await using var db = await h.OpenDbAsync();
        var callCount = await db.CallSessions.CountAsync(c => c.TripStopId == stopId && c.ProtocolVersion == 2);
        callCount.Should().Be(1, "A→B and B→A under the same locks produce exactly one call");
    }

    [Fact]
    public async Task Initiate_IdempotentRetry_SameKeySameDevice_ReturnsSameCall()
    {
        var (h, _) = await CreateRacingHostAsync();
        var clientRequestId = Guid.NewGuid();

        var first = await InitiateAsync(h.DriverDeviceA, h.TripStopId, clientRequestId);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var callId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();

        var retry = await InitiateAsync(h.DriverDeviceA, h.TripStopId, clientRequestId);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var retryCallId = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();

        retryCallId.Should().Be(callId, "an honest retry returns the existing call");

        await using var db = await h.OpenDbAsync();
        var callCount = await db.CallSessions.CountAsync(c => c.TripStopId == h.TripStopId);
        var receiptCount = await db.IdempotencyRecords.CountAsync(
            r => r.Scope == "voice-calls:v2:initiate" && r.Key == clientRequestId.ToString("N"));
        callCount.Should().Be(1);
        receiptCount.Should().Be(1);
    }

    [Fact]
    public async Task Initiate_SameKeyDifferentStop_ReturnsMismatch()
    {
        var (h, _) = await CreateRacingHostAsync();
        var clientRequestId = Guid.NewGuid();

        var first = await InitiateAsync(h.DriverDeviceA, h.TripStopId, clientRequestId);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Retry the same key against a nonexistent stop — a different request intent.
        var mismatch = await InitiateAsync(h.DriverDeviceA, Guid.NewGuid(), clientRequestId);

        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await mismatch.Content.ReadAsStringAsync();
        body.Should().Contain("calls.idempotency_mismatch");
    }

    [Fact]
    public async Task Initiate_WhenReceiverBusy_NoPartialRowsFromFailedAttempt()
    {
        var (h, _) = await CreateRacingHostAsync();
        var clientRequestId = Guid.NewGuid();

        // First call: driver → customer (customer now busy).
        var first = await InitiateAsync(h.DriverDeviceA, h.TripStopId, clientRequestId);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var callId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();

        // A third party stop attempt: the CUSTOMER (busy) tries to initiate toward the
        // driver on another stop — rejected with no partial rows of its own.
        var customerInitiate = await InitiateAsync(h.CustomerDeviceA, h.TripStopId, Guid.NewGuid());
        customerInitiate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errBody = await customerInitiate.Content.ReadAsStringAsync();
        errBody.Should().Contain("calls.caller_busy");

        await using var db = await h.OpenDbAsync();
        var calls = await db.CallSessions.CountAsync(c => c.TripStopId == h.TripStopId);
        var busyRows = await db.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId);
        calls.Should().Be(1, "the failed attempt created no call");
        busyRows.Should().Be(2, "no orphan busy rows from the failed attempt");
    }
}
