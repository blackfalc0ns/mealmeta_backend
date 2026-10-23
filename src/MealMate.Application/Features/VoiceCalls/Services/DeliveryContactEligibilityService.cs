using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// Authoritative contact-eligibility policy (spec 4.1). Policy values are the spec's
/// proposed defaults and stay subject to G-POLICY adoption; the F14 radius rule
/// is temporarily disabled for call testing. A driver with DriverProfile.IsAvailable=false because
/// they are mid-delivery stays eligible on their existing stop — assignment
/// availability and live-contact eligibility are different concepts.
/// </summary>
public sealed class DeliveryContactEligibilityService : IDeliveryContactEligibilityService
{
    /// <summary>Proposed: fix older than this is stale. Pending G-POLICY.</summary>
    public const double LocationFreshnessSeconds = 60d;

    /// <summary>Proposed: fix less accurate than this is rejected. Pending G-POLICY.</summary>
    public const double LocationMaxAccuracyMeters = 100d;

    /// <summary>Standing F14 rule: contact window is 3km around the stop.</summary>
    public const double ContactRadiusMeters = 3000d;

    /// <summary>Server-clock skew tolerated when rejecting future timestamps (replay guard).</summary>
    private static readonly TimeSpan TimestampSkewTolerance = TimeSpan.FromMinutes(2);

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public DeliveryContactEligibilityService(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<DeliveryContactEligibilityDto>> EvaluateAsync(Guid tripStopId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure<DeliveryContactEligibilityDto>(VoiceCallErrors.TripStopNotFound());
        }

        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == tripStopId, cancellationToken);

        if (stop is null)
        {
            return Result.Failure<DeliveryContactEligibilityDto>(VoiceCallErrors.TripStopNotFound());
        }

        // G07 ordering: participants are resolved and the actor authorized BEFORE any
        // state/account/location policy detail is produced — an out-of-scope actor gets
        // the same 404 as a missing resource regardless of stop/trip state.
        var participants = await ResolveParticipantsAsync(tripStopId, cancellationToken);
        if (participants.IsFailure)
        {
            return Result.Failure<DeliveryContactEligibilityDto>(participants.Error);
        }

        var resolved = participants.Value;
        var isDriver = resolved.DriverUserId == actorUserId;
        var isCustomer = resolved.CustomerUserId == actorUserId;
        if (!isDriver && !isCustomer)
        {
            return Result.Failure<DeliveryContactEligibilityDto>(VoiceCallErrors.TripStopNotFound());
        }

        var verdict = await EvaluateCallPolicyAsync(stop, resolved, cancellationToken);
        // A read must not open a new contact cycle. Never inherit rights from a
        // previous driver assignment, closed cycle, or another stop.
        var contactCase = await _db.DeliveryContactCases.AsNoTracking()
            .Where(c => c.TripStopId == tripStopId
                && c.ActiveDriverAssignmentId == resolved.ActiveDriverAssignmentId
                && c.State == DeliveryContactCase.StateOpen)
            .OrderByDescending(c => c.ContactCycleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (contactCase is null)
            return verdict;

        var hold = contactCase.HoldId.HasValue
            ? await _db.DeliveryHolds.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == contactCase.HoldId.Value, cancellationToken)
            : null;
        var resumed = hold?.ResumedAtUtc is not null;
        var driverActionsAllowed = isDriver && stop.Status == TripStopStatus.PickedUp
            && stop.Trip?.Status == TripStatus.InProgress;
        return Result.Success(verdict.Value with
        {
            ContactCaseId = contactCase.Id,
            // Temporarily hide Hold in eligibility at user request for mobile call testing.
            CanHold = false,
            // Resume already enforces cooldown; time alone must never release a hold.
            CanRetry = driverActionsAllowed && verdict.Value.CanInitiate
                && contactCase.QualifiedMissedCount >= 1 && resumed,
            CanRevealPhone = driverActionsAllowed && contactCase.CanRevealPhone() && resumed
        });
    }

    private async Task<Result<DeliveryContactEligibilityDto>> EvaluateCallPolicyAsync(
        TripStop stop, VoiceCallContactParticipants resolved, CancellationToken cancellationToken)
    {
        if (stop.Status != TripStopStatus.PickedUp)
        {
            return Deny("contact.stop_not_active", stop, null, null);
        }

        if (stop.Trip is null || stop.Trip.Status != TripStatus.InProgress)
        {
            return Deny("contact.trip_not_in_progress", stop, null, null);
        }

        if (resolved.ActiveDriverAssignmentId is null || resolved.DriverUserId is null)
        {
            return Deny("contact.no_active_assignment", stop, null, null);
        }

        // G04: real operational eligibility — profile deleted/rejected/unapproved,
        // expired license/national-ID/vehicle registration all deny contact, while
        // IsAvailable=false mid-delivery stays irrelevant to an existing stop.
        var driverProfile = await _db.DriverProfiles
            .AsNoTracking()
            .FirstAsync(dp => dp.Id == resolved.DriverProfileId!.Value, cancellationToken);
        var operational = await DriverOperationalEligibility.CheckEligibilityAsync(
            _db, users: null, driverProfile, _clock.UtcNow, isArabic: false, requireApproved: true, cancellationToken);
        if (operational.IsFailure)
        {
            return Deny("contact.driver_ineligible", stop, null, null);
        }

        if (resolved.CustomerUserId is null || resolved.CustomerUserId.Value == Guid.Empty)
        {
            return Deny("contact.unresolved_customer", stop, null, null);
        }

        var activeAccounts = await _db.GetActiveAccountUserIdsAsync([resolved.DriverUserId!.Value, resolved.CustomerUserId.Value], cancellationToken);

        if (!activeAccounts.Contains(resolved.DriverUserId.Value) || !activeAccounts.Contains(resolved.CustomerUserId.Value))
        {
            return Deny("contact.account_inactive", stop, null, null);
        }

        // Fresh, accurate fix owned by the assigned driver on THIS trip. The query is
        // scoped by both driver and trip: another driver's or another trip's points
        // can never satisfy eligibility, and no point is fabricated when absent.
        // NULL-accuracy points (legacy pings) are skipped so they can never displace
        // the latest quality fix.
        var latestFix = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => p.DriverProfileId == resolved.DriverProfileId!.Value && p.TripId == stop.TripId)
            .Where(p => p.AccuracyMeters != null)
            .OrderByDescending(p => p.CapturedAtUtc ?? p.RecordedAtUtc)
            .Select(p => new
            {
                p.Latitude,
                p.Longitude,
                p.AccuracyMeters,
                CapturedAtUtc = p.CapturedAtUtc ?? p.RecordedAtUtc,
                p.ReceivedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestFix is null)
        {
            return Deny("contact.location_missing", stop, null, null);
        }

        var nowUtc = _clock.UtcNow;
        var receivedAt = latestFix.ReceivedAtUtc ?? latestFix.CapturedAtUtc;
        if (latestFix.CapturedAtUtc > nowUtc.Add(TimestampSkewTolerance) || receivedAt > nowUtc.Add(TimestampSkewTolerance))
        {
            return Deny("contact.location_invalid", stop, null, null);
        }

        var ageSeconds = (nowUtc - latestFix.CapturedAtUtc).TotalSeconds;
        if (ageSeconds < 0)
        {
            ageSeconds = 0;
        }

        if (ageSeconds > LocationFreshnessSeconds)
        {
            return Deny("contact.location_stale", stop, latestFix.AccuracyMeters, ageSeconds);
        }

        if (!latestFix.AccuracyMeters.HasValue)
        {
            return Deny("contact.location_accuracy_missing", stop, null, ageSeconds);
        }

        if (latestFix.AccuracyMeters.Value > LocationMaxAccuracyMeters)
        {
            return Deny("contact.location_accuracy_low", stop, latestFix.AccuracyMeters, ageSeconds);
        }

        if (!stop.Latitude.HasValue || !stop.Longitude.HasValue)
        {
            return Deny("contact.stop_location_missing", stop, latestFix.AccuracyMeters, ageSeconds);
        }

        var distance = VoiceCallPolicyMath.HaversineMeters(
            (double)latestFix.Latitude,
            (double)latestFix.Longitude,
            (double)stop.Latitude.Value,
            (double)stop.Longitude.Value);

        if (distance is null)
        {
            return Deny("contact.location_invalid", stop, latestFix.AccuracyMeters, ageSeconds);
        }

        // TEMPORARY (2026-10-10): 3km restriction disabled at user request for call testing.
        // To restore, uncomment this block and the matching radius check in
        // ResumeDeliveryHoldV2CommandHandler.Handle, then restore the radius test expectations.
        // if (!VoiceCallPolicyMath.IsWithinContactRadius(distance.Value, ContactRadiusMeters))
        // {
        //     return Deny("contact.out_of_contact_radius", stop, latestFix.AccuracyMeters, ageSeconds, distance);
        // }

        return Result.Success(new DeliveryContactEligibilityDto(
            CanInitiate: true,
            ReasonCode: "contact.eligible",
            DistanceMeters: Math.Round(distance.Value, 1),
            LocationAgeSeconds: Math.Round(ageSeconds, 1),
            // EvaluateAsync adds rights from the persisted contact workflow.
            CanHold: false,
            CanRetry: false,
            CanRevealPhone: false,
            ContactCaseId: null));
    }

    public async Task<Result<VoiceCallContactParticipants>> ResolveParticipantsAsync(Guid tripStopId, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == tripStopId, cancellationToken);

        if (stop is null)
        {
            return Result.Failure<VoiceCallContactParticipants>(VoiceCallErrors.TripStopNotFound());
        }

        Guid? customerUserId = null;
        if (stop.SubscriptionDayId.HasValue)
        {
            customerUserId = await _db.SubscriptionDays
                .AsNoTracking()
                .Where(sd => sd.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, sd => sd.SubscriptionId, s => s.Id, (sd, s) => (Guid?)s.CustomerUserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // G02: newest ACTIVE assignment only — inactive/reassigned never resolve as
        // the driver side, matching every other driver-trip path in the system.
        var activeAssignment = stop.Trip?.DriverAssignments
            .Where(a => a.Status == "Active")
            .OrderByDescending(a => a.AssignedAtUtc)
            .FirstOrDefault();

        Guid? driverProfileId = null;
        Guid? driverUserId = null;
        if (activeAssignment is not null)
        {
            driverProfileId = activeAssignment.DriverProfileId;
            driverUserId = await _db.DriverProfiles
                .AsNoTracking()
                .Where(dp => dp.Id == activeAssignment.DriverProfileId)
                .Select(dp => (Guid?)dp.UserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Result.Success(new VoiceCallContactParticipants(
            stop.Id,
            activeAssignment?.Id,
            driverProfileId,
            driverUserId,
            customerUserId));
    }

    private static Result<DeliveryContactEligibilityDto> Deny(
        string reasonCode,
        TripStop stop,
        double? accuracyMeters,
        double? ageSeconds,
        double? distanceMeters = null) =>
        Result.Success(new DeliveryContactEligibilityDto(
            CanInitiate: false,
            ReasonCode: reasonCode,
            DistanceMeters: distanceMeters,
            LocationAgeSeconds: ageSeconds,
            CanHold: false,
            CanRetry: false,
            CanRevealPhone: false,
            ContactCaseId: null));
}
