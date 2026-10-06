using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T02 acceptance (AC06/AC07): call initiation reaches the participant lock through
/// the real HTTP pipeline with the production persistence registration
/// (EnableRetryOnFailure) and succeeds without execution-strategy errors.
/// </summary>
public sealed class VoiceCallStartApiTests
{
    [Fact]
    public async Task Initiate_ThroughRealHttpPipeline_CreatesRingingCall()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "initiation must succeed through AddPersistence retry configuration and the participant lock");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Ringing");
        body.GetProperty("callerUserId").GetGuid().Should().Be(h.DriverUserId);
        body.GetProperty("receiverUserId").GetGuid().Should().Be(h.CustomerUserId);
    }

    [Fact]
    public async Task Initiate_RetryWithSameClientRequestId_ReturnsSameCall()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();
        var clientRequestId = Guid.NewGuid();

        var first = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = clientRequestId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = clientRequestId });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstCallId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
        var secondCallId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
        secondCallId.Should().Be(firstCallId, "idempotent retry returns the existing session, not a new one");

        await using var db = await h.OpenDbAsync();
        var callCount = await db.CallSessions.CountAsync(c => c.TripStopId == h.TripStopId);
        callCount.Should().Be(1);
    }
}
