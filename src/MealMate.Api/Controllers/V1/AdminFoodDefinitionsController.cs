using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.FoodDefinitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/food-definitions")]
public sealed class AdminFoodDefinitionsController : ApiControllerBase
{
    private readonly IAdminFoodDefinitionsService _foodDefinitionsService;

    public AdminFoodDefinitionsController(IAdminFoodDefinitionsService foodDefinitionsService)
        => _foodDefinitionsService = foodDefinitionsService;

    [HttpGet("allergens")]
    public async Task<IActionResult> Allergens(CancellationToken ct)
        => HandleResult(await _foodDefinitionsService.GetAllergensAsync(ct));

    [HttpPut("allergens/{code}")]
    public async Task<IActionResult> SaveAllergen(string code, [FromBody] FoodDefinitionRequest request, CancellationToken ct)
        => HandleResult(await _foodDefinitionsService.SaveAllergenAsync(code, request, ct));

    [HttpGet("dietary-preferences")]
    public async Task<IActionResult> Dietary(CancellationToken ct)
        => HandleResult(await _foodDefinitionsService.GetDietaryPreferencesAsync(ct));

    [HttpPut("dietary-preferences/{code}")]
    public async Task<IActionResult> SaveDietary(string code, [FromBody] FoodDefinitionRequest request, CancellationToken ct)
        => HandleResult(await _foodDefinitionsService.SaveDietaryPreferenceAsync(code, request, ct));
}
