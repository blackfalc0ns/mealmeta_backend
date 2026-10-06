using System.Net;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T01 privacy regressions (F01/F02). These tests are created in T00 failing on the
/// current code and flip green when stop-scoped authorization and phone removal land.
/// </summary>
public sealed class VoiceCallPrivacyRegressionTests
{
    [Fact]
    public async Task Outsider_CannotReadCallProxy()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.OutsiderClient.GetAsync(
            $"/api/v1/driver/orders/{h.TripStopId}/call-proxy");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Outsider_CannotReadTripStopCallHistory()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.OutsiderClient.GetAsync(
            $"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CallProxy_MustNotReturnRealCustomerPhone()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync(
            $"/api/v1/driver/orders/{h.TripStopId}/call-proxy");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(h.SeededCustomerPhone,
            "routine driver payloads must not disclose the stored customer phone");
    }
}
