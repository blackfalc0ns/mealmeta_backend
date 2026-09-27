using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Unified device token registration for the shared Staff mobile app
/// (used by both Drivers and Delivery Managers who share the same Firebase project).
///
/// The backend automatically routes the token to the correct storage table
/// based on the authenticated user's JWT role:
///   - Driver          → DriverDeviceTokens
///   - DeliveryManager → RestaurantDeviceTokens
///
/// This eliminates the need for the mobile app to choose a different endpoint
/// per role — a single unified endpoint handles both roles.
/// </summary>
[Route("api/v1/staff")]
[Authorize]
public sealed class StaffDeviceTokenController : ApiControllerBase
{
    /// <summary>
    /// Registers or updates the FCM push device token for a staff member.
    /// Works for both Driver and Delivery Manager roles on the same shared mobile app.
    ///
    /// The system reads the authenticated user's role from the JWT and stores the token
    /// in the appropriate table automatically:
    ///   - Driver → DriverDeviceTokens
    ///   - DeliveryManager → RestaurantDeviceTokens
    ///
    /// Mobile apps should call this endpoint after:
    ///   1. Obtaining the FCM token from Firebase SDK
    ///   2. Logging in (JWT available)
    ///   3. After each app startup if the token may have changed
    ///
    /// The endpoint is idempotent — calling it multiple times with the same token is safe.
    /// </summary>
    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] StaffDeviceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new RegisterStaffDeviceTokenCommand(
            Token: request.Token,
            Platform: request.Platform,
            DeviceId: request.DeviceId,
            Role: request.Role), cancellationToken);

        return HandleResult(result);
    }
}

/// <summary>
/// Request body for registering a staff device token.
/// </summary>
public sealed record StaffDeviceTokenRequest(
    /// <summary>FCM push notification token from Firebase SDK.</summary>
    string Token,

    /// <summary>Device platform. Defaults to Android.</summary>
    string Platform = "Android",

    /// <summary>Optional unique device identifier for deduplication across token refreshes.</summary>
    string? DeviceId = null,

    /// <summary>
    /// Optional explicit role override. Used only if the user is not yet authenticated
    /// (no JWT). Accepted values: "Driver", "DeliveryManager".
    /// When the user is authenticated, the role is read from the JWT and this field is ignored.
    /// </summary>
    string? Role = null);
