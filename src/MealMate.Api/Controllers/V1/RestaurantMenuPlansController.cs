using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

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
            restaurantId, body.NameAr, body.NameEn, body.EffectiveFrom, body.Days ?? [], body.ProgramIds, body.Mode), ct),
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
    IReadOnlyList<Guid> ProgramIds, ScheduleMode Mode = ScheduleMode.Cycle,
    IReadOnlyList<MenuCycleDayInput>? Days = null);

public sealed record SetWeekdayMealRequest(DayOfWeek DayOfWeek, Guid MealId, int SortOrder = 0, bool IsPrimary = false);
