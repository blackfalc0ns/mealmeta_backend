using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// Red-first regressions for the 2026-10-06 independent-review findings G02–G07.
/// Each test fails on the current implementation and is flipped green by its fix.
/// </summary>
public sealed class VoiceCallReviewFixTests
{
    private static async Task<HttpResponseMessage> InitiateAsync(VoiceCallTestHost h, HttpClient client, Guid? stopId = null, Guid? clientRequestId = null)
    {
        return await client.PostAsJsonAsync(
            "/api/v1/voice-calls/initiate",
            new { TripStopId = stopId ?? h.TripStopId, ClientRequestId = clientRequestId ?? Guid.NewGuid() });
    }

    /// <summary>G02: a newer INACTIVE assignment must never become the call receiver.</summary>
    [Fact]
    public async Task Initiate_CustomerToDriver_WithNewerInactiveAssignment_ReachesActiveDriver()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        // Driver B: a NEWER but reassigned (inactive) assignment on the same trip.
        Guid driverBUserId;
        await using (var db = await h.OpenDbAsync())
        {
            driverBUserId = Guid.NewGuid();
            var profileB = new DriverProfile
            {
                UserId = driverBUserId,
                FullNameAr = "سائق باء",
                FullNameEn = "Driver B",
                VerificationStatus = VerificationStatus.Approved
            };
            db.DriverProfiles.Add(profileB);
            db.Users.Add(new ApplicationUser
            {
                Id = driverBUserId,
                UserName = "drvb_" + driverBUserId.ToString("N")[..8],
                NormalizedUserName = ("DRVB_" + driverBUserId.ToString("N")[..8]).ToUpperInvariant(),
                UserType = UserType.Driver,
                FullName = "سائق باء",
                SecurityStamp = Guid.NewGuid().ToString("D"),
                EmailConfirmed = true,
                AccountStatus = AccountStatus.Active
            });
            db.DriverAssignments.Add(new DriverAssignment
            {
                TripId = h.TripId,
                DriverProfileId = profileB.Id,
                AssignedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Status = "Reassigned"
            });
            await db.SaveChangesAsync();
        }

        var response = await InitiateAsync(h, h.CustomerClient);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("receiverUserId").GetGuid().Should().Be(h.DriverUserId,
            "the receiver is the driver holding the ACTIVE assignment, never the newer inactive one");
        body.GetProperty("callerUserId").GetGuid().Should().Be(h.CustomerUserId);
    }

    /// <summary>G03 (REST half, revised): a legacy location update without measured accuracy
    /// is still tracked (stored with NULL accuracy for the dispatcher view) but is never
    /// selected as the eligibility fix — the previous good fix must remain the governing one.</summary>
    [Fact]
    public async Task LocationUpdate_WithoutAccuracy_IsTracked_ButNeverDisplacesEligibilityFix()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var response = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/driver/location",
            new { Latitude = 29.3974m, Longitude = 47.9774m }); // legacy body: no accuracyMeters

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "legacy pings are tracked with a NULL accuracy so dispatchers still see the driver");

        await using var db = await h.OpenDbAsync();
        var latest = await db.DeliveryTrackingPoints
            .Where(p => p.TripId == h.TripId)
            .OrderByDescending(p => p.CapturedAtUtc ?? p.RecordedAtUtc)
            .FirstAsync();
        latest.AccuracyMeters.Should().BeNull("the legacy ping carried no measured accuracy");

        // The NULL-accuracy ping is skipped by the eligibility fix selection, so the
        // seeded good fix is still the governing one and initiation succeeds.
        var initiate = await InitiateAsync(h, h.DriverClient);
        initiate.StatusCode.Should().Be(HttpStatusCode.OK,
            "a null-accuracy update must not have replaced the previous valid fix");
    }

    /// <summary>G03 (Hub half): the SignalR ingestion path carries real accuracy and
    /// capture time; verified at the command contract level (transport proof → T12).</summary>
    [Fact]
    public async Task LocationUpdate_WithAccuracyAndCapturedTime_PersistsThem()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        var capturedAt = DateTime.UtcNow.AddSeconds(-3);
        var response = await h.DriverClient.PostAsJsonAsync(
            "/api/v1/driver/location",
            new { Latitude = 29.3974m, Longitude = 47.9774m, AccuracyMeters = 18d, CapturedAtUtc = capturedAt });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await h.OpenDbAsync();
        var latest = await db.DeliveryTrackingPoints
            .Where(p => p.TripId == h.TripId)
            .OrderByDescending(p => p.CapturedAtUtc ?? p.RecordedAtUtc)
            .FirstAsync();
        latest.AccuracyMeters.Should().Be(18d);
        latest.CapturedAtUtc.Should().BeCloseTo(capturedAt, TimeSpan.FromSeconds(2),
            "the real capture time is stored, not overwritten by ingest time");
    }

    /// <summary>G04: an operationally ineligible driver (rejected / expired license) cannot call.</summary>
    [Theory]
    [InlineData("rejected")]
    [InlineData("expiredlicense")]
    public async Task Initiate_ByOperationallyIneligibleDriver_IsDenied(string variant)
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        await using (var db = await h.OpenDbAsync())
        {
            var profile = await db.DriverProfiles.FirstAsync(p => p.Id == h.DriverProfileId);
            if (variant == "rejected")
            {
                profile.VerificationStatus = VerificationStatus.Rejected;
            }
            else
            {
                profile.LicenseExpiryUtc = DateTime.UtcNow.AddDays(-1);
            }

            await db.SaveChangesAsync();
        }

        var response = await InitiateAsync(h, h.DriverClient);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("contact.driver_ineligible");
    }

    /// <summary>G05: the same idempotency key for a DIFFERENT stop is a conflict, not the old success.</summary>
    [Fact]
    public async Task IdempotencyKey_ReusedForDifferentStop_ReturnsMismatch()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        // A second stop in the same context — with its OWN subscription day, because
        // one active stop per subscription day is a real database constraint.
        Guid stopB;
        await using (var db = await h.OpenDbAsync())
        {
            var dayB = new Domain.CalendarMeals.SubscriptionDay
            {
                SubscriptionId = (await db.SubscriptionDays.FirstAsync(d => d.Id == h.SubscriptionDayId)).SubscriptionId,
                ServiceDate = System.DateOnly.FromDateTime(h.Clock.UtcNow).AddDays(1),
                Status = Domain.Enums.SubscriptionDayStatus.ConfirmedByRestaurant
            };
            db.SubscriptionDays.Add(dayB);
            await db.SaveChangesAsync();

            var stopB_ = new TripStop
            {
                TripId = h.TripId,
                SubscriptionDayId = dayB.Id,
                OrderCode = "ORD-B-" + Guid.NewGuid().ToString("N")[..6],
                CustomerMaskedId = "MM-VC-B",
                CustomerName = "عميل ب",
                CustomerPhone = h.SeededCustomerPhone,
                ZoneNameAr = "السالمية",
                ZoneNameEn = "Salmiya",
                MealSummaryAr = "وجبة ب",
                MealSummaryEn = "Meal B",
                SlotLabel = "Lunch",
                SequenceNumber = 2,
                Status = TripStopStatus.PickedUp,
                Latitude = 29.3983m,
                Longitude = 47.9774m
            };
            db.TripStops.Add(stopB_);
            db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
            {
                TripId = h.TripId,
                DriverProfileId = h.DriverProfileId,
                Latitude = 29.3974m,
                Longitude = 47.9774m,
                AccuracyMeters = 20,
                CapturedAtUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RecordedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            stopB = stopB_.Id;
        }

        var sameKey = Guid.NewGuid();
        var original = await InitiateAsync(h, h.DriverClient, h.TripStopId, sameKey);
        original.StatusCode.Should().Be(HttpStatusCode.OK);

        var mismatch = await InitiateAsync(h, h.DriverClient, stopB, sameKey);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a key bound to stop A must not succeed for stop B");
        var body = await mismatch.Content.ReadAsStringAsync();
        body.Should().Contain("calls.idempotency_mismatch");
    }

    /// <summary>G06: concurrent registrations for one user+installation converge to a
    /// single ACTIVE session — the database enforces it, not request timing.</summary>
    [Fact]
    public async Task ConcurrentRegistrations_SameInstallation_SingleActiveSession()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });

        var clients = Enumerable.Range(0, 8)
            .Select(_ => h.CreateClientForUser(h.CustomerUserId, UserType.Customer, ["Customer"]))
            .ToList();

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync(
            "/api/v2/voice-calls/devices/register",
            new { installationId = "race-install", platform = "android", capabilityVersion = 2 })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "concurrent rotations all complete; convergence is enforced server-side; bodies: {0}",
            string.Join(" | ", await Task.WhenAll(responses.Select(async r => await r.Content.ReadAsStringAsync()))));

        await using var db = await h.OpenDbAsync();
        var activeCount = await db.VoiceCallDeviceSessions
            .CountAsync(s => s.UserId == h.CustomerUserId
                             && s.InstallationId == "race-install"
                             && s.RevokedAtUtc == null);
        activeCount.Should().Be(1, "only one active session may survive a registration race");
    }

    /// <summary>G07: stop state must not leak to out-of-scope actors — 404 before any policy reason.</summary>
    [Theory]
    [InlineData(TripStopStatus.Delivered)]
    [InlineData(TripStopStatus.Pending)]
    public async Task Outsider_OnExistingStop_Gets404_RegardlessOfState(TripStopStatus status)
    {
        await using var h = await VoiceCallTestHost.CreateAsync(new VoiceCallTestSeed(StopStatus: status));

        var response = await InitiateAsync(h, h.OutsiderClient);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "out-of-scope actors learn nothing about a real stop, whatever its state");
    }
}
