using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using ApplicationOutboxMessage = MealMate.Domain.Common.OutboxMessage;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.V2.Commands;

/// <summary>
/// F14 delivery-contact workflow (T14/T15): qualified no-answer attempts recorded once
/// per terminal call, explicit Hold with required notes, explicit resume with fresh
/// in-radius location, and the conditional phone reveal with grant/audit. Manual or
/// local counters can never create qualifying evidence — every increment derives from
/// a persisted terminal call + a real receiver ringing receipt.
/// </summary>
public static class DeliveryContactWorkflowErrors
{
    public static Error CaseNotFound() =>
        Error.Conflict("contact.case_not_found", "No open contact case exists for this stop.");

    public static Error HoldNotesInvalid() =>
        Error.Validation("contact.hold_notes_invalid", "Hold notes are required (5-500 characters).");

    public static Error HoldAlreadyOpen() =>
        Error.Conflict("contact.hold_already_open", "An open hold already exists for this stop.");

    public static Error HoldRequired() =>
        Error.Conflict("contact.hold_required", "A documented hold is required before this action.");

    public static Error NotQualified() =>
        Error.Conflict("contact.not_qualified", "The qualifying attempt chain is not complete.");

    public static Error CooldownActive() =>
        Error.Conflict("contact.cooldown_active", "The retry cooldown has not elapsed since the last attempt.");

    public static Error ResumeLocationRequired() =>
        Error.Conflict("contact.resume_location_required",
            "A fresh, accurate location within the contact radius is required to resume.");

    public static Error ReasonInvalid() =>
        Error.Validation("contact.reason_invalid", "A reason (5-500 characters) is required.");

    public static Error PrivacyAcknowledgementRequired() =>
        Error.Validation("contact.privacy_acknowledgement_required",
            "The privacy warning must be acknowledged before revealing the phone number.");

    public static Error GrantExpired() =>
        Error.Conflict("contact.grant_expired", "The reveal grant has expired; start a new reveal request.");

    public static Error PhoneUnavailable() =>
        Error.Conflict("calls.phone_unavailable", "No confirmed phone number exists for this customer.");

    public static Error NotTheAssignedDriver() =>
        Error.Forbidden("contact.not_assigned_driver", "Only the currently assigned driver may perform this action.");
}

/// <summary>Shared resolution of the stop's open contact case (creates cycle 1 lazily).</summary>
public sealed class DeliveryContactCaseService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IDeliveryContactEligibilityService _eligibility;

    public DeliveryContactCaseService(IAppDbContext db, IDateTimeProvider clock, IDeliveryContactEligibilityService eligibility)
    {
        _db = db;
        _clock = clock;
        _eligibility = eligibility;
    }

    public async Task<Result<DeliveryContactCase>> GetOrOpenCaseAsync(Guid tripStopId, CancellationToken cancellationToken)
    {
        var participants = await _eligibility.ResolveParticipantsAsync(tripStopId, cancellationToken);
        if (participants.IsFailure || participants.Value.ActiveDriverAssignmentId is null)
        {
            return Result.Failure<DeliveryContactCase>(DeliveryContactWorkflowErrors.CaseNotFound());
        }

        var assignmentId = participants.Value.ActiveDriverAssignmentId.Value;

        var openCase = await _db.DeliveryContactCases
            .FirstOrDefaultAsync(c => c.TripStopId == tripStopId
                                      && c.ActiveDriverAssignmentId == assignmentId
                                      && c.State == DeliveryContactCase.StateOpen, cancellationToken);

        if (openCase is not null)
        {
            return Result.Success(openCase);
        }

        var lastCycle = await _db.DeliveryContactCases
            .Where(c => c.TripStopId == tripStopId && c.ActiveDriverAssignmentId == assignmentId)
            .OrderByDescending(c => c.ContactCycleId)
            .Select(c => (int?)c.ContactCycleId)
            .FirstOrDefaultAsync(cancellationToken);

        var contactCase = new DeliveryContactCase(
            tripStopId, assignmentId, (lastCycle ?? 0) + 1, _clock.UtcNow);
        _db.DeliveryContactCases.Add(contactCase);
        return Result.Success(contactCase);
    }

    /// <summary>
    /// Shared participant resolution with an actor guard: only the currently assigned
    /// driver (or the stop's customer for reads) passes; anyone else gets NotFound.
    /// </summary>
    public async Task<Result<VoiceCallContactParticipants>> ResolveForActorAsync(
        Guid tripStopId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var participants = await _eligibility.ResolveParticipantsAsync(tripStopId, cancellationToken);
        if (participants.IsFailure)
        {
            return participants;
        }

        var isDriver = participants.Value.DriverUserId == actorUserId;
        var isCustomer = participants.Value.CustomerUserId == actorUserId;
        if (!isDriver && !isCustomer)
        {
            return Result.Failure<VoiceCallContactParticipants>(
                MealMate.Application.Features.VoiceCalls.Errors.VoiceCallErrors.TripStopNotFound());
        }

        return participants;
    }

    /// <summary>
    /// Records the attempt for a terminal missed call (called from the durable outbox
    /// consumer, so it survives crashes; the unique CallId index makes it idempotent).
    /// Qualification: driver→customer direction, NoAnswer, receiver ringing receipt,
    /// observed ring ≥ 20s (proposed value pending G-POLICY).
    /// </summary>
    public async Task RecordAttemptForMissedCallAsync(CallSession call, CancellationToken cancellationToken)
    {
        var alreadyRecorded = await _db.DeliveryContactAttempts
            .AnyAsync(a => a.CallId == call.Id, cancellationToken);
        if (alreadyRecorded)
        {
            return;
        }

        var participants = await _eligibility.ResolveParticipantsAsync(call.TripStopId, cancellationToken);
        if (participants.IsFailure || participants.Value.ActiveDriverAssignmentId is null)
        {
            return; // delivery context gone (delivered/cancelled) — nothing to record
        }

        var assignmentId = participants.Value.ActiveDriverAssignmentId.Value;
        var contactCase = await GetOrOpenCaseAsync(call.TripStopId, cancellationToken);
        if (contactCase.IsFailure)
        {
            return;
        }

        var directionDriverToCustomer = call.CallerType == CallParticipantType.Driver
                                        && call.CallerUserId == participants.Value.DriverUserId;
        var hasRingingReceipt = await _db.VoiceCallDeviceReceipts
            .AnyAsync(r => r.CallId == call.Id && r.RingingAckAtUtc != null, cancellationToken);
        var observedRingSeconds = call.RingingAtUtc.HasValue && call.EndedAtUtc.HasValue
            ? (call.EndedAtUtc.Value - call.RingingAtUtc.Value).TotalSeconds
            : 0;
        var reasonNoAnswer = call.EndReason == CallEndReason.NoAnswer;

        var qualifies = directionDriverToCustomer && reasonNoAnswer && hasRingingReceipt
                        && observedRingSeconds >= DeliveryContactThresholds.QualifyingObservedRingSeconds;

        var ordinal = await _db.DeliveryContactAttempts
            .CountAsync(a => a.ContactCaseId == contactCase.Value.Id, cancellationToken) + 1;

        var attempt = new DeliveryContactAttempt(
            call.Id,
            contactCase.Value.Id,
            call.CallerUserId,
            call.CallerType,
            qualifies ? DeliveryContactAttempt.OutcomeQualifiedNoAnswer : DeliveryContactAttempt.OutcomeTechnicalNoAnswer,
            qualifies,
            ordinal,
            call.StartedAtUtc,
            call.EndedAtUtc ?? _clock.UtcNow);

        _db.DeliveryContactAttempts.Add(attempt);
        if (qualifies)
        {
            contactCase.Value.RecordQualifiedMissedAttempt(call.Id, _clock.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

public static class DeliveryContactThresholds
{
    /// <summary>Proposed: observed ring below this never qualifies. Pending G-POLICY.</summary>
    public const double QualifyingObservedRingSeconds = 20d;

    /// <summary>Proposed: cooldown after a qualified attempt. Pending G-POLICY.</summary>
    public const int RetryCooldownSeconds = 120;

    /// <summary>Proposed: reveal grant validity. Pending G-POLICY.</summary>
    public const double RevealGrantValiditySeconds = 60d;
}

// ── Get case ─────────────────────────────────────────────────────────────────

public sealed record GetDeliveryContactCaseV2Query(Guid TripStopId, Guid ActorUserId) : IQuery<DeliveryContactCaseDto>;

public sealed record DeliveryContactCaseDto(
    Guid ContactCaseId,
    string State,
    int QualifiedMissedCount,
    Guid? HoldId,
    bool HoldActive,
    bool HoldResumed,
    bool CanHold,
    bool CanRevealPhone,
    Guid? LastCallId,
    DateTime CreatedAtUtc);

public sealed class GetDeliveryContactCaseV2QueryHandler : IQueryHandler<GetDeliveryContactCaseV2Query, DeliveryContactCaseDto>
{
    private readonly DeliveryContactCaseService _caseService;
    private readonly IAppDbContext _db;

    public GetDeliveryContactCaseV2QueryHandler(DeliveryContactCaseService caseService, IAppDbContext db)
    {
        _caseService = caseService;
        _db = db;
    }

    public async Task<Result<DeliveryContactCaseDto>> Handle(GetDeliveryContactCaseV2Query request, CancellationToken cancellationToken)
    {
        var contactCase = await _caseService.GetOrOpenCaseAsync(request.TripStopId, cancellationToken);
        if (contactCase.IsFailure)
        {
            return Result.Failure<DeliveryContactCaseDto>(contactCase.Error);
        }

        var contactCaseValue = contactCase.Value;
        var hold = contactCaseValue.HoldId.HasValue
            ? await _db.DeliveryHolds.FirstOrDefaultAsync(h => h.Id == contactCaseValue.HoldId.Value, cancellationToken)
            : null;

        return Result.Success(new DeliveryContactCaseDto(
            contactCaseValue.Id,
            contactCaseValue.State,
            contactCaseValue.QualifiedMissedCount,
            contactCaseValue.HoldId,
            hold is not null && hold.ResumedAtUtc is null,
            hold?.ResumedAtUtc is not null,
            CanHold: contactCaseValue.QualifiedMissedCount >= 1 && contactCaseValue.HoldId is null && contactCaseValue.State == DeliveryContactCase.StateOpen,
            CanRevealPhone: contactCaseValue.CanRevealPhone() && hold?.ResumedAtUtc is not null,
            contactCaseValue.LastCallId,
            contactCaseValue.CreatedAtUtc));
    }
}

// ── Hold ─────────────────────────────────────────────────────────────────────

public sealed record CreateDeliveryHoldV2Command(
    Guid TripStopId,
    Guid ActorUserId,
    string Notes,
    Guid? FirstAttemptCallId) : ICommand<DeliveryContactCaseDto>, ISelfPersistingCommand;

public sealed class CreateDeliveryHoldV2CommandHandler : ICommandHandler<CreateDeliveryHoldV2Command, DeliveryContactCaseDto>
{
    private readonly DeliveryContactCaseService _caseService;
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CreateDeliveryHoldV2CommandHandler(DeliveryContactCaseService caseService, IAppDbContext db, IDateTimeProvider clock)
    {
        _caseService = caseService;
        _db = db;
        _clock = clock;
    }

    public async Task<Result<DeliveryContactCaseDto>> Handle(CreateDeliveryHoldV2Command request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Notes) || request.Notes.Length is < 5 or > 500)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.HoldNotesInvalid());
        }

        var participants = await _caseService.ResolveForActorAsync(request.TripStopId, request.ActorUserId, cancellationToken);
        if (participants.IsFailure)
        {
            return Result.Failure<DeliveryContactCaseDto>(participants.Error);
        }

        // Hold is the assigned driver's action; the customer side cannot create one.
        if (participants.Value.DriverUserId != request.ActorUserId)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.NotTheAssignedDriver());
        }

        var contactCase = await _caseService.GetOrOpenCaseAsync(request.TripStopId, cancellationToken);
        if (contactCase.IsFailure)
        {
            return Result.Failure<DeliveryContactCaseDto>(contactCase.Error);
        }

        var contactCaseValue = contactCase.Value;

        if (contactCaseValue.QualifiedMissedCount < 1)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.NotQualified());
        }

        if (contactCaseValue.HoldId.HasValue)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.HoldAlreadyOpen());
        }

        var firstAttempt = await _db.DeliveryContactAttempts
            .Where(a => a.ContactCaseId == contactCaseValue.Id && a.QualifiesAsNoAnswer)
            .OrderBy(a => a.Ordinal)
            .Select(a => (Guid?)a.CallId)
            .FirstOrDefaultAsync(cancellationToken);

        var stop = await _db.TripStops.AsNoTracking().FirstAsync(st => st.Id == request.TripStopId, cancellationToken);

        var hold = new DeliveryHold
        {
            TripId = stop.TripId,
            SubscriptionDayId = stop.SubscriptionDayId,
            TripStopId = request.TripStopId,
            ContactCaseId = contactCaseValue.Id,
            ActorUserId = request.ActorUserId,
            Reason = "CustomerNoAnswer",
            Notes = request.Notes,
            FirstAttemptCallId = firstAttempt ?? request.FirstAttemptCallId,
            Status = "Active",
            HeldAtUtc = _clock.UtcNow
        };
        _db.DeliveryHolds.Add(hold);
        contactCaseValue.AttachHold(hold.Id, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildCaseDtoAsync(contactCaseValue.Id, cancellationToken));
    }

    private async Task<DeliveryContactCaseDto> BuildCaseDtoAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var contactCaseValue = await _db.DeliveryContactCases.FirstAsync(c => c.Id == caseId, cancellationToken);
        var hold = contactCaseValue.HoldId.HasValue
            ? await _db.DeliveryHolds.FirstOrDefaultAsync(h => h.Id == contactCaseValue.HoldId.Value, cancellationToken)
            : null;
        return new DeliveryContactCaseDto(
            contactCaseValue.Id,
            contactCaseValue.State,
            contactCaseValue.QualifiedMissedCount,
            contactCaseValue.HoldId,
            hold is not null && hold.ResumedAtUtc is null,
            hold?.ResumedAtUtc is not null,
            CanHold: contactCaseValue.QualifiedMissedCount >= 1 && contactCaseValue.HoldId is null && contactCaseValue.State == DeliveryContactCase.StateOpen,
            CanRevealPhone: contactCaseValue.CanRevealPhone() && hold?.ResumedAtUtc is not null,
            contactCaseValue.LastCallId,
            contactCaseValue.CreatedAtUtc);
    }
}

// ── Resume ───────────────────────────────────────────────────────────────────

public sealed record ResumeDeliveryHoldV2Command(
    Guid TripStopId,
    Guid ActorUserId) : ICommand<DeliveryContactCaseDto>, ISelfPersistingCommand;

public sealed class ResumeDeliveryHoldV2CommandHandler : ICommandHandler<ResumeDeliveryHoldV2Command, DeliveryContactCaseDto>
{
    private readonly DeliveryContactCaseService _caseService;
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly Microsoft.Extensions.Options.IOptions<MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions> _options;

    public ResumeDeliveryHoldV2CommandHandler(
        DeliveryContactCaseService caseService,
        IAppDbContext db,
        IDateTimeProvider clock,
        Microsoft.Extensions.Options.IOptions<MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions>? options = null)
    {
        _caseService = caseService;
        _db = db;
        _clock = clock;
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions());
    }

    public async Task<Result<DeliveryContactCaseDto>> Handle(ResumeDeliveryHoldV2Command request, CancellationToken cancellationToken)
    {
        var participants = await _caseService.ResolveForActorAsync(request.TripStopId, request.ActorUserId, cancellationToken);
        if (participants.IsFailure)
        {
            return Result.Failure<DeliveryContactCaseDto>(participants.Error);
        }

        var contactCase = await _caseService.GetOrOpenCaseAsync(request.TripStopId, cancellationToken);
        if (contactCase.IsFailure)
        {
            return Result.Failure<DeliveryContactCaseDto>(contactCase.Error);
        }

        var contactCaseValue = contactCase.Value;
        var hold = contactCaseValue.HoldId.HasValue
            ? await _db.DeliveryHolds.FirstOrDefaultAsync(h => h.Id == contactCaseValue.HoldId.Value, cancellationToken)
            : null;

        if (hold is null || hold.ResumedAtUtc is not null)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.HoldRequired());
        }

        // Cooldown after the last qualified attempt (proposed 120s; test-configurable).
        var lastQualifiedEnd = await _db.DeliveryContactAttempts
            .Where(a => a.ContactCaseId == contactCaseValue.Id && a.QualifiesAsNoAnswer)
            .OrderByDescending(a => a.Ordinal)
            .Select(a => (DateTime?)a.EndedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastQualifiedEnd.HasValue
            && _clock.UtcNow < lastQualifiedEnd.Value.AddSeconds(Math.Max(0, _options.Value.RetryCooldownSeconds)))
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.CooldownActive());
        }

        // Fresh, accurate, driver-owned location within the contact radius (spec 4.2).
        var stop = await _db.TripStops.AsNoTracking().FirstAsync(st => st.Id == request.TripStopId, cancellationToken);
        if (!stop.Latitude.HasValue || !stop.Longitude.HasValue)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.ResumeLocationRequired());
        }

        var latestFix = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(tp => tp.DriverProfileId == participants.Value.DriverProfileId!.Value && tp.TripId == stop.TripId)
            .OrderByDescending(tp => tp.CapturedAtUtc ?? tp.RecordedAtUtc)
            .Select(tp => new { tp.Latitude, tp.Longitude, tp.AccuracyMeters, CapturedAtUtc = tp.CapturedAtUtc ?? tp.RecordedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestFix is null
            || !latestFix.AccuracyMeters.HasValue
            || latestFix.AccuracyMeters.Value > DeliveryContactEligibilityService.LocationMaxAccuracyMeters
            || (_clock.UtcNow - latestFix.CapturedAtUtc).TotalSeconds > DeliveryContactEligibilityService.LocationFreshnessSeconds)
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.ResumeLocationRequired());
        }

        var distance = VoiceCallPolicyMath.HaversineMeters(
            (double)latestFix.Latitude, (double)latestFix.Longitude,
            (double)stop.Latitude.Value, (double)stop.Longitude.Value);

        if (distance is null
            || !VoiceCallPolicyMath.IsWithinContactRadius(distance.Value, DeliveryContactEligibilityService.ContactRadiusMeters))
        {
            return Result.Failure<DeliveryContactCaseDto>(DeliveryContactWorkflowErrors.ResumeLocationRequired());
        }

        // Explicit resume — never inferred from time alone. Stop stays PickedUp.
        hold.ResumedAtUtc = _clock.UtcNow;
        hold.Status = "Released";
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildCaseDtoAsync(contactCaseValue.Id, cancellationToken));
    }

    private async Task<DeliveryContactCaseDto> BuildCaseDtoAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var contactCaseValue = await _db.DeliveryContactCases.FirstAsync(c => c.Id == caseId, cancellationToken);
        var hold = contactCaseValue.HoldId.HasValue
            ? await _db.DeliveryHolds.FirstOrDefaultAsync(h => h.Id == contactCaseValue.HoldId.Value, cancellationToken)
            : null;
        return new DeliveryContactCaseDto(
            contactCaseValue.Id,
            contactCaseValue.State,
            contactCaseValue.QualifiedMissedCount,
            contactCaseValue.HoldId,
            hold is not null && hold.ResumedAtUtc is null,
            hold?.ResumedAtUtc is not null,
            CanHold: contactCaseValue.QualifiedMissedCount >= 1 && contactCaseValue.HoldId is null && contactCaseValue.State == DeliveryContactCase.StateOpen,
            CanRevealPhone: contactCaseValue.CanRevealPhone() && hold?.ResumedAtUtc is not null,
            contactCaseValue.LastCallId,
            contactCaseValue.CreatedAtUtc);
    }
}

// ── Reveal ───────────────────────────────────────────────────────────────────

public sealed record RevealCustomerPhoneV2Command(
    Guid TripStopId,
    Guid ActorUserId,
    Guid ClientRequestId,
    string Reason,
    bool AcknowledgedPrivacyWarning) : ICommand<RevealCustomerPhoneV2Response>, ISelfPersistingCommand;

public sealed record RevealCustomerPhoneV2Response(
    Guid GrantId,
    DateTime ExpiresAtUtc,
    string CustomerPhone,
    Guid ContactCaseId);

public sealed class RevealCustomerPhoneV2CommandHandler : ICommandHandler<RevealCustomerPhoneV2Command, RevealCustomerPhoneV2Response>
{
    private readonly DeliveryContactCaseService _caseService;
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public RevealCustomerPhoneV2CommandHandler(DeliveryContactCaseService caseService, IAppDbContext db, IDateTimeProvider clock)
    {
        _caseService = caseService;
        _db = db;
        _clock = clock;
    }

    public async Task<Result<RevealCustomerPhoneV2Response>> Handle(RevealCustomerPhoneV2Command request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length is < 5 or > 500)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.ReasonInvalid());
        }

        if (!request.AcknowledgedPrivacyWarning)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.PrivacyAcknowledgementRequired());
        }

        var participants = await _caseService.ResolveForActorAsync(request.TripStopId, request.ActorUserId, cancellationToken);
        if (participants.IsFailure)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(participants.Error);
        }

        // Reveal is the assigned driver's action; the customer side can never self-reveal.
        if (participants.Value.DriverUserId != request.ActorUserId)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.NotTheAssignedDriver());
        }

        var contactCase = await _caseService.GetOrOpenCaseAsync(request.TripStopId, cancellationToken);
        if (contactCase.IsFailure)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(contactCase.Error);
        }

        var contactCaseValue = contactCase.Value;

        // Idempotent replay: same (driver, clientRequestId) inside validity → same grant.
        var existingGrant = await _db.CustomerPhoneRevealGrants
            .FirstOrDefaultAsync(g => g.DriverUserId == request.ActorUserId
                                      && g.ClientRequestId == request.ClientRequestId
                                      && g.ContactCaseId == contactCaseValue.Id, cancellationToken);
        if (existingGrant is not null)
        {
            if (existingGrant.IsExpired(_clock.UtcNow))
            {
                return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.GrantExpired());
            }

            return Result.Success(await BuildResponseAsync(existingGrant, cancellationToken));
        }

        // Qualifying chain: two qualified attempts + documented hold + explicit resume.
        if (contactCaseValue.QualifiedMissedCount < 2 || !contactCaseValue.HoldId.HasValue)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.NotQualified());
        }

        var hold = await _db.DeliveryHolds.FirstAsync(h => h.Id == contactCaseValue.HoldId.Value, cancellationToken);
        if (hold.ResumedAtUtc is null)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.HoldRequired());
        }

        var qualifiedCallIds = await _db.DeliveryContactAttempts
            .Where(a => a.ContactCaseId == contactCaseValue.Id && a.QualifiesAsNoAnswer)
            .OrderBy(a => a.Ordinal)
            .Select(a => a.CallId)
            .ToListAsync(cancellationToken);
        if (qualifiedCallIds.Count < 2)
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.NotQualified());
        }

        // Trusted persisted phone: real identity of the linked customer, never a proxy.
        var customerUserId = await _db.TripStops
            .AsNoTracking()
            .Where(st => st.Id == contactCaseValue.TripStopId && st.SubscriptionDayId != null)
            .Join(_db.SubscriptionDays, st => st.SubscriptionDayId, day => day.Id, (st, day) => day.SubscriptionId)
            .Join(_db.Subscriptions, sid => sid, sub => sub.Id, (sid, sub) => (Guid?)sub.CustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        var customerPhone = customerUserId.HasValue
            ? await _db.GetUserPhoneAsync(customerUserId.Value, cancellationToken)
            : null;

        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return Result.Failure<RevealCustomerPhoneV2Response>(DeliveryContactWorkflowErrors.PhoneUnavailable());
        }

        var grant = new CustomerPhoneRevealGrant(
            contactCaseValue.Id,
            request.ActorUserId,
            qualifiedCallIds[0],
            qualifiedCallIds[1],
            request.ClientRequestId,
            request.Reason,
            _clock.UtcNow.AddSeconds(DeliveryContactThresholds.RevealGrantValiditySeconds),
            _clock.UtcNow);
        _db.CustomerPhoneRevealGrants.Add(grant);

        // Audit carries grant/call ids and actor — NEVER the phone number itself.
        _db.AuditLogs.Add(new AuditLog
        {
            Action = "CustomerPhoneRevealed",
            EntityType = "CustomerPhoneRevealGrant",
            EntityId = grant.Id,
            Reason = $"Phone reveal grant for TripStop {request.TripStopId}; attempts [{qualifiedCallIds[0]}, {qualifiedCallIds[1]}]; hold {hold.Id}",
            ActorUserId = request.ActorUserId,
            OccurredAtUtc = _clock.UtcNow
        });

        // Admin notification outbox — metadata only, no phone.
        _db.OutboxMessages.Add(new ApplicationOutboxMessage
        {
            EventType = "voice-call.contact.phone-revealed",
            AggregateType = "CustomerPhoneRevealGrant",
            AggregateId = grant.Id.ToString(),
            DeduplicationKey = $"voice-call:reveal:{grant.Id}",
            EventVersion = 2,
            PayloadJson = JsonSerializer.Serialize(new
            {
                grantId = grant.Id,
                tripStopId = request.TripStopId,
                driverUserId = request.ActorUserId,
                contactCaseId = contactCaseValue.Id,
                occurredAtUtc = _clock.UtcNow
            }),
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildResponseAsync(grant, cancellationToken));
    }

    private async Task<RevealCustomerPhoneV2Response> BuildResponseAsync(CustomerPhoneRevealGrant grant, CancellationToken cancellationToken)
    {
        var caseEntity = await _db.DeliveryContactCases.AsNoTracking()
            .FirstAsync(c => c.Id == grant.ContactCaseId, cancellationToken);
        var customerUserId = await _db.TripStops
            .AsNoTracking()
            .Where(st => st.Id == caseEntity.TripStopId && st.SubscriptionDayId != null)
            .Join(_db.SubscriptionDays, st => st.SubscriptionDayId, day => day.Id, (st, day) => day.SubscriptionId)
            .Join(_db.Subscriptions, sid => sid, sub => sub.Id, (sid, sub) => (Guid?)sub.CustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        var customerPhone = customerUserId.HasValue
            ? await _db.GetUserPhoneAsync(customerUserId.Value, cancellationToken)
            : null;

        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return new RevealCustomerPhoneV2Response(grant.Id, grant.ExpiresAtUtc, string.Empty, grant.ContactCaseId);
        }

        return new RevealCustomerPhoneV2Response(grant.Id, grant.ExpiresAtUtc, customerPhone, grant.ContactCaseId);
    }
}
