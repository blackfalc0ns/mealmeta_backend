using System.Net;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Models;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// HTTP adapter tests for GoogleRoutingService — no network, no real key.
/// The service must hand back real Google geometry only, and must translate
/// every not-ready answer (missing metrics, ZERO_RESULTS, HTTP error, network
/// crash) into the existing fallback estimate instead of a zero-success.
/// </summary>
public class DriverGoogleRoutingTests
{
    private const string RealPolyline = "_p~iF~ps|U_ulLnnqC";

    private static GoogleRoutingService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://maps.googleapis.com/") },
        Options.Create(new GoogleMapsOptions { ApiKey = "unit-test-key" }),
        NullLogger<GoogleRoutingService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request, cancellationToken));
    }

    private static string DirectionsJson(
        string status = "OK",
        bool includePolyline = true,
        bool includeDistance = true,
        bool includeDuration = true,
        long distanceValue = 5200,
        long durationValue = 480)
    {
        var polyline = includePolyline
            ? "\"overview_polyline\": { \"points\": \"" + RealPolyline + "\" },"
            : string.Empty;
        var distance = includeDistance
            ? "\"distance\": { \"text\": \"5.2 km\", \"value\": " + distanceValue + " }"
            : "\"distance\": null";
        var duration = includeDuration
            ? "\"duration\": { \"text\": \"8 mins\", \"value\": " + durationValue + " }"
            : "\"duration\": null";
        return $$"""
            {
              "status": "{{status}}",
              "routes": [
                {
                  {{polyline}}
                  "legs": [
                    { {{distance}}, {{duration}} }
                  ]
                }
              ]
            }
            """;
    }

    [Fact]
    public async Task GetDirectionsAsync_WithValidGoogleResponse_ReturnsRealGeometry()
    {
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.OK, DirectionsJson())));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        var directions = result.Value;
        directions.IsFallbackEstimate.Should().BeFalse();
        directions.OverviewPolyline.Should().Be(RealPolyline);
        directions.DistanceMeters.Should().Be(5200);
        directions.DurationSeconds.Should().Be(480);
    }

    [Fact]
    public async Task GetDirectionsAsync_WithZeroMetrics_IsRealSuccess_NotFallback()
    {
        // A zero-length trip is valid data, not a missing metric.
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.OK, DirectionsJson(distanceValue: 0, durationValue: 0))));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeFalse();
        result.Value.DistanceMeters.Should().Be(0);
        result.Value.DurationSeconds.Should().Be(0);
    }

    [Fact]
    public async Task GetDirectionsAsync_WithMissingDistance_FallsBack_NotZeroSuccess()
    {
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.OK, DirectionsJson(includeDistance: false))));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeTrue();
        result.Value.OverviewPolyline.Should().BeNull();
        result.Value.DistanceMeters.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetDirectionsAsync_WithMissingDuration_FallsBack_NotZeroSuccess()
    {
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.OK, DirectionsJson(includeDuration: false))));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeTrue();
        result.Value.OverviewPolyline.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectionsAsync_WithZeroResults_FallsBack()
    {
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.OK, DirectionsJson(status: "ZERO_RESULTS"))));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeTrue();
        result.Value.OverviewPolyline.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectionsAsync_WithHttpError_FallsBack()
    {
        var service = CreateService(new StubHandler((_, _) => Json(HttpStatusCode.InternalServerError, "{}")));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeTrue();
        result.Value.OverviewPolyline.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectionsAsync_WhenNetworkThrows_FallsBack()
    {
        var service = CreateService(new StubHandler((_, _) => throw new HttpRequestException("network unreachable")));

        var result = await service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFallbackEstimate.Should().BeTrue();
        result.Value.OverviewPolyline.Should().BeNull();
    }

    [Fact]
    public async Task GetDirectionsAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService(new StubHandler((_, token) => throw new TaskCanceledException("cancelled", null, token)));

        var act = () => service.GetDirectionsAsync(29.3375m, 48.0280m, 29.3380m, 48.0230m, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }
}
