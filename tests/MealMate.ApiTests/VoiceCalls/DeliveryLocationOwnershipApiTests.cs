using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NSubstitute;
using MealMate.ApiTests.VoiceCalls.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T04 acceptance (AC16/AC17): driver location ingestion is ownership-scoped
/// (explicit trip must be an active assignment of this driver), never fabricates a
/// trip, and validates coordinates by range — (0,0) stays geographically valid.
/// </summary>
public sealed class DeliveryLocationOwnershipApiTests
{
    private static Task<HttpResponseMessage> PostLocationAsync(VoiceCallTestHost h, object body) =>
        h.DriverClient.PostAsJsonAsync("/api/v1/driver/location", body);

    [Fact]
    public async Task ZeroCoordinates_AreGeographicallyValid()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await PostLocationAsync(h, new { Latitude = 0m, Longitude = 0m, AccuracyMeters = 20d });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "(0,0) passes range validation; only out-of-range coordinates are rejected");
    }

    [Fact]
    public async Task OutOfRangeCoordinates_AreRejected()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await PostLocationAsync(h, new { Latitude = 91m, Longitude = 0m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForeignTrip_IsRejected()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        // A trip that exists but is not assigned to this driver. The driver REST route
        // never accepts a TripId (identity/ownership from JWT only), so the explicit
        // TripId path is the dispatcher route — exercised at the handler boundary.
        Guid driverProfileId;
        await using (var db = await h.OpenDbAsync())
        {
            driverProfileId = h.DriverProfileId;
            var foreignTrip = new Domain.DriverDelivery.Trip
            {
                RestaurantId = h.RestaurantId,
                Code = "TRIP-FOREIGN-" + Guid.NewGuid().ToString("N")[..8],
                Status = Domain.Enums.TripStatus.InProgress,
                PlannedDate = System.DateOnly.FromDateTime(h.Clock.UtcNow)
            };
            db.Trips.Add(foreignTrip);
            await db.SaveChangesAsync();
            _foreignTripId = foreignTrip.Id;
        }

        var currentUser = Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>();
        var handler = new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommandHandler(
            await h.OpenDbAsync(),
            currentUser,
            new MealMate.Application.Common.Localization.NullLocalizedMessageProvider());

        var result = await handler.Handle(
            new MealMate.Application.Features.DriverDelivery.Dispatcher.UpdateDriverLocationCommand(
                DriverId: driverProfileId,
                TripId: _foreignTripId,
                Latitude: 29.3m,
                Longitude: 47.9m),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse(
            "an explicit trip that is not this driver's active assignment must be refused");
    }

    private Guid _foreignTripId;

    [Fact]
    public async Task NoActiveTrip_CreatesNoFabricatedTrip()
    {
        // A driver with no active work: fresh host, then detach the assignment so the
        // has-active-work gate rejects the update.
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(ActiveDriverAssignment: false));

        var tripsBefore = 0;
        await using (var db = await h.OpenDbAsync())
        {
            tripsBefore = await db.Trips.CountAsync();
        }

        var response = await PostLocationAsync(h, new { Latitude = 29.3m, Longitude = 47.9m, AccuracyMeters = 20d });

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "tracking without active delivery work is refused and never fabricates a trip");

        await using var verify = await h.OpenDbAsync();
        var tripsAfter = await verify.Trips.CountAsync();
        tripsAfter.Should().Be(tripsBefore, "no trip may be created to satisfy the FK");
    }

    [Fact]
    public async Task ValidUpdate_PersistsServerTimestampsAndAccuracy()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await PostLocationAsync(h, new { Latitude = 29.3974m, Longitude = 47.9774m, AccuracyMeters = 25d });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Yarmouk", "the fabricated default zone was removed");
        body.Should().Contain("remainingDistanceKm", "the distance shown is computed from the real next stop");

        await using var db = await h.OpenDbAsync();
        var latest = await db.DeliveryTrackingPoints
            .Where(p => p.TripId == h.TripId)
            .OrderByDescending(p => p.ReceivedAtUtc ?? p.RecordedAtUtc)
            .FirstAsync();
        latest.AccuracyMeters.Should().Be(25d);
        latest.CapturedAtUtc.Should().NotBeNull();
        latest.ReceivedAtUtc.Should().NotBeNull();
    }
}
