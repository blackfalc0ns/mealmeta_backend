namespace MealMate.Application.Features.FoodDefinitions;

public sealed record FoodDefinitionRequest(
    string NameAr,
    string NameEn,
    bool IsActive = true,
    int SortOrder = 0);
