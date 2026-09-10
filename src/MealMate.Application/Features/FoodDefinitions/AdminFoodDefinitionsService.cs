using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FoodDefinitions;

public sealed class AdminFoodDefinitionsService : IAdminFoodDefinitionsService
{
    private readonly IAppDbContext _db;

    public AdminFoodDefinitionsService(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<AllergenDefinition>>> GetAllergensAsync(CancellationToken ct = default)
    {
        var list = await _db.AllergenDefinitions.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<AllergenDefinition>>(list);
    }

    public async Task<Result<AllergenDefinition>> SaveAllergenAsync(
        string code,
        FoodDefinitionRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return Error.Validation("food_definition.names_required", "Names are required.");

        var normalized = FoodCodeNormalizer.Normalize(code);
        var entity = await _db.AllergenDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Code == normalized, ct);

        if (entity is null)
        {
            entity = new AllergenDefinition { Code = normalized };
            _db.AllergenDefinitions.Add(entity);
        }

        entity.NameAr = request.NameAr.Trim();
        entity.NameEn = request.NameEn.Trim();
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        await _db.SaveChangesAsync(ct);
        return Result.Success(entity);
    }

    public async Task<Result<IReadOnlyList<FoodPreferenceDefinition>>> GetDietaryPreferencesAsync(CancellationToken ct = default)
    {
        var list = await _db.FoodPreferenceDefinitions.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<FoodPreferenceDefinition>>(list);
    }

    public async Task<Result<FoodPreferenceDefinition>> SaveDietaryPreferenceAsync(
        string code,
        FoodDefinitionRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return Error.Validation("food_definition.names_required", "Names are required.");

        var normalized = FoodCodeNormalizer.Normalize(code);
        var entity = await _db.FoodPreferenceDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Code == normalized, ct);

        if (entity is null)
        {
            entity = new FoodPreferenceDefinition { Code = normalized };
            _db.FoodPreferenceDefinitions.Add(entity);
        }

        entity.NameAr = request.NameAr.Trim();
        entity.NameEn = request.NameEn.Trim();
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        await _db.SaveChangesAsync(ct);
        return Result.Success(entity);
    }
}
