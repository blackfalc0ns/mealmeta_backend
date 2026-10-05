using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver")]
[Authorize]
public sealed class DriverHomeController : ApiControllerBase
{
    private readonly IHubContext<DispatcherHub> _dispatcherHub;
    private readonly IHubContext<DriverHub> _driverHub;

    public DriverHomeController(
        IHubContext<DispatcherHub> dispatcherHub,
        IHubContext<DriverHub> driverHub)
    {
        _dispatcherHub = dispatcherHub;
        _driverHub = driverHub;
    }

    /// <summary>
    /// Returns full data model for the Driver Home screen (Screen 05.02 / 05.01).
    /// Includes current status, next stop, daily target progress (8/5), current delivery task (BX-458722),
    /// today's summary counters, daily performance, and unread notifications count.
    /// </summary>
    [HttpGet("home")]
    public async Task<IActionResult> GetHomeScreen(
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverHomeScreenQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns live route map and trip stops carousel data for Screen 03.01 / 05.04.
    /// Includes active/focused stop (BX-458622), horizontal carousel stops (1/3, 2/3, 3/3),
    /// driver coordinates, and route polyline waypoints.
    /// </summary>
    [HttpGet("map/route")]
    public async Task<IActionResult> GetDeliveryMap(
        [FromQuery] Guid? driverId = null,
        [FromQuery] Guid? focusedStopId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverDeliveryMapQuery(driverId, focusedStopId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Starts a driver work session / shift (Screen 05.01 -> 05.02).
    /// Validates driver readiness, sets IsAvailable = true, opens DriverWorkSession, and broadcasts to Dispatcher.
    /// </summary>
    [HttpPost("shift/start")]
    public async Task<IActionResult> StartShift(
        [FromBody] StartShiftRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageDriverShift())
        {
            return HandleResult(SelfShiftManagementError());
        }

        var result = await Sender.Send(new StartDriverShiftCommand(
            DriverId: driverId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            ShiftLabel: request.ShiftLabel,
            Notes: request.Notes), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        // Broadcast driver online/active status to Dispatcher Hub
        try
        {
            var shiftPayload = new
            {
                driverId = result.Value.DriverId,
                driverName = result.Value.DriverName,
                statusCategory = "InDelivery",
                statusText = result.Value.CurrentStatusText,
                isAvailable = true,
                startedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverStatusUpdated, shiftPayload, cancellationToken);
            await _driverHub.Clients.Group(DriverHub.GroupForDriver(result.Value.DriverId))
                .SendAsync(DriverHub.EventShiftStatusConfirmed, shiftPayload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Ends a driver work session / shift (Screen 05.02 -> 05.03).
    /// Verifies no active pending deliveries remain, closes session, and returns final celebratory summary.
    /// </summary>
    [HttpPost("shift/end")]
    public async Task<IActionResult> EndShift(
        [FromBody] EndShiftRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageDriverShift())
        {
            return HandleResult(SelfShiftManagementError());
        }

        var result = await Sender.Send(new EndDriverShiftCommand(
            DriverId: driverId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            Notes: request.Notes,
            ForceEndWithPendingReturns: request.ForceEndWithPendingReturns), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        // Broadcast shift ended to Dispatcher Hub
        try
        {
            var shiftPayload = new
            {
                sessionId = result.Value.SessionId,
                driverName = result.Value.DriverName,
                driverCode = result.Value.DriverCode,
                statusCategory = "Offline",
                isAvailable = false,
                endedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverStatusUpdated, shiftPayload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Returns celebratory work session summary for Screen 05.03.
    /// </summary>
    [HttpGet("shift/summary")]
    public async Task<IActionResult> GetShiftSummary(
        [FromQuery] Guid? driverId = null,
        [FromQuery] Guid? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverShiftSummaryQuery(driverId, sessionId), cancellationToken);
        return HandleResult(result);
    }


    /// <summary>
    /// Completes delivery of a box to the customer with invoice barcode scan and proof of delivery photo.
    /// Automatically updates daily target counter and transitions to next stop.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/deliver")]
    public async Task<IActionResult> CompleteDelivery(
        [FromRoute] Guid boxId,
        [FromBody] CompleteDeliveryRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new CompleteBoxDeliveryCommand(
            BoxId: boxId,
            BarcodeValue: request.BarcodeValue,
            ProofPhotoStorageKey: request.ProofPhotoStorageKey,
            DeliveryOtp: request.DeliveryOtp,
            HandoverNotes: request.HandoverNotes,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId,
                driverId,
                status = "Delivered",
                deliveredAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("box-delivered", payload, cancellationToken);
            await _driverHub.Clients.All.SendAsync(DriverHub.EventDeliveryCompleted, payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Reports an urgent delivery issue from the driver field app (Screen 05.02 warning banner).
    /// Instantly raises an urgent operational ticket in the Dispatcher Support Center (Screen 04.11 & 04.12).
    /// </summary>
    [HttpPost("orders/{boxId:guid}/report-issue")]
    public async Task<IActionResult> ReportIssue(
        [FromRoute] Guid boxId,
        [FromBody] ReportIssueRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ReportDriverIssueCommand(
            BoxId: boxId,
            IssueType: request.IssueType,
            Description: request.Description,
            DriverId: driverId,
            EvidencePhotoStorageKey: request.EvidencePhotoStorageKey,
            Severity: request.Severity), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var issuePayload = new
            {
                issueId = result.Value,
                boxId,
                driverId,
                issueType = request.IssueType,
                description = request.Description,
                severity = request.Severity,
                reportedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverIssueUpdated, issuePayload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Confirms driver arrival at the customer delivery location (Screen 07.01 -> 07.02).
    /// Broadcasts arrival event to customer and dispatcher.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/arrive-customer")]
    public async Task<IActionResult> ArriveAtCustomer(
        [FromRoute] Guid boxId,
        [FromQuery] decimal? latitude = null,
        [FromQuery] decimal? longitude = null,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ConfirmCustomerArrivalCommand(
            BoxId: boxId,
            Latitude: latitude,
            Longitude: longitude,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId,
                driverId,
                status = "ArrivedAtCustomer",
                arrivedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("driver-arrived-at-customer", payload, cancellationToken);
            await _driverHub.Clients.All.SendAsync("driver-arrived-at-customer", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Reports delivery delay to dispatcher (Screen 07.03).
    /// </summary>
    [HttpPost("orders/{boxId:guid}/report-delay")]
    public async Task<IActionResult> ReportDelay(
        [FromRoute] Guid boxId,
        [FromBody] ReportDelayRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ReportDeliveryDelayCommand(
            BoxId: boxId,
            DelayMinutes: request.DelayMinutes,
            Reason: request.Reason,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId,
                driverId,
                delayMinutes = request.DelayMinutes,
                reason = request.Reason,
                reportedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("delivery-delay-reported", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Reports failed delivery to customer with reason category and evidence (Screen 07.04).
    /// Sets stop status to Failed and alerts dispatcher.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/fail-delivery")]
    public async Task<IActionResult> FailDelivery(
        [FromRoute] Guid boxId,
        [FromBody] FailDeliveryRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new FailDeliveryCommand(
            BoxId: boxId,
            ReasonCategory: request.ReasonCategory,
            Notes: request.Notes,
            ProofPhotoStorageKey: request.ProofPhotoStorageKey,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId,
                driverId,
                reasonCategory = request.ReasonCategory,
                notes = request.Notes,
                status = "Failed",
                failedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("delivery-failed", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Confirms box return to the restaurant kitchen after failed delivery (Screen 07.05).
    /// Releases driver responsibility and clears pending shift blockers.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/return-to-kitchen")]
    public async Task<IActionResult> ReturnBoxToKitchen(
        [FromRoute] Guid boxId,
        [FromBody] ReturnBoxRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ReturnBoxToKitchenCommand(
            BoxId: boxId,
            Reason: request.Reason,
            Notes: request.Notes,
            ReturnProofPhotoStorageKey: request.ReturnProofPhotoStorageKey,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId,
                driverId,
                status = "ReturnedToKitchen",
                returnedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("box-returned-to-kitchen", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Driver submits a request to reassign a box delivery to another driver (Screen 08.02 -> 08.03).
    /// Instantly broadcasts to Dispatcher Dashboard for manual/auto reassignment.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/request-reassignment")]
    public async Task<IActionResult> RequestReassignment(
        [FromRoute] Guid boxId,
        [FromBody] RequestReassignmentRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new RequestReassignmentCommand(
            BoxId: boxId,
            Reason: request.Reason,
            Notes: request.Notes,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            DriverId: driverId), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                requestId = result.Value.RequestId,
                boxId,
                driverId,
                reason = request.Reason,
                notes = request.Notes,
                status = "ReassignmentRequested",
                requestedAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverIssueUpdated, payload, cancellationToken);
            await _dispatcherHub.Clients.All.SendAsync("driver-requested-reassignment", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Returns customer call proxy details including masked phone number for privacy protection (Screen 08.06, 08.07, 08.08).
    /// </summary>
    [HttpGet("orders/{boxId:guid}/call-proxy")]
    public async Task<IActionResult> GetCustomerCallProxy(
        [FromRoute] Guid boxId,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCustomerCallProxyQuery(boxId, driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Synchronizes a batch of scans, deliveries, and status updates captured offline on the driver's phone (Screen 06.14).
    /// </summary>
    [HttpPost("sync/offline-queue")]
    public async Task<IActionResult> SyncOfflineQueue(
        [FromBody] SyncDriverOfflineQueueCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Operational status is supervisor-managed: the driver role must not be able
    /// to start or end its own work session through these endpoints.
    /// </summary>
    private bool CanManageDriverShift() => User?.Identity?.IsAuthenticated == true
        && !User.IsInRole(AppRoles.Driver)
        && (User.IsInRole(AppRoles.DeliveryManager)
            || User.IsInRole(AppRoles.RestaurantOwner)
            || User.IsInRole(AppRoles.SuperAdmin)
            || User.IsInRole(AppRoles.OperationsAdmin)
            || User.HasClaim(AppClaimTypes.Permission, PermissionCodes.DeliveryDriversManage));

    private static Result SelfShiftManagementError() => Result.Failure(Error.Forbidden(
        "Driver.SelfShiftManagementDisabled",
        "Drivers cannot change their own operational status from the app; the delivery supervisor manages it."));
}
