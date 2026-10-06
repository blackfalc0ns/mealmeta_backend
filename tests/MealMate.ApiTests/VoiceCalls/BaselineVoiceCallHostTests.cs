using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T00 baseline: proves the isolated host serves the real API over disposable
/// PostgreSQL with production DI (retry, MediatR pipeline) and that JWT identity
/// is resolved server-side (no ICurrentUser substitution in HTTP tests).
/// </summary>
public sealed class BaselineVoiceCallHostTests
{
    [Fact]
    public async Task Baseline_CanAuthenticateAndReadOwnStop()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonPagedShape>();
        payload.Should().NotBeNull();
        payload!.Page.Should().Be(1);
    }

    [Fact]
    public async Task Baseline_OutsiderHasDifferentIdentity()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        // A participant-scoped read: only caller/receiver resolve the call.
        Guid callId;
        await using (var db = await h.OpenDbAsync())
        {
            var call = new CallSession(
                h.TripStopId,
                h.DriverUserId,
                CallParticipantType.Driver,
                h.CustomerUserId,
                CallParticipantType.Customer,
                Guid.NewGuid(),
                h.Clock.UtcNow);
            call.MarkRinging(h.Clock.UtcNow);
            db.CallSessions.Add(call);
            await db.SaveChangesAsync();
            callId = call.Id;
        }

        var driverResponse = await h.DriverClient.GetAsync($"/api/v1/voice-calls/{callId}");
        driverResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var outsiderResponse = await h.OutsiderClient.GetAsync($"/api/v1/voice-calls/{callId}");
        outsiderResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record JsonPagedShape(int Page, int PageSize, int TotalCount);
}
