using MealMate.Application.Common.Models;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.FoodDefinitions;

public interface IAdminFoodDefinitionsService
{
    Task<Result<IReadOnlyList<AllergenDefinition>>> GetAllergensAsync(CancellationToken ct = default);
    Task<Result<AllergenDefinition>> SaveAllergenAsync(string code, FoodDefinitionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<FoodPreferenceDefinition>>> GetDietaryPreferencesAsync(CancellationToken ct = default);
    Task<Result<FoodPreferenceDefinition>> SaveDietaryPreferenceAsync(string code, FoodDefinitionRequest request, CancellationToken ct = default);
}
