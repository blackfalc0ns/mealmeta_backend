using FluentValidation;
using MealMate.Application.Common;
using MealMate.Application.Common.Storage;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantMeals;

// Validators
public sealed class CreateIngredientCommandValidator : AbstractValidator<CreateIngredientCommand>
{
    public CreateIngredientCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(1000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.DefaultWeightGrams).GreaterThan(0).When(x => x.DefaultWeightGrams.HasValue);
        RuleFor(x => x.DefaultCaloriesPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultCaloriesPer100g.HasValue);
        RuleFor(x => x.DefaultProteinPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultProteinPer100g.HasValue);
        RuleFor(x => x.DefaultCarbsPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultCarbsPer100g.HasValue);
        RuleFor(x => x.DefaultFatPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultFatPer100g.HasValue);
        RuleFor(x => x.OriginCountryCode).Length(2, 3).When(x => !string.IsNullOrWhiteSpace(x.OriginCountryCode));
        RuleFor(x => x.SupplierName).MaximumLength(200).When(x => x.SupplierName is not null);
        RuleFor(x => x.TraceabilityNote).MaximumLength(1000).When(x => x.TraceabilityNote is not null);
        RuleFor(x => x.RestaurantIconUrl).MaximumLength(2048).When(x => x.RestaurantIconUrl is not null);
        RuleFor(x => x.MealIconUrl).MaximumLength(2048).When(x => x.MealIconUrl is not null);
    }
}

public sealed class UpdateIngredientCommandValidator : AbstractValidator<UpdateIngredientCommand>
{
    public UpdateIngredientCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(1000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.DefaultWeightGrams).GreaterThan(0).When(x => x.DefaultWeightGrams.HasValue);
        RuleFor(x => x.DefaultCaloriesPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultCaloriesPer100g.HasValue);
        RuleFor(x => x.DefaultProteinPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultProteinPer100g.HasValue);
        RuleFor(x => x.DefaultCarbsPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultCarbsPer100g.HasValue);
        RuleFor(x => x.DefaultFatPer100g).GreaterThanOrEqualTo(0).When(x => x.DefaultFatPer100g.HasValue);
        RuleFor(x => x.OriginCountryCode).Length(2, 3).When(x => !string.IsNullOrWhiteSpace(x.OriginCountryCode));
        RuleFor(x => x.SupplierName).MaximumLength(200).When(x => x.SupplierName is not null);
        RuleFor(x => x.TraceabilityNote).MaximumLength(1000).When(x => x.TraceabilityNote is not null);
        RuleFor(x => x.RestaurantIconUrl).MaximumLength(2048).When(x => x.RestaurantIconUrl is not null);
        RuleFor(x => x.MealIconUrl).MaximumLength(2048).When(x => x.MealIconUrl is not null);
    }
}

// Handlers
public sealed class IngredientHandlers :
    ICommandHandler<CreateIngredientCommand, IngredientDto>,
    ICommandHandler<BulkCreateIngredientsCommand, BulkCreateIngredientsResult>,
    ICommandHandler<ClearIngredientsCommand, int>,
    ICommandHandler<UpdateIngredientCommand, IngredientDto>,
    ICommandHandler<ToggleIngredientActiveCommand, IngredientDto>,
    ICommandHandler<DeleteIngredientCommand, bool>,
    IQueryHandler<GetIngredientByIdQuery, IngredientDto>,
    IQueryHandler<GetIngredientsListQuery, PagedResult<IngredientDto>>
{
    private readonly IAppDbContext _db;

    public IngredientHandlers(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IngredientDto>> Handle(CreateIngredientCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var allergenError = await ValidateAllergensAsync(FoodCodeNormalizer.Split(request.AllergenCodes), cancellationToken);
        if (allergenError is not null) return allergenError;

        var exists = await _db.Ingredients
            .AnyAsync(x => x.RestaurantId == request.RestaurantId && x.Code == code && !x.IsDeleted, cancellationToken);
        if (exists)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Ingredient code '{code}' already exists for this restaurant.");
        }

        var ingredient = new Ingredient
        {
            RestaurantId = request.RestaurantId,
            Code = code,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            DefaultCaloriesPer100g = request.DefaultCaloriesPer100g,
            DefaultProteinPer100g = request.DefaultProteinPer100g,
            DefaultCarbsPer100g = request.DefaultCarbsPer100g,
            DefaultFatPer100g = request.DefaultFatPer100g,
            DefaultWeightGrams = request.DefaultWeightGrams,
            AllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.AllergenCodes))),
            Category = request.Category?.Trim().ToLowerInvariant(),
            OriginCountryCode = request.OriginCountryCode?.Trim().ToUpperInvariant(),
            SupplierName = request.SupplierName?.Trim(),
            IsLocal = request.IsLocal,
            IsHalalCertified = request.IsHalalCertified,
            TraceabilityNote = request.TraceabilityNote?.Trim(),
            IsOriginVisibleToCustomers = request.IsOriginVisibleToCustomers,
            IsCore = request.IsCore,
            RestaurantIconUrl = request.IsCore ? CatalogImageStorage.Normalize(request.RestaurantIconUrl) : null,
            MealIconUrl = CatalogImageStorage.Normalize(request.MealIconUrl),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Ingredients.Add(ingredient);
        var preferenceError = await SyncPreferencesAsync(ingredient.Id, request.DietaryPreferenceCodes, cancellationToken);
        if (preferenceError is not null) return preferenceError;
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(ingredient, NormalizePreferenceCodes(request.DietaryPreferenceCodes));
    }

    public async Task<Result<IngredientDto>> Handle(UpdateIngredientCommand request, CancellationToken cancellationToken)
    {
        var allergenError = await ValidateAllergensAsync(FoodCodeNormalizer.Split(request.AllergenCodes), cancellationToken);
        if (allergenError is not null) return allergenError;
        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (ingredient is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Ingredient '{request.Id}' was not found.");
        }

        ingredient.NameAr = request.NameAr.Trim();
        ingredient.NameEn = request.NameEn.Trim();
        ingredient.DescriptionAr = request.DescriptionAr?.Trim();
        ingredient.DescriptionEn = request.DescriptionEn?.Trim();
        ingredient.DefaultCaloriesPer100g = request.DefaultCaloriesPer100g;
        ingredient.DefaultProteinPer100g = request.DefaultProteinPer100g;
        ingredient.DefaultCarbsPer100g = request.DefaultCarbsPer100g;
        ingredient.DefaultFatPer100g = request.DefaultFatPer100g;
        ingredient.DefaultWeightGrams = request.DefaultWeightGrams;
        ingredient.AllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.AllergenCodes)));
        ingredient.Category = request.Category?.Trim().ToLowerInvariant();
        ingredient.OriginCountryCode = request.OriginCountryCode?.Trim().ToUpperInvariant();
        ingredient.SupplierName = request.SupplierName?.Trim();
        ingredient.IsLocal = request.IsLocal;
        ingredient.IsHalalCertified = request.IsHalalCertified;
        ingredient.TraceabilityNote = request.TraceabilityNote?.Trim();
        ingredient.IsOriginVisibleToCustomers = request.IsOriginVisibleToCustomers;
        ingredient.IsCore = request.IsCore;
        ingredient.RestaurantIconUrl = request.IsCore ? CatalogImageStorage.Normalize(request.RestaurantIconUrl) : null;
        ingredient.MealIconUrl = CatalogImageStorage.Normalize(request.MealIconUrl);
        ingredient.ModifiedAtUtc = DateTime.UtcNow;
        var preferenceError = await SyncPreferencesAsync(ingredient.Id, request.DietaryPreferenceCodes, cancellationToken);
        if (preferenceError is not null) return preferenceError;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(ingredient, NormalizePreferenceCodes(request.DietaryPreferenceCodes));
    }

    public async Task<Result<IngredientDto>> Handle(ToggleIngredientActiveCommand request, CancellationToken cancellationToken)
    {
        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (ingredient is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Ingredient '{request.Id}' was not found.");
        }

        ingredient.IsActive = !ingredient.IsActive;
        ingredient.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(ingredient, await GetPreferenceCodesAsync(ingredient.Id, cancellationToken));
    }

    public async Task<Result<IngredientDto>> Handle(GetIngredientByIdQuery request, CancellationToken cancellationToken)
    {
        var ingredient = await _db.Ingredients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (ingredient is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Ingredient '{request.Id}' was not found.");
        }

        return MapToDto(ingredient, await GetPreferenceCodesAsync(ingredient.Id, cancellationToken));
    }

    public async Task<Result<PagedResult<IngredientDto>>> Handle(GetIngredientsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Ingredients
            .AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted);

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.NameAr.ToLower().Contains(search) || x.NameEn.ToLower().Contains(search) || x.Code.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        var items = await query
            .OrderBy(x => x.NameAr)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var itemIds = items.Select(x => x.Id).ToList();
        var preferenceLinks = await _db.IngredientFoodPreferences.AsNoTracking().Where(x => itemIds.Contains(x.IngredientId)).ToListAsync(cancellationToken);
        var dtos = items.Select(x => MapToDto(x, preferenceLinks.Where(link => link.IngredientId == x.Id).Select(link => link.PreferenceCode).ToList())).ToList();
        return new PagedResult<IngredientDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<Result<BulkCreateIngredientsResult>> Handle(BulkCreateIngredientsCommand request, CancellationToken cancellationToken)
    {
        var issues = new List<BulkIngredientImportIssue>();
        if (request.Items == null || request.Items.Count == 0)
        {
            return new BulkCreateIngredientsResult(0, 0, 0, issues);
        }

        var allergenCodes = await _db.AllergenDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
        var allergenSet = new HashSet<string>(allergenCodes, StringComparer.OrdinalIgnoreCase);
        var validateAllergens = allergenSet.Count > 0;

        var existingIngredients = await _db.Ingredients
            .IgnoreQueryFilters()
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToDictionaryAsync(x => x.Code.ToUpper(), cancellationToken);

        var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var skippedDuplicate = 0;
        var skippedInvalid = 0;

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.NameAr) || string.IsNullOrWhiteSpace(item.NameEn))
            {
                skippedInvalid++;
                issues.Add(new BulkIngredientImportIssue(item.Code, item.NameAr, "missing_name"));
                continue;
            }

            var code = string.IsNullOrWhiteSpace(item.Code)
                ? NextGeneratedCode(existingIngredients, processedCodes)
                : item.Code.Trim().ToUpperInvariant();

            if (!processedCodes.Add(code))
            {
                skippedDuplicate++;
                issues.Add(new BulkIngredientImportIssue(code, item.NameAr, "duplicate"));
                continue;
            }

            var allergenParts = FoodCodeNormalizer.Split(item.AllergenCodes);
            if (validateAllergens && allergenParts.Count > 0)
            {
                var invalid = allergenParts
                    .Select(FoodCodeNormalizer.Normalize)
                    .Where(x => x.Length > 0 && !allergenSet.Contains(x))
                    .ToList();
                if (invalid.Count > 0)
                {
                    skippedInvalid++;
                    processedCodes.Remove(code);
                    issues.Add(new BulkIngredientImportIssue(code, item.NameAr, "invalid_allergen"));
                    continue;
                }
            }

            if (existingIngredients.TryGetValue(code, out var existing) && !existing.IsDeleted)
            {
                skippedDuplicate++;
                issues.Add(new BulkIngredientImportIssue(code, item.NameAr, "duplicate"));
                continue;
            }

            if (existing is not null)
            {
                existing.NameAr = item.NameAr.Trim();
                existing.NameEn = item.NameEn.Trim();
                existing.DescriptionAr = item.DescriptionAr?.Trim();
                existing.DescriptionEn = item.DescriptionEn?.Trim();
                existing.DefaultCaloriesPer100g = item.DefaultCaloriesPer100g;
                existing.DefaultProteinPer100g = item.DefaultProteinPer100g;
                existing.DefaultCarbsPer100g = item.DefaultCarbsPer100g;
                existing.DefaultFatPer100g = item.DefaultFatPer100g;
                existing.DefaultWeightGrams = item.DefaultWeightGrams;
                existing.AllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.AllergenCodes)));
                existing.OriginCountryCode = item.OriginCountryCode?.Trim().ToUpperInvariant();
                existing.SupplierName = item.SupplierName?.Trim();
                existing.IsLocal = item.IsLocal;
                existing.IsHalalCertified = item.IsHalalCertified;
                existing.TraceabilityNote = item.TraceabilityNote?.Trim();
                existing.IsOriginVisibleToCustomers = item.IsOriginVisibleToCustomers;
                existing.IsCore = item.IsCore;
                existing.RestaurantIconUrl = item.IsCore ? CatalogImageStorage.Normalize(item.RestaurantIconUrl) : null;
                existing.MealIconUrl = CatalogImageStorage.Normalize(item.MealIconUrl);
                if (!string.IsNullOrWhiteSpace(item.Category))
                    existing.Category = item.Category.Trim().ToLowerInvariant();
                existing.IsActive = true;
                existing.IsDeleted = false;
                existing.DeletedAtUtc = null;
                existing.ModifiedAtUtc = DateTime.UtcNow;
                created++;
            }
            else
            {
                var ingredient = new Ingredient
                {
                    RestaurantId = request.RestaurantId,
                    Code = code,
                    NameAr = item.NameAr.Trim(),
                    NameEn = item.NameEn.Trim(),
                    DescriptionAr = item.DescriptionAr?.Trim(),
                    DescriptionEn = item.DescriptionEn?.Trim(),
                    DefaultCaloriesPer100g = item.DefaultCaloriesPer100g,
                    DefaultProteinPer100g = item.DefaultProteinPer100g,
                    DefaultCarbsPer100g = item.DefaultCarbsPer100g,
                    DefaultFatPer100g = item.DefaultFatPer100g,
                    DefaultWeightGrams = item.DefaultWeightGrams,
                    AllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.AllergenCodes))),
                    Category = item.Category?.Trim().ToLowerInvariant(),
                    OriginCountryCode = item.OriginCountryCode?.Trim().ToUpperInvariant(),
                    SupplierName = item.SupplierName?.Trim(),
                    IsLocal = item.IsLocal,
                    IsHalalCertified = item.IsHalalCertified,
                    TraceabilityNote = item.TraceabilityNote?.Trim(),
                    IsOriginVisibleToCustomers = item.IsOriginVisibleToCustomers,
                    IsCore = item.IsCore,
                    RestaurantIconUrl = item.IsCore ? CatalogImageStorage.Normalize(item.RestaurantIconUrl) : null,
                    MealIconUrl = CatalogImageStorage.Normalize(item.MealIconUrl),
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.Ingredients.Add(ingredient);
                existingIngredients[code] = ingredient;
                created++;
            }

            var preferenceError = await SyncPreferencesAsync(existingIngredients[code].Id, item.DietaryPreferenceCodes, cancellationToken);
            if (preferenceError is not null) return preferenceError;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new BulkCreateIngredientsResult(created, skippedDuplicate, skippedInvalid, issues);
    }

    private static string NextGeneratedCode(
        Dictionary<string, Ingredient> existingIngredients,
        HashSet<string> processedCodes)
    {
        while (true)
        {
            var code = $"ING-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            if (!existingIngredients.ContainsKey(code) && !processedCodes.Contains(code))
            {
                return code;
            }
        }
    }

    public async Task<Result<int>> Handle(ClearIngredientsCommand request, CancellationToken cancellationToken)
    {
        var items = await _db.Ingredients
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.IsDeleted = true;
            item.DeletedAtUtc = DateTime.UtcNow;
        }

        if (items.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return items.Count;
    }

    public async Task<Result<bool>> Handle(DeleteIngredientCommand request, CancellationToken cancellationToken)
    {
        var item = await _db.Ingredients
            .FirstOrDefaultAsync(x => x.Id == request.IngredientId && !x.IsDeleted, cancellationToken);

        if (item == null)
        {
            return Error.NotFound(MessageCodes.RestaurantMeals.IngredientNotFound);
        }

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Error?> SyncPreferencesAsync(Guid ingredientId, IReadOnlyList<string>? codes, CancellationToken ct)
    {
        if (codes is null) return null;
        var normalized = codes.Select(FoodCodeNormalizer.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (await _db.FoodPreferenceDefinitions.AsNoTracking().CountAsync(x => normalized.Contains(x.Code) && x.IsActive, ct) != normalized.Count)
            return Error.Validation("ingredient.invalid_dietary_preference", "One or more dietary preference codes are inactive or unknown.");
        await _db.IngredientFoodPreferences.IgnoreQueryFilters().Where(x => x.IngredientId == ingredientId).ExecuteDeleteAsync(ct);
        _db.IngredientFoodPreferences.AddRange(normalized.Select(x => new IngredientFoodPreference { IngredientId = ingredientId, PreferenceCode = x }));
        return null;
    }

    private async Task<Error?> ValidateAllergensAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var normalized = codes.Select(FoodCodeNormalizer.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized.Count == 0) return null;
        return await _db.AllergenDefinitions.AsNoTracking().CountAsync(x => normalized.Contains(x.Code) && x.IsActive, ct) == normalized.Count
            ? null
            : Error.Validation("ingredient.invalid_allergen", "One or more allergen codes are inactive or unknown.");
    }

    private Task<List<string>> GetPreferenceCodesAsync(Guid ingredientId, CancellationToken ct)
        => _db.IngredientFoodPreferences.AsNoTracking().Where(x => x.IngredientId == ingredientId).OrderBy(x => x.PreferenceCode).Select(x => x.PreferenceCode).ToListAsync(ct);

    private static IReadOnlyList<string> NormalizePreferenceCodes(IReadOnlyList<string>? codes)
        => (codes ?? []).Select(FoodCodeNormalizer.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static IngredientDto MapToDto(Ingredient i, IReadOnlyList<string>? dietaryPreferenceCodes = null) =>
        new(
            i.Id,
            i.RestaurantId,
            i.Code,
            i.NameAr,
            i.NameEn,
            i.DescriptionAr,
            i.DescriptionEn,
            i.DefaultCaloriesPer100g,
            i.DefaultProteinPer100g,
            i.DefaultCarbsPer100g,
            i.DefaultFatPer100g,
            i.AllergenCodes,
            i.Category,
            i.IsActive,
            i.CreatedAtUtc,
            i.DefaultWeightGrams,
            i.OriginCountryCode,
            i.SupplierName,
            i.IsLocal,
            i.IsHalalCertified,
            i.TraceabilityNote,
            i.IsOriginVisibleToCustomers,
            dietaryPreferenceCodes,
            i.IsCore,
            CatalogImageStorage.ToContentUrl(i.RestaurantIconUrl),
            CatalogImageStorage.ToContentUrl(i.MealIconUrl));
}
