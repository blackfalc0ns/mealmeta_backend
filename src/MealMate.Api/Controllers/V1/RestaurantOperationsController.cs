using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Application.Features.RestaurantOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/operations")]
[Authorize]
public sealed class RestaurantOperationsController : ApiControllerBase
{
    [HttpGet("capacity")]
    public async Task<IActionResult> GetCapacityOverview(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantCapacityOverviewQuery(from, to), cancellationToken));

    [HttpGet("nutrition-standards")]
    public async Task<IActionResult> GetNutritionStandards(
        [FromQuery] Guid? programId = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetProgramNutritionStandardsQuery(programId), cancellationToken));

    [HttpGet("nutrition-standards/daily-target")]
    public async Task<IActionResult> GetDailyNutritionTarget(
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetProgramDailyNutritionTargetQuery(programId, bundleId), cancellationToken));

    [HttpPut("capacity/base")]
    public async Task<IActionResult> UpdateBaseCapacity(
        [FromBody] UpdateBaseCapacityRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateBaseCapacityCommand(request.NewCapacity), cancellationToken));

    [HttpPut("capacity/dates/{date}/limit")]
    public async Task<IActionResult> UpdateDateCapacity(
        DateOnly date,
        [FromBody] UpdateDateCapacityRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateDateCapacityCommand(date, request.NewCapacity), cancellationToken));

    [HttpPut("capacity/dates/{date}")]
    public async Task<IActionResult> SetDateManualBusy(
        DateOnly date,
        [FromBody] SetDateManualBusyRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new SetDateManualBusyCommand(date, request.IsManualBusy, request.Reason), cancellationToken));

    [HttpGet("working-days")]
    public async Task<IActionResult> GetWorkingDaysSchedule(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetWorkingDaysScheduleQuery(), cancellationToken));

    [HttpPut("working-days")]
    public async Task<IActionResult> UpdateWorkingDaysSchedule(
        [FromBody] UpdateWorkingDaysScheduleRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateWorkingDaysScheduleCommand(request.Schedule), cancellationToken));

    [HttpPost("working-days/exceptions")]
    public async Task<IActionResult> AddWorkingDayException(
        [FromBody] AddWorkingDayExceptionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new AddWorkingDayExceptionCommand(request.Date, request.IsWorkingDay, request.Reason), cancellationToken));

    [HttpDelete("working-days/exceptions/{date}")]
    public async Task<IActionResult> DeleteWorkingDayException(
        DateOnly date,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new DeleteWorkingDayExceptionCommand(date), cancellationToken));

    [HttpPost("emergency-override")]
    public async Task<IActionResult> ActivateEmergencyOverride(
        [FromBody] ActivateEmergencyOverrideRequest request,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            HttpContext.RequestServices.GetRequiredService<ICurrentUser>(), restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<RestaurantEmergencyOverrideDto>(scopeResult.Error));
        }

        return HandleResult(await Sender.Send(new ActivateRestaurantEmergencyOverrideCommand(
            scopeResult.Value,
            request.From,
            request.To,
            request.ReducedCapacity,
            request.Closed,
            request.Reason), cancellationToken));
    }

    [HttpPost("emergency-override/{overrideId:guid}/resolve")]
    public async Task<IActionResult> ResolveEmergencyOverride(
        Guid overrideId,
        [FromBody] ResolveEmergencyOverrideRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ResolveRestaurantEmergencyOverrideCommand(overrideId, request.Reason), cancellationToken));


    [HttpGet("service-regions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetServiceRegions(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantServiceRegionsQuery(), cancellationToken));

    [HttpGet("service-regions/{regionId:guid}")]
    public async Task<IActionResult> GetServiceRegionDetail(
        Guid regionId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantServiceRegionDetailQuery(regionId), cancellationToken));

    [HttpPut("service-regions")]
    public async Task<IActionResult> UpdateServiceRegionSelection(
        [FromBody] UpdateServiceRegionSelectionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantServiceRegionSelectionCommand(request.RegionIds),
            cancellationToken));

    [HttpPut("service-regions/{regionId:guid}")]
    public async Task<IActionResult> UpdateServiceRegion(
        Guid regionId,
        [FromBody] UpdateServiceRegionRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateRestaurantServiceRegionCommand(
            regionId,
            request.IsActive,
            request.DeliveryWindowStart,
            request.DeliveryWindowEnd,
            request.PreparationLeadTimeMinutes), cancellationToken));
}

public sealed record UpdateBaseCapacityRequest(int NewCapacity);
public sealed record UpdateDateCapacityRequest(int NewCapacity);
public sealed record SetDateManualBusyRequest(bool IsManualBusy, string? Reason = null);
public sealed record UpdateWorkingDaysScheduleRequest(List<WeeklyDayScheduleInputDto> Schedule);
public sealed record AddWorkingDayExceptionRequest(DateOnly Date, bool IsWorkingDay, string? Reason = null);
public sealed record UpdateServiceRegionRequest(
    bool IsActive,
    TimeOnly DeliveryWindowStart,
    TimeOnly DeliveryWindowEnd,
    int? PreparationLeadTimeMinutes = null);
public sealed record UpdateServiceRegionSelectionRequest(List<Guid> RegionIds);
public sealed record ActivateEmergencyOverrideRequest(
    DateOnly From,
    DateOnly To,
    int? ReducedCapacity,
    bool Closed,
    string Reason);
public sealed record ResolveEmergencyOverrideRequest(string Reason);

