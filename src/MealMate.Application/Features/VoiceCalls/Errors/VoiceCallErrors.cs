using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.VoiceCalls.Errors;

/// <summary>
/// Domain and application error codes and messages for the Voice Calling module.
/// </summary>
public static class VoiceCallErrors
{
    public static Error NotFound(string? message = null) =>
        Error.NotFound("calls.not_found", message ?? "Call session not found.");

    public static Error Unauthorized(string? message = null) =>
        Error.Unauthorized("calls.unauthorized", message ?? "Authentication required to perform this action.");

    public static Error Forbidden(string? message = null) =>
        Error.Forbidden("calls.forbidden", message ?? "You are not authorized to participate in or modify this call.");

    public static Error TripStopNotFound() =>
        Error.NotFound("calls.trip_stop_not_found", "The specified delivery trip stop was not found.");

    public static Error DeliveryNotActive(TripStopStatus status) =>
        Error.Conflict("calls.delivery_not_active", $"Voice calls are only allowed during active delivery. Current stop status is '{status}'.");

    public static Error TripCancelled() =>
        Error.Conflict("calls.trip_cancelled", "The delivery trip has been cancelled.");

    public static Error UnassignedDelivery() =>
        Error.Conflict("calls.unassigned_delivery", "No driver is currently assigned to this delivery stop.");

    public static Error UnresolvedCustomer() =>
        Error.Conflict("calls.unresolved_customer", "Customer for this delivery stop could not be identified.");

    public static Error CallerBusy() =>
        Error.Conflict("calls.caller_busy", "You already have an active call session in progress.");

    public static Error RecipientBusy() =>
        Error.Conflict("calls.recipient_busy", "The recipient is currently busy on another call.");

    public static Error SelfCallNotAllowed() =>
        Error.Validation("calls.self_call_not_allowed", "Caller and recipient cannot be the same user.");

    public static Error InvalidState(string message) =>
        Error.Conflict("calls.invalid_state", message);

    public static Error CallAlreadyEnded() =>
        Error.Conflict("calls.already_ended", "The call session has already ended.");

    public static Error CallAlreadyCancelled() =>
        Error.Conflict("calls.call_cancelled", "The call was cancelled by the caller.");

    public static Error CallAlreadyAnswered() =>
        Error.Conflict("calls.call_already_answered", "The call has already been answered by the recipient.");

    public static Error ContactNotEligible(string reasonCode) =>
        // The reason code IS the error code so ProblemDetails/clients can branch on it;
        // a generic umbrella code would swallow the specific deny reason in localization.
        Error.Conflict(reasonCode, $"Voice calls are not available for this delivery stop ({reasonCode}).");

    public static Error V2DeviceControlRequired() =>
        Error.Conflict("calls.v2_device_control_required",
            "This call uses the v2 protocol and must be controlled through the v2 API with a device session.");

    public static Error ConcurrencyConflict() =>
        Error.Conflict("calls.concurrency_conflict", "The call state was modified concurrently. Please refresh and retry.");
}
