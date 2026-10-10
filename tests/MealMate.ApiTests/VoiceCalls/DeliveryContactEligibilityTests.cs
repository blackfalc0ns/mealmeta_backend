using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T04 acceptance (AC10–AC15, AC18): server-enforced contact eligibility — stop
/// status, trip state, assignment, account, location freshness/accuracy, and the
/// temporarily disabled 3km radius — exercised through the real initiation path.
/// </summary>
public sealed class DeliveryContactEligibilityTests
{
    private static async Task<HttpResponseMessage> InitiateAsync(VoiceCallTestHost h)
    {
        return await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = Guid.NewGuid() });
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task EligibleContext_InitiateSucceeds()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PendingStop_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(StopStatus: Domain.Enums.TripStopStatus.Pending));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.stop_not_active");
    }

    [Fact]
    public async Task DeliveredStop_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(StopStatus: Domain.Enums.TripStopStatus.Delivered));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.stop_not_active");
    }

    [Fact]
    public async Task PlannedTrip_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(TripStatus: Domain.Enums.TripStatus.Planned));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.trip_not_in_progress");
    }

    [Fact]
    public async Task InactiveAssignment_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(ActiveDriverAssignment: false));

        var response = await InitiateAsync(h);

        // G07 semantics: with no ACTIVE assignment the former driver is out of scope
        // and receives the same 404 as any outsider — never a state reveal.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingLocation_IsDeniedWithoutFallback()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        await using (var db = await h.OpenDbAsync())
        {
            await db.DeliveryTrackingPoints.ExecuteDeleteAsync();
            await db.SaveChangesAsync();
        }

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.location_missing");
    }

    [Fact]
    public async Task StaleLocation_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(LocationAgeSeconds: 120));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.location_stale");
    }

    [Fact]
    public async Task PoorAccuracy_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(AccuracyMeters: 150));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.location_accuracy_low");
    }

    [Fact]
    public async Task MissingAccuracy_LegacyPing_IsNeverSelectedAsEligibilityFix()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(AccuracyMeters: null));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.location_missing");
    }

    [Theory]
    [InlineData(2999, HttpStatusCode.OK, "")]
    // TEMPORARY: restore the denial beyond 3km when the production radius checks are uncommented.
    [InlineData(3001, HttpStatusCode.OK, "")]
    [InlineData(10000, HttpStatusCode.OK, "")]
    public async Task DistanceBoundary_IsTemporarilyDisabled(double meters, HttpStatusCode expected, string reasonFragment)
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(DistanceMeters: meters));

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(expected);
        if (reasonFragment.Length > 0)
        {
            (await BodyAsync(response)).Should().Contain(reasonFragment);
        }
    }

    [Fact]
    public async Task FutureLocationTimestamp_IsRejectedAsReplay()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        await using (var db = await h.OpenDbAsync())
        {
            var fix = await db.DeliveryTrackingPoints.FirstAsync();
            // The eligibility service runs on the production clock, so the replayed fix
            // must be future relative to DateTime.UtcNow, not the host's test clock.
            fix.CapturedAtUtc = DateTime.UtcNow.AddHours(2);
            fix.ReceivedAtUtc = DateTime.UtcNow.AddHours(2);
            await db.SaveChangesAsync();
        }

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.location_invalid");
    }

    [Fact]
    public async Task StopWithoutCoordinates_IsDenied()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        await using (var db = await h.OpenDbAsync())
        {
            var stop = await db.TripStops.FirstAsync(s => s.Id == h.TripStopId);
            stop.Latitude = null;
            stop.Longitude = null;
            await db.SaveChangesAsync();
        }

        var response = await InitiateAsync(h);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).Should().Contain("contact.stop_location_missing");
    }

    [Fact]
    public async Task RetryOfExistingCall_BypassesCurrentEligibility()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();
        var clientRequestId = Guid.NewGuid();

        var first = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = clientRequestId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // The driver drives out of the 3km window after the call started.
        await using (var db = await h.OpenDbAsync())
        {
            var fix = await db.DeliveryTrackingPoints.FirstAsync();
            fix.Latitude = 28.0m;
            await db.SaveChangesAsync();
        }

        var retry = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = clientRequestId });

        retry.StatusCode.Should().Be(HttpStatusCode.OK,
            "reconnecting to the same call never re-imposes the 3km rule");
    }
}
