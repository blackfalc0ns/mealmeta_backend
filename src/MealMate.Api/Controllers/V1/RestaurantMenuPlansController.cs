using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/{restaurantId:guid}/menu-plans")]
[RestaurantOwnership]
public sealed class RestaurantMenuPlansController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    public RestaurantMenuPlansController(ICurrentUser currentUser) => _currentUser = currentUser;

    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(Guid restaurantId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantMenuCyclesQuery(restaurantId), ct)) : Forbid();

    [HttpGet("resolve")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Resolve(Guid restaurantId, [FromQuery] Guid programId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new ResolveMenuPlanQuery(
            restaurantId, programId, date ?? DateOnly.FromDateTime(DateTime.UtcNow)), ct)) : Forbid();

    [HttpGet("{planId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get(Guid restaurantId, Guid planId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantMenuCycleQuery(restaurantId, planId), ct)) : Forbid();

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Create(Guid restaurantId, [FromBody] CreateMenuPlanRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleCreated(await Sender.Send(new CreateRestaurantMenuCycleCommand(
            restaurantId, body.NameAr, body.NameEn, body.EffectiveFrom, body.Days ?? [], body.ProgramIds, body.ParsedMode()), ct),
            $"/api/v1/restaurants/{restaurantId}/menu-plans") : Forbid();

    [HttpPut("{planId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Update(Guid restaurantId, Guid planId, [FromBody] UpdateMenuCycleRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new UpdateMenuCycleDraftCommand(
            restaurantId, planId, body.NameAr, body.NameEn, body.EffectiveFrom, body.Days), ct)) : Forbid();

    [HttpPost("{planId:guid}/submit")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Submit(Guid restaurantId, Guid planId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new SubmitMenuCycleCommand(restaurantId, planId), ct)) : Forbid();

    [HttpPost("{planId:guid}/future-version")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> FutureVersion(Guid restaurantId, Guid planId, [FromBody] FutureVersionRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new CreateFutureMenuCycleVersionCommand(restaurantId, planId, body.EffectiveFrom, body.Days), ct)) : Forbid();

    [HttpPost("{planId:guid}/sequence/add")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> AddSequenceDay(Guid restaurantId, Guid planId, [FromBody] AddMenuSequenceDayRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new AddMenuCycleSequenceDayCommand(
            restaurantId, planId, body.NameAr, body.NameEn, body.TargetDayNumber), ct)) : Forbid();

    [HttpPost("{planId:guid}/sequence/swap")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> SwapSequenceDay(Guid restaurantId, Guid planId, [FromBody] SwapMenuSequenceDayRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new SwapMenuCycleSequenceDayCommand(
            restaurantId, planId, body.DayId, body.TargetDayNumber), ct)) : Forbid();

    [HttpDelete("{planId:guid}/sequence/pending")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> CancelPendingSequence(Guid restaurantId, Guid planId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new CancelMenuCyclePendingSequenceCommand(restaurantId, planId), ct)) : Forbid();

    [HttpGet("{planId:guid}/preview")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Preview(Guid restaurantId, Guid planId, [FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new PreviewRestaurantMenuCycleQuery(
            restaurantId, planId, from ?? DateOnly.FromDateTime(DateTime.UtcNow), days), ct)) : Forbid();

    [HttpPost("{planId:guid}/weekdays/meals")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> SetWeekdayMeal(Guid restaurantId, Guid planId, [FromBody] SetWeekdayMealRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new SetWeekdayMealCommand(
            restaurantId, planId, body.DayOfWeek, body.MealId, body.SortOrder, body.IsPrimary), ct)) : Forbid();

    [HttpDelete("{planId:guid}/weekdays/{dayOfWeek}/meals/{mealId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> RemoveWeekdayMeal(Guid restaurantId, Guid planId, DayOfWeek dayOfWeek, Guid mealId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new RemoveWeekdayMealCommand(restaurantId, planId, dayOfWeek, mealId), ct)) : Forbid();

    [HttpPatch("menu-days/{dayId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> RenameDay(Guid restaurantId, Guid dayId, [FromBody] RenameMenuDayRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new RenameMenuCycleDayCommand(
            restaurantId, dayId, body.NameAr, body.NameEn), ct)) : Forbid();

    [HttpPost("menu-days/{dayId:guid}/meals")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> AddMeal(Guid restaurantId, Guid dayId, [FromBody] SetMenuDayMealRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new SetMenuDayMealCommand(restaurantId, dayId, body.MealId, body.SortOrder, body.IsPrimary), ct)) : Forbid();

    [HttpDelete("menu-days/{dayId:guid}/meals/{mealId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> RemoveMeal(Guid restaurantId, Guid dayId, Guid mealId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new RemoveMenuDayMealCommand(restaurantId, dayId, mealId), ct)) : Forbid();

    private bool Owns(Guid restaurantId) => _currentUser.RestaurantId == restaurantId;
}

public sealed record CreateMenuPlanRequest(
    string NameAr, string NameEn, DateOnly EffectiveFrom,
    IReadOnlyList<Guid> ProgramIds,
    IReadOnlyList<MenuCycleDayInput>? Days = null,
    JsonElement Mode = default)
{
    public ScheduleMode ParsedMode()
    {
        if (Mode.ValueKind == JsonValueKind.Number && Mode.TryGetInt32(out var numeric))
            return numeric == 2 ? ScheduleMode.Weekday : ScheduleMode.Cycle;
        if (Mode.ValueKind == JsonValueKind.String)
        {
            var raw = Mode.GetString();
            if (string.Equals(raw, "Weekday", StringComparison.OrdinalIgnoreCase) || raw == "2")
                return ScheduleMode.Weekday;
        }
        return ScheduleMode.Cycle;
    }
}

public sealed record SetWeekdayMealRequest(DayOfWeek DayOfWeek, Guid MealId, int SortOrder = 0, bool IsPrimary = false);

public sealed record AddMenuSequenceDayRequest(string NameAr, string NameEn, int TargetDayNumber);

public sealed record SwapMenuSequenceDayRequest(Guid DayId, int TargetDayNumber);

public sealed record RenameMenuDayRequest(string NameAr, string NameEn);
