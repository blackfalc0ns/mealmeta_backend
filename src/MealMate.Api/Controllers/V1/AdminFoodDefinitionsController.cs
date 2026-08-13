using MealMate.Api.Common;
using MealMate.Api.Authorization;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.RestaurantOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/food-definitions")]
public sealed class AdminFoodDefinitionsController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    public AdminFoodDefinitionsController(IAppDbContext db) => _db = db;

    [HttpGet("allergens")]
    public async Task<IActionResult> Allergens(CancellationToken ct) => Ok(await _db.AllergenDefinitions.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(ct));

    [HttpPut("allergens/{code}")]
    public async Task<IActionResult> SaveAllergen(string code, [FromBody] FoodDefinitionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return BadRequest(new { code = "food_definition.names_required" });
        var normalized = FoodCodeNormalizer.Normalize(code);
        var entity = await _db.AllergenDefinitions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == normalized, ct);
        if (entity is null) { entity = new AllergenDefinition { Code = normalized }; _db.AllergenDefinitions.Add(entity); }
        entity.NameAr = request.NameAr.Trim(); entity.NameEn = request.NameEn.Trim(); entity.IsActive = request.IsActive; entity.SortOrder = request.SortOrder; entity.IsDeleted = false; entity.DeletedAtUtc = null;
        await _db.SaveChangesAsync(ct); return Ok(entity);
    }

    [HttpGet("dietary-preferences")]
    public async Task<IActionResult> Dietary(CancellationToken ct) => Ok(await _db.FoodPreferenceDefinitions.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(ct));

    [HttpPut("dietary-preferences/{code}")]
    public async Task<IActionResult> SaveDietary(string code, [FromBody] FoodDefinitionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return BadRequest(new { code = "food_definition.names_required" });
        var normalized = FoodCodeNormalizer.Normalize(code);
        var entity = await _db.FoodPreferenceDefinitions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == normalized, ct);
        if (entity is null) { entity = new FoodPreferenceDefinition { Code = normalized }; _db.FoodPreferenceDefinitions.Add(entity); }
        entity.NameAr = request.NameAr.Trim(); entity.NameEn = request.NameEn.Trim(); entity.IsActive = request.IsActive; entity.SortOrder = request.SortOrder; entity.IsDeleted = false; entity.DeletedAtUtc = null;
        await _db.SaveChangesAsync(ct); return Ok(entity);
    }
}

public sealed record FoodDefinitionRequest(string NameAr, string NameEn, bool IsActive = true, int SortOrder = 0);
