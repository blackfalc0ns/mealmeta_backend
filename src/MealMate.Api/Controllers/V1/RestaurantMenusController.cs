using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantMeals;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/{restaurantId:guid}/menus")]
[RestaurantOwnership]
public sealed class RestaurantMenusController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;

    public RestaurantMenusController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(
        Guid restaurantId,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetMenusListQuery(restaurantId, isActive, search, page, pageSize), cancellationToken));

    [HttpGet("programs")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Programs(Guid restaurantId, CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId != restaurantId)
        {
            return Forbid();
        }

        return HandleResult(await Sender.Send(new GetRestaurantMealProgramsQuery(restaurantId), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMenuByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Create(Guid restaurantId, [FromBody] CreateMenuRequest request, CancellationToken cancellationToken)
        => HandleCreated(
            await Sender.Send(new CreateMenuCommand(restaurantId, request.NameAr, request.NameEn, request.ProgramId), cancellationToken),
            $"/api/v1/restaurants/{restaurantId}/menus");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Update(Guid restaurantId, Guid id, [FromBody] UpdateMenuRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateMenuCommand(id, request.NameAr, request.NameEn, request.ProgramId), cancellationToken));

    [HttpPost("{id:guid}/toggle-active")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> ToggleActive(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ToggleMenuActiveCommand(id), cancellationToken));
}

public sealed record CreateMenuRequest(
    string NameAr,
    string NameEn,
    Guid? ProgramId = null);

public sealed record UpdateMenuRequest(
    string NameAr,
    string NameEn,
    Guid? ProgramId = null);
