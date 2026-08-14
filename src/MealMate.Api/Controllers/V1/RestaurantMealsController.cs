using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using MealMate.Application.Features.RestaurantMeals;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/{restaurantId:guid}/meals")]
[RestaurantOwnership]
public sealed class RestaurantMealsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(
        Guid restaurantId,
        [FromQuery] Guid? menuId = null,
        [FromQuery] bool? availableOnly = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetMealsListQuery(restaurantId, menuId, availableOnly, search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMealByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Create(Guid restaurantId, [FromBody] CreateMealRequest request, CancellationToken cancellationToken)
        => HandleCreated(
            await Sender.Send(new CreateMealCommand(
                request.MenuId,
                restaurantId,
                request.Code,
                request.NameAr,
                request.NameEn,
                request.BundleId,
                request.DescriptionAr,
                request.DescriptionEn,
                request.Calories,
                request.ProteinG,
                request.CarbsG,
                request.FatG,
                request.Slot,
                request.ServingWeightG,
                request.FiberG,
                request.NetCarbsG,
                request.SugarAlcoholG,
                request.SugarAlcoholType,
                request.SaturatedFatG,
                request.TotalSugarsG,
                request.AddedSugarsG,
                request.SodiumMg,
                request.NutritionSource,
                request.AnalysisReference,
                request.VerificationDate,
                request.VerifiedBy,
                request.RecipeVersion,
                request.RestaurantNotes,
                request.AllergensReviewed,
                request.PresentAllergenCodes,
                request.MayContainAllergenCodes,
                request.CrossContactNotes,
                request.DataResponsibilityConfirmed,
                request.NutritionTagCodes,
                request.SearchExclusionTagCodes,
                request.ProgramId,
                request.MenuDayIds,
                request.ProgramAssignments,
                DietaryPreferenceCodes: request.DietaryPreferenceCodes), cancellationToken),
            $"/api/v1/restaurants/{restaurantId}/meals");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Update(Guid restaurantId, Guid id, [FromBody] UpdateMealRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateMealCommand(
            id,
            request.NameAr,
            request.NameEn,
            request.BundleId,
            request.DescriptionAr,
            request.DescriptionEn,
            request.Calories,
            request.ProteinG,
            request.CarbsG,
            request.FatG,
            request.Slot,
            request.ServingWeightG,
            request.FiberG,
            request.NetCarbsG,
            request.SugarAlcoholG,
            request.SugarAlcoholType,
            request.SaturatedFatG,
            request.TotalSugarsG,
            request.AddedSugarsG,
            request.SodiumMg,
            request.NutritionSource,
            request.AnalysisReference,
            request.VerificationDate,
            request.VerifiedBy,
            request.RecipeVersion,
            request.RestaurantNotes,
            request.AllergensReviewed,
            request.PresentAllergenCodes,
            request.MayContainAllergenCodes,
            request.CrossContactNotes,
            request.DataResponsibilityConfirmed,
            request.NutritionTagCodes,
            request.SearchExclusionTagCodes,
            request.ProgramId,
            request.MenuDayIds,
            request.ProgramAssignments,
            DietaryPreferenceCodes: request.DietaryPreferenceCodes), cancellationToken));

    [HttpPost("{id:guid}/toggle-availability")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> ToggleAvailability(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ToggleMealAvailabilityCommand(id), cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Submit(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SubmitMealCommand(id), cancellationToken));

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Withdraw(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new WithdrawMealCommand(id), cancellationToken));

    [HttpPost("{id:guid}/ingredients")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> LinkIngredient(Guid restaurantId, Guid id, [FromBody] LinkMealIngredientRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new LinkMealIngredientCommand(id, request.IngredientId, request.PortionGrams, request.IsOptional, request.SortOrder), cancellationToken));

    [HttpDelete("{id:guid}/ingredients/{ingredientId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> UnlinkIngredient(Guid restaurantId, Guid id, Guid ingredientId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnlinkMealIngredientCommand(id, ingredientId), cancellationToken));

    [HttpPost("{id:guid}/images")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> AddImage(Guid restaurantId, Guid id, [FromBody] AddMealImageRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AddMealImageCommand(id, request.ImageUrl, request.IsPrimary, request.DisplayOrder, request.AltTextAr, request.AltTextEn), cancellationToken));

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> RemoveImage(Guid restaurantId, Guid id, Guid imageId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RemoveMealImageCommand(id, imageId), cancellationToken));

    [HttpPost("bulk")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> BulkCreate(Guid restaurantId, [FromBody] BulkCreateMealsRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new BulkCreateMealsCommand(
            restaurantId,
            request.MenuId,
            request.Items.Select(x => new BulkMealItemDto(
                x.Code,
                x.NameAr,
                x.NameEn,
                x.ProgramId,
                x.BundleId,
                x.DescriptionAr,
                x.DescriptionEn,
                x.Calories,
                x.ProteinG,
                x.CarbsG,
                x.FatG,
                x.Ingredients,
                x.Slot,
                x.ServingWeightG,
                x.FiberG,
                x.NetCarbsG,
                x.SugarAlcoholG,
                x.SugarAlcoholType,
                x.SaturatedFatG,
                x.TotalSugarsG,
                x.AddedSugarsG,
                x.SodiumMg,
                x.NutritionSource,
                x.AnalysisReference,
                x.VerificationDate,
                x.VerifiedBy,
                x.RecipeVersion,
                x.RestaurantNotes,
                x.AllergensReviewed,
                x.PresentAllergenCodes,
                x.MayContainAllergenCodes,
                x.CrossContactNotes,
                x.DataResponsibilityConfirmed,
                x.NutritionTagCodes,
                x.SearchExclusionTagCodes,
                x.DietaryPreferenceCodes)).ToList()), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Delete(Guid restaurantId, Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteMealCommand(id), cancellationToken));

    [HttpDelete("clear")]
    [HasPermission(PermissionCodes.RestaurantMealsManage)]
    public async Task<IActionResult> Clear(Guid restaurantId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ClearMealsCommand(restaurantId), cancellationToken));
}

public sealed record CreateMealRequest(
    Guid? MenuId,
    string Code,
    string NameAr,
    string NameEn,
    Guid BundleId,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    Guid? ProgramId = null,
    IReadOnlyList<Guid>? MenuDayIds = null,
    IReadOnlyList<MealProgramAssignmentInput>? ProgramAssignments = null,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);

public sealed record BulkMealItemRequest(
    string Code,
    string NameAr,
    string NameEn,
    Guid ProgramId,
    Guid BundleId,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Ingredients = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);

public sealed record UpdateMealRequest(
    string NameAr,
    string NameEn,
    Guid? BundleId = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    decimal? Calories = null,
    decimal? ProteinG = null,
    decimal? CarbsG = null,
    decimal? FatG = null,
    string? Slot = null,
    decimal? ServingWeightG = null,
    decimal? FiberG = null,
    decimal? NetCarbsG = null,
    decimal? SugarAlcoholG = null,
    string? SugarAlcoholType = null,
    decimal? SaturatedFatG = null,
    decimal? TotalSugarsG = null,
    decimal? AddedSugarsG = null,
    decimal? SodiumMg = null,
    string? NutritionSource = null,
    string? AnalysisReference = null,
    DateOnly? VerificationDate = null,
    string? VerifiedBy = null,
    string? RecipeVersion = null,
    string? RestaurantNotes = null,
    bool AllergensReviewed = false,
    string? PresentAllergenCodes = null,
    string? MayContainAllergenCodes = null,
    string? CrossContactNotes = null,
    bool DataResponsibilityConfirmed = false,
    string? NutritionTagCodes = null,
    string? SearchExclusionTagCodes = null,
    Guid? ProgramId = null,
    IReadOnlyList<Guid>? MenuDayIds = null,
    IReadOnlyList<MealProgramAssignmentInput>? ProgramAssignments = null,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);

public sealed record LinkMealIngredientRequest(
    Guid IngredientId,
    decimal PortionGrams,
    bool IsOptional = false,
    int SortOrder = 0);

public sealed record AddMealImageRequest(
    string ImageUrl,
    bool IsPrimary = false,
    int DisplayOrder = 0,
    string? AltTextAr = null,
    string? AltTextEn = null);

public sealed record BulkCreateMealsRequest(Guid? MenuId, List<BulkMealItemRequest> Items);
