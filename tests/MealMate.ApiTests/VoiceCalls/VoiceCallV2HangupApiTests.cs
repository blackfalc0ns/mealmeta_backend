using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallV2HangupApiTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task EitherOwnerCanHangUpAfterAnswer_AndBothSeeTerminalState(bool callerEnds, bool mediaConnected)
    {
        await using var host = await VoiceCallTestHost.CreateAsync(extraSettings:
            new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });
        await host.RegisterDeviceAsync(host.DriverDeviceA, "hangup-driver");
        await host.RegisterDeviceAsync(host.CustomerDeviceA, "hangup-customer");
        var initiate = await host.DriverDeviceA.PostAsJsonAsync("/api/v2/voice-calls/initiate",
            new { tripStopId = host.TripStopId, clientRequestId = Guid.NewGuid() });
        initiate.EnsureSuccessStatusCode();
        var callId = (await initiate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
        (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", null)).EnsureSuccessStatusCode();

        if (mediaConnected)
        {
            (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connecting", null)).EnsureSuccessStatusCode();
            (await host.DriverDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", null)).EnsureSuccessStatusCode();
            (await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connected", null)).EnsureSuccessStatusCode();
        }

        var owner = callerEnds ? host.DriverDeviceA : host.CustomerDeviceA;
        var end = await owner.PostAsync($"/api/v2/voice-calls/{callId}/end", null);
        end.StatusCode.Should().Be(HttpStatusCode.OK);
        var ended = await end.Content.ReadFromJsonAsync<JsonElement>();
        var expectedState = mediaConnected ? "Ended" : "Failed";
        var expectedReason = mediaConnected ? (callerEnds ? "CallerEnded" : "ReceiverEnded") : "EndedBeforeConnect";
        ended.GetProperty("status").GetString().Should().Be(expectedState);
        ended.GetProperty("endReason").GetString().Should().Be(expectedReason);
        ended.GetProperty("endedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        foreach (var participant in new[] { host.DriverDeviceA, host.CustomerDeviceA })
        {
            var snapshot = await participant.GetFromJsonAsync<JsonElement>($"/api/v2/voice-calls/{callId}");
            snapshot.GetProperty("status").GetString().Should().Be(expectedState);
            snapshot.GetProperty("sequence").GetInt64().Should().Be(ended.GetProperty("sequence").GetInt64());
        }
        // Retrying hangup must not manufacture another terminal transition.
        var retry = await owner.PostAsync($"/api/v2/voice-calls/{callId}/end", null);
        retry.EnsureSuccessStatusCode();
        (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sequence").GetInt64()
            .Should().Be(ended.GetProperty("sequence").GetInt64());

        // Actor audit: the first /end records its authenticated HTTP requester
        // (account + device session), retries never re-attribute it, and the other
        // participant sees the terminal transition without gaining requester detail.
        await using var db = await host.OpenDbAsync();
        var call = await db.CallSessions.SingleAsync(c => c.Id == callId);
        call.EndedByUserId.Should().Be(callerEnds ? host.DriverUserId : host.CustomerUserId,
            "the first terminal request records its authenticated actor");
        call.EndedByDeviceSessionId.Should().Be(host.RegisteredDeviceSessions[callerEnds ? "hangup-driver" : "hangup-customer"]);
        call.Status.ToString().Should().Be(expectedState);
        if (!mediaConnected)
        {
            call.EndReason.Should().Be(Domain.VoiceCalls.CallEndReason.EndedBeforeConnect);
        }

        // The other side's /end retry does not re-attribute the recorded origin.
        var other = callerEnds ? host.CustomerDeviceA : host.DriverDeviceA;
        var otherEnd = await other.PostAsync($"/api/v2/voice-calls/{callId}/end", null);
        otherEnd.EnsureSuccessStatusCode();
        await using (var verify = await host.OpenDbAsync())
        {
            var reloaded = await verify.CallSessions.SingleAsync(c => c.Id == callId);
            reloaded.EndedByUserId.Should().Be(call.EndedByUserId, "the first terminal requester stays authoritative");
            reloaded.EndedByDeviceSessionId.Should().Be(call.EndedByDeviceSessionId);
            reloaded.Sequence.Should().Be(call.Sequence);
        }
    }
}
