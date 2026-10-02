using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Screen 09.05: server-stored driver app preferences (language, navigation,
/// alerts, security, offline-sync auto-sync). The driver is resolved from the
/// JWT; no client-supplied driver ID is accepted.
/// </summary>
[Route("api/v1/driver/settings")]
[Authorize(Roles = AppRoles.Driver)]
public sealed class DriverSettingsController : ApiControllerBase
{
    /// <summary>Reads the signed-in driver's stored preferences; unset fields are null.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DriverAppSettingsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDriverAppSettingsQuery(), cancellationToken));

    /// <summary>Partial update of the signed-in driver's preferences; omitted fields keep their stored value.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(DriverAppSettingsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateDriverAppSettingsRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateDriverAppSettingsCommand(
            request.PreferredLanguage,
            request.PreferredNavigationApp,
            request.AutoLaunchNavigation,
            request.SoundAlertsEnabled,
            request.VibrationEnabled,
            request.BiometricLoginEnabled,
            request.OfflineSync?.AutoSyncOnReconnect), cancellationToken));
}

public sealed record UpdateDriverAppSettingsRequest(
    string? PreferredLanguage,
    string? PreferredNavigationApp,
    bool? AutoLaunchNavigation,
    bool? SoundAlertsEnabled,
    bool? VibrationEnabled,
    bool? BiometricLoginEnabled,
    UpdateDriverAppSettingsOfflineSyncRequest? OfflineSync);

public sealed record UpdateDriverAppSettingsOfflineSyncRequest(
    bool? AutoSyncOnReconnect);
