using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantMeals;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/{restaurantId:guid}/ingredients")]
public sealed class RestaurantIngredientsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantIngredientsView)]
    public async Task<IActionResult> List(
        Guid restaurantId,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetIngredientsListQuery(restaurantId, search, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantIngredientsView)]
    public async Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetIngredientByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> Create(Guid restaurantId, [FromBody] CreateIngredientRequest request, CancellationToken cancellationToken)
        => HandleCreated(
            await Sender.Send(new CreateIngredientCommand(
                restaurantId,
                request.Code,
                request.NameAr,
                request.NameEn,
                request.DescriptionAr,
                request.DescriptionEn,
                request.DefaultCaloriesPer100g,
                request.DefaultProteinPer100g,
                request.DefaultCarbsPer100g,
                request.DefaultFatPer100g,
                request.AllergenCodes,
                request.Category,
                request.DefaultWeightGrams), cancellationToken),
            $"/api/v1/restaurants/{restaurantId}/ingredients");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> Update(Guid restaurantId, Guid id, [FromBody] UpdateIngredientRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateIngredientCommand(
            id,
            request.NameAr,
            request.NameEn,
            request.DescriptionAr,
            request.DescriptionEn,
            request.DefaultCaloriesPer100g,
            request.DefaultProteinPer100g,
            request.DefaultCarbsPer100g,
            request.DefaultFatPer100g,
            request.AllergenCodes,
            request.Category,
            request.DefaultWeightGrams), cancellationToken));

    [HttpPost("{id:guid}/toggle-active")]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> ToggleActive(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ToggleIngredientActiveCommand(id), cancellationToken));

    [HttpPost("bulk")]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> BulkCreate(Guid restaurantId, [FromBody] BulkCreateIngredientsRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new BulkCreateIngredientsCommand(
            restaurantId,
            request.Items.Select(x => new BulkIngredientItemDto(
                x.Code,
                x.NameAr,
                x.NameEn,
                x.DescriptionAr,
                x.DescriptionEn,
                x.DefaultCaloriesPer100g,
                x.DefaultProteinPer100g,
                x.DefaultCarbsPer100g,
                x.DefaultFatPer100g,
                x.AllergenCodes,
                x.Category,
                x.DefaultWeightGrams)).ToList()), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> Delete(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteIngredientCommand(id), cancellationToken));

    [HttpDelete("clear")]
    [HasPermission(PermissionCodes.RestaurantIngredientsManage)]
    public async Task<IActionResult> Clear(Guid restaurantId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ClearIngredientsCommand(restaurantId), cancellationToken));
}

public sealed record BulkCreateIngredientsRequest(List<CreateIngredientRequest> Items);

public sealed record CreateIngredientRequest(
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? DefaultCaloriesPer100g = null,
    decimal? DefaultProteinPer100g = null,
    decimal? DefaultCarbsPer100g = null,
    decimal? DefaultFatPer100g = null,
    string? AllergenCodes = null,
    string? Category = null,
    decimal? DefaultWeightGrams = null);

public sealed record UpdateIngredientRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? DefaultCaloriesPer100g = null,
    decimal? DefaultProteinPer100g = null,
    decimal? DefaultCarbsPer100g = null,
    decimal? DefaultFatPer100g = null,
    string? AllergenCodes = null,
    string? Category = null,
    decimal? DefaultWeightGrams = null);
