using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T01 acceptance (AC01–AC05): stop-scoped authorization on call surfaces and no
/// customer-phone disclosure in routine driver payloads.
/// </summary>
public sealed class VoiceCallAuthorizationApiTests
{
    [Fact]
    public async Task AssignedDriver_CanReadOwnStopHistory()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomerParticipant_CanReadStopHistory()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.CustomerClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OutsiderAdminRole_DoesNotImplicitlyReadHistory()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        // An operations admin with no participant relationship on this stop gets the
        // same out-of-scope answer as any other outsider; read-history supervision is
        // a separate scoped permission, never an implicit all-admin bypass.
        var adminClient = h.CreateClientForUser(
            h.OutsiderUserId,
            UserType.Admin,
            ["OperationsAdmin"]);

        var response = await adminClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DriverWithInactiveAssignment_CannotReadStopHistory()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(ActiveDriverAssignment: false));

        var response = await h.DriverClient.GetAsync($"/api/v1/voice-calls/history/{h.TripStopId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CallProxy_IsRetiredForEveryone()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var driverResponse = await h.DriverClient.GetAsync($"/api/v1/driver/orders/{h.TripStopId}/call-proxy");
        var outsiderResponse = await h.OutsiderClient.GetAsync($"/api/v1/driver/orders/{h.TripStopId}/call-proxy");

        driverResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var driverBody = await driverResponse.Content.ReadAsStringAsync();
        driverBody.Should().NotContain(h.SeededCustomerPhone);
    }

    [Fact]
    public async Task HomeCurrentTask_CarriesNoCustomerPhone()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync("/api/v1/driver/home");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(h.SeededCustomerPhone);
    }

    [Fact]
    public async Task ConfirmArrival_CarriesNoCustomerPhone()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.PostAsJsonAsync(
            $"/api/v1/driver/orders/{h.TripStopId}/arrive-customer",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(h.SeededCustomerPhone);
    }

    [Fact]
    public async Task ConfirmArrival_RejectsOutsider()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.OutsiderClient.PostAsJsonAsync(
            $"/api/v1/driver/orders/{h.TripStopId}/arrive-customer",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapPayload_CarriesNoCustomerPhone()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.GetAsync("/api/v1/driver/map/route");

        // The map endpoint may require additional operational state; either way the
        // payload must never contain the stored customer phone.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(h.SeededCustomerPhone);
    }
}
