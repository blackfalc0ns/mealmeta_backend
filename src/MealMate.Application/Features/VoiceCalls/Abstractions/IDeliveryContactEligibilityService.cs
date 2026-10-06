using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Central server-side contact-eligibility policy (spec 4.1): PickedUp stop on an
/// InProgress trip with an active driver assignment, active accounts, and a fresh,
/// accurate, driver-owned location within the contact radius. Used by the driver UI
/// surface AND re-checked inside call creation.
/// </summary>
public interface IDeliveryContactEligibilityService
{
    Task<Result<DeliveryContactEligibilityDto>> EvaluateAsync(Guid tripStopId, Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Single authoritative participant resolution for a stop (G02): the driver side
    /// is the newest ACTIVE assignment only; the customer side is the subscription
    /// ownership chain. Call creation and eligibility MUST share this — never a
    /// divergent newest-of-any-status selection.
    /// </summary>
    Task<Result<VoiceCallContactParticipants>> ResolveParticipantsAsync(Guid tripStopId, CancellationToken cancellationToken);
}

/// <summary>Resolved participants of a delivery stop's contact context. Nulls mean "no such side".</summary>
public sealed record VoiceCallContactParticipants(
    Guid TripStopId,
    Guid? ActiveDriverAssignmentId,
    Guid? DriverProfileId,
    Guid? DriverUserId,
    Guid? CustomerUserId);
