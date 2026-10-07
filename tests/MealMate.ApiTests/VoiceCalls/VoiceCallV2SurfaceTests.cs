using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T05 acceptance (AC20/AC21): the declared v2 surface never fakes success before
/// its implementing tasks land, and the legacy creation switch genuinely gates v1
/// initiation without affecting read/terminal paths.
/// </summary>
public sealed class VoiceCallV2SurfaceTests
{
    [Fact]
    public async Task V2Routes_AreDisabledWhileFlagOff()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync("/api/v2/voice-calls/active");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "VoiceCalls:V2Enabled defaults to false; declared-but-disabled surfaces report 503, never fabricated success");
    }

    [Fact]
    public async Task V2Routes_DeclaredButUnimplementedReturn501()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });

        // All v2 routes are implemented; an unknown route under the v2 prefix must 404
        // (never fabricate success), and a known route without device context stays 403.
        var unknown = await h.DriverDeviceA.GetAsync($"/api/v2/voice-calls/{Guid.NewGuid()}/nonexistent");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var noDevice = await h.DriverClient.PostAsJsonAsync(
            $"/api/v2/voice-calls/{Guid.NewGuid()}/answer",
            new { clientRequestId = Guid.NewGuid() });
        noDevice.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "mutations require a resolved device-control context");
    }

    [Fact]
    public async Task LegacyInitiationSwitch_DisablesV1Creation()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["VoiceCalls:LegacyInitiationEnabled"] = "false" });

        var response = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = h.TripStopId, ClientRequestId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("calls.legacy_initiation_disabled");
    }

    [Fact]
    public async Task LegacyReadPath_StillWorksWhenCreationDisabled()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["VoiceCalls:LegacyInitiationEnabled"] = "false" });

        var response = await h.DriverClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "closing legacy creation must not close legacy read/terminal paths");
    }
}
