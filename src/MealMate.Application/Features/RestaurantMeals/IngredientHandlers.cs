using FluentValidation;
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
    }
}

// Handlers
public sealed class IngredientHandlers :
    ICommandHandler<CreateIngredientCommand, IngredientDto>,
    ICommandHandler<BulkCreateIngredientsCommand, int>,
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
            AllergenCodes = request.AllergenCodes?.Trim(),
            Category = request.Category?.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(ingredient);
    }

    public async Task<Result<IngredientDto>> Handle(UpdateIngredientCommand request, CancellationToken cancellationToken)
    {
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
        ingredient.AllergenCodes = request.AllergenCodes?.Trim();
        ingredient.Category = request.Category?.Trim().ToLowerInvariant();
        ingredient.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(ingredient);
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

        return MapToDto(ingredient);
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

        return MapToDto(ingredient);
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

        var dtos = items.Select(MapToDto).ToList();
        return new PagedResult<IngredientDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<Result<int>> Handle(BulkCreateIngredientsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            return 0;
        }

        var existingIngredients = await _db.Ingredients
            .IgnoreQueryFilters()
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToDictionaryAsync(x => x.Code.ToUpper(), cancellationToken);

        var processedCodes = new HashSet<string>();
        var affectedCount = 0;

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.NameAr) || string.IsNullOrWhiteSpace(item.NameEn))
                continue;

            var code = item.Code.Trim().ToUpperInvariant();
            if (processedCodes.Contains(code))
                continue;

            processedCodes.Add(code);

            if (existingIngredients.TryGetValue(code, out var existing))
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
                existing.AllergenCodes = item.AllergenCodes?.Trim();
                if (!string.IsNullOrWhiteSpace(item.Category))
                    existing.Category = item.Category.Trim().ToLowerInvariant();
                existing.IsActive = true;
                existing.IsDeleted = false;
                existing.DeletedAtUtc = null;
                existing.ModifiedAtUtc = DateTime.UtcNow;
                affectedCount++;
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
                    AllergenCodes = item.AllergenCodes?.Trim(),
                    Category = item.Category?.Trim().ToLowerInvariant(),
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.Ingredients.Add(ingredient);
                existingIngredients[code] = ingredient;
                affectedCount++;
            }
        }

        if (affectedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return affectedCount;
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
            return Error.NotFound("errors.ingredient.not_found", "المكون غير موجود");
        }

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IngredientDto MapToDto(Ingredient i) =>
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
            i.DefaultWeightGrams);
}
