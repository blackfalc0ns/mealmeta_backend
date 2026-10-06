using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Stop-scoped participant authorization for voice-call surfaces. Out-of-scope
/// access must fail with NotFound so resource IDs never act as capabilities.
/// </summary>
public interface IVoiceCallAuthorizationService
{
    /// <summary>
    /// Validates that the user is a current participant of the delivery stop:
    /// the driver holding the active assignment on the stop's trip, or the
    /// customer owning the linked subscription day. Any other authenticated
    /// user receives NotFound, regardless of knowing the identifier.
    /// </summary>
    Task<Result> AuthorizeTripStopParticipantAsync(Guid tripStopId, Guid userId, CancellationToken cancellationToken);
}
