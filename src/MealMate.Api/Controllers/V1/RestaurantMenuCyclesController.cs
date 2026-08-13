using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[DeprecatedRoute]
[Route("api/v1/restaurants/{restaurantId:guid}/menu-cycles")]
[RestaurantOwnership]
public sealed class RestaurantMenuCyclesController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    public RestaurantMenuCyclesController(ICurrentUser currentUser) => _currentUser = currentUser;

    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(Guid restaurantId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantMenuCyclesQuery(restaurantId), ct)) : Forbid();

    [HttpGet("current")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Current(Guid restaurantId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantMenuOfDateQuery(
            restaurantId, date ?? DateOnly.FromDateTime(DateTime.UtcNow)), ct)) : Forbid();

    [HttpGet("{cycleId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get(Guid restaurantId, Guid cycleId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantMenuCycleQuery(restaurantId, cycleId), ct)) : Forbid();

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Create(Guid restaurantId, [FromBody] CreateMenuCycleRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleCreated(await Sender.Send(new CreateRestaurantMenuCycleCommand(
            restaurantId, body.NameAr, body.NameEn, body.EffectiveFrom, body.Days, body.ProgramIds), ct),
            $"/api/v1/restaurants/{restaurantId}/menu-cycles") : Forbid();

    [HttpPut("{cycleId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Update(Guid restaurantId, Guid cycleId, [FromBody] UpdateMenuCycleRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new UpdateMenuCycleDraftCommand(
            restaurantId, cycleId, body.NameAr, body.NameEn, body.EffectiveFrom, body.Days), ct)) : Forbid();

    [HttpPost("{cycleId:guid}/submit")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Submit(Guid restaurantId, Guid cycleId, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new SubmitMenuCycleCommand(restaurantId, cycleId), ct)) : Forbid();

    [HttpPost("{cycleId:guid}/future-version")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> FutureVersion(Guid restaurantId, Guid cycleId, [FromBody] FutureVersionRequest body, CancellationToken ct)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new CreateFutureMenuCycleVersionCommand(restaurantId, cycleId, body.EffectiveFrom), ct)) : Forbid();

    [HttpGet("{cycleId:guid}/preview")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Preview(Guid restaurantId, Guid cycleId, [FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default)
        => Owns(restaurantId) ? HandleResult(await Sender.Send(new PreviewRestaurantMenuCycleQuery(
            restaurantId, cycleId, from ?? DateOnly.FromDateTime(DateTime.UtcNow), days), ct)) : Forbid();

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

public sealed record CreateMenuCycleRequest(
    string NameAr, string NameEn, DateOnly EffectiveFrom,
    IReadOnlyList<MenuCycleDayInput> Days, IReadOnlyList<Guid> ProgramIds);
public sealed record UpdateMenuCycleRequest(string NameAr, string NameEn, DateOnly EffectiveFrom, IReadOnlyList<MenuCycleDayInput> Days);
public sealed record FutureVersionRequest(DateOnly EffectiveFrom);
public sealed record SetMenuDayMealRequest(Guid MealId, int SortOrder = 0, bool IsPrimary = false);
