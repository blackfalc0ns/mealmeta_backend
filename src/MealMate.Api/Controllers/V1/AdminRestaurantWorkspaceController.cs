using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/restaurants/{restaurantId:guid}/operations")]
[Authorize]
public sealed class AdminRestaurantOperationsController : ApiControllerBase
{
    [HttpGet("capacity")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetCapacityOverview(
        Guid restaurantId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetRestaurantCapacityOverviewQuery(from, to, restaurantId),
            cancellationToken));

    [HttpPut("capacity/base")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateBaseCapacity(
        Guid restaurantId,
        [FromBody] UpdateBaseCapacityRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateBaseCapacityCommand(request.NewCapacity, restaurantId),
            cancellationToken));

    [HttpPut("capacity/dates/{date}/limit")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateDateCapacity(
        Guid restaurantId,
        DateOnly date,
        [FromBody] UpdateDateCapacityRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateDateCapacityCommand(date, request.NewCapacity, restaurantId),
            cancellationToken));

    [HttpPut("capacity/dates/{date}")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> SetDateManualBusy(
        Guid restaurantId,
        DateOnly date,
        [FromBody] SetDateManualBusyRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new SetDateManualBusyCommand(date, request.IsManualBusy, request.Reason, restaurantId),
            cancellationToken));

    [HttpGet("working-days")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetWorkingDaysSchedule(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetWorkingDaysScheduleQuery(restaurantId), cancellationToken));

    [HttpPut("working-days")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateWorkingDaysSchedule(
        Guid restaurantId,
        [FromBody] UpdateWorkingDaysScheduleRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateWorkingDaysScheduleCommand(request.Schedule, restaurantId),
            cancellationToken));

    [HttpPost("working-days/exceptions")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> AddWorkingDayException(
        Guid restaurantId,
        [FromBody] AddWorkingDayExceptionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new AddWorkingDayExceptionCommand(request.Date, request.IsWorkingDay, request.Reason, restaurantId),
            cancellationToken));

    [HttpDelete("working-days/exceptions/{date}")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> DeleteWorkingDayException(
        Guid restaurantId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new DeleteWorkingDayExceptionCommand(date, restaurantId), cancellationToken));

    [HttpGet("service-regions")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetServiceRegions(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantServiceRegionsQuery(restaurantId), cancellationToken));

    [HttpGet("service-regions/{regionId:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetServiceRegionDetail(
        Guid restaurantId,
        Guid regionId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetRestaurantServiceRegionDetailQuery(regionId, restaurantId),
            cancellationToken));

    [HttpPut("service-regions")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateServiceRegionSelection(
        Guid restaurantId,
        [FromBody] UpdateServiceRegionSelectionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantServiceRegionSelectionCommand(request.RegionIds, restaurantId),
            cancellationToken));

    [HttpPut("service-regions/{regionId:guid}")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateServiceRegion(
        Guid restaurantId,
        Guid regionId,
        [FromBody] UpdateServiceRegionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateRestaurantServiceRegionCommand(
            regionId,
            request.IsActive,
            request.DeliveryWindowStart,
            request.DeliveryWindowEnd,
            request.PreparationLeadTimeMinutes,
            restaurantId), cancellationToken));
}

[Route("api/v1/admin/restaurants/{restaurantId:guid}/team")]
[Authorize]
public sealed class AdminRestaurantTeamController : ApiControllerBase
{
    [HttpGet("delivery-managers")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> ListDeliveryManagers(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListDeliveryManagersQuery(restaurantId), cancellationToken));

    [HttpPost("delivery-managers")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> CreateDeliveryManager(
        Guid restaurantId,
        [FromBody] CreateRestaurantDeliveryManagerRequest body,
        CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(new RegisterDeliveryManagerCommand(
            body.FullName,
            body.Email,
            body.Phone,
            body.Password,
            restaurantId,
            body.RegionId), cancellationToken), $"/api/v1/admin/restaurants/{restaurantId}/team/delivery-managers");

    [HttpPost("delivery-managers/{id:guid}/toggle-status")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> ToggleDeliveryManagerStatus(
        Guid restaurantId,
        Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ToggleDeliveryManagerStatusCommand(id), cancellationToken));

    [HttpPost("delivery-managers/{id:guid}/resend-invite")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> ResendDeliveryManagerInvite(
        Guid restaurantId,
        Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ResendDeliveryManagerInviteCommand(id), cancellationToken));

    [HttpGet("drivers")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> ListDrivers(
        Guid restaurantId,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListRestaurantDriversQuery(restaurantId, isActive), cancellationToken));

    [HttpGet("staff")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> ListStaff(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListRestaurantStaffQuery(restaurantId), cancellationToken));

    [HttpGet("staff/invites")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> ListStaffInvites(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListStaffInvitationsQuery(restaurantId), cancellationToken));
}

[Route("api/v1/admin/restaurants/{restaurantId:guid}/settings")]
[Authorize]
public sealed class AdminRestaurantSettingsController : ApiControllerBase
{
    [HttpGet("commercial")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetCommercialSettings(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantCommercialSettingsQuery(restaurantId), cancellationToken));

    [HttpPut("commercial")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateCommercialSettings(
        Guid restaurantId,
        [FromBody] UpdateRestaurantCommercialSettingsRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantCommercialSettingsCommand(restaurantId, request.CommissionPercent),
            cancellationToken));

    [HttpGet("operational-preferences")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetOperationalPreferences(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetRestaurantOperationalPreferencesQuery(restaurantId),
            cancellationToken));

    [HttpPut("operational-preferences")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateOperationalPreferences(
        Guid restaurantId,
        [FromBody] UpdateRestaurantOperationalPreferencesRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantOperationalPreferencesCommand(
                restaurantId,
                request.AutoConfirmOrders,
                request.ManualBusyOverride),
            cancellationToken));

    [HttpGet("notifications")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetNotificationPreferences(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetRestaurantNotificationPreferencesQuery(restaurantId),
            cancellationToken));

    [HttpPut("notifications")]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> UpdateNotificationPreferences(
        Guid restaurantId,
        [FromBody] UpdateRestaurantNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantNotificationPreferencesCommand(restaurantId, request.Preferences),
            cancellationToken));
}

[Route("api/v1/admin/restaurants/{restaurantId:guid}")]
[Authorize]
public sealed class AdminRestaurantAuditController : ApiControllerBase
{
    [HttpGet("audit-log")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> GetAuditLog(
        Guid restaurantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetRestaurantAuditLogQuery(restaurantId, page, pageSize),
            cancellationToken));
}

public sealed record UpdateRestaurantCommercialSettingsRequest(decimal CommissionPercent);

public sealed record UpdateRestaurantOperationalPreferencesRequest(
    bool AutoConfirmOrders,
    bool ManualBusyOverride);

public sealed record UpdateRestaurantNotificationPreferencesRequest(
    IReadOnlyList<RestaurantNotificationPreferenceDto> Preferences);
