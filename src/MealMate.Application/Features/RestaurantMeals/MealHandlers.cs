using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantMeals;

// Validators
public sealed class CreateMealCommandValidator : AbstractValidator<CreateMealCommand>
{
    public CreateMealCommandValidator()
    {
        RuleFor(x => x.MenuId).NotEmpty();
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BundleId).NotEmpty();
        RuleFor(x => x.ServingWeightG).GreaterThan(0).When(x => x.ServingWeightG.HasValue);
        RuleFor(x => x.FiberG).GreaterThanOrEqualTo(0).When(x => x.FiberG.HasValue);
        RuleFor(x => x.NetCarbsG).GreaterThanOrEqualTo(0).When(x => x.NetCarbsG.HasValue);
        RuleFor(x => x.SugarAlcoholG).GreaterThanOrEqualTo(0).When(x => x.SugarAlcoholG.HasValue);
        RuleFor(x => x.SaturatedFatG).GreaterThanOrEqualTo(0).When(x => x.SaturatedFatG.HasValue);
        RuleFor(x => x.TotalSugarsG).GreaterThanOrEqualTo(0).When(x => x.TotalSugarsG.HasValue);
        RuleFor(x => x.AddedSugarsG).GreaterThanOrEqualTo(0).When(x => x.AddedSugarsG.HasValue);
        RuleFor(x => x.SodiumMg).GreaterThanOrEqualTo(0).When(x => x.SodiumMg.HasValue);
        RuleFor(x => x.AddedSugarsG).LessThanOrEqualTo(x => x.TotalSugarsG!.Value).When(x => x.AddedSugarsG.HasValue && x.TotalSugarsG.HasValue);
        RuleFor(x => x.NetCarbsG).LessThanOrEqualTo(x => x.CarbsG!.Value).When(x => x.NetCarbsG.HasValue && x.CarbsG.HasValue);
        RuleFor(x => x.RestaurantNotes).MaximumLength(2000).When(x => x.RestaurantNotes is not null);
        RuleFor(x => x.DescriptionAr).MaximumLength(2000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(2000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.ServingWeightG).GreaterThan(0).When(x => x.ServingWeightG.HasValue);
        RuleFor(x => x.FiberG).GreaterThanOrEqualTo(0).When(x => x.FiberG.HasValue);
        RuleFor(x => x.NetCarbsG).GreaterThanOrEqualTo(0).When(x => x.NetCarbsG.HasValue);
        RuleFor(x => x.SugarAlcoholG).GreaterThanOrEqualTo(0).When(x => x.SugarAlcoholG.HasValue);
        RuleFor(x => x.SaturatedFatG).GreaterThanOrEqualTo(0).When(x => x.SaturatedFatG.HasValue);
        RuleFor(x => x.TotalSugarsG).GreaterThanOrEqualTo(0).When(x => x.TotalSugarsG.HasValue);
        RuleFor(x => x.AddedSugarsG).GreaterThanOrEqualTo(0).When(x => x.AddedSugarsG.HasValue);
        RuleFor(x => x.SodiumMg).GreaterThanOrEqualTo(0).When(x => x.SodiumMg.HasValue);
        RuleFor(x => x.AddedSugarsG).LessThanOrEqualTo(x => x.TotalSugarsG!.Value).When(x => x.AddedSugarsG.HasValue && x.TotalSugarsG.HasValue);
        RuleFor(x => x.NetCarbsG).LessThanOrEqualTo(x => x.CarbsG!.Value).When(x => x.NetCarbsG.HasValue && x.CarbsG.HasValue);
    }
}

public sealed class UpdateMealCommandValidator : AbstractValidator<UpdateMealCommand>
{
    public UpdateMealCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionAr).MaximumLength(2000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(2000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.ServingWeightG).GreaterThan(0).When(x => x.ServingWeightG.HasValue);
        RuleFor(x => x.FiberG).GreaterThanOrEqualTo(0).When(x => x.FiberG.HasValue);
        RuleFor(x => x.NetCarbsG).GreaterThanOrEqualTo(0).When(x => x.NetCarbsG.HasValue);
        RuleFor(x => x.SugarAlcoholG).GreaterThanOrEqualTo(0).When(x => x.SugarAlcoholG.HasValue);
        RuleFor(x => x.SaturatedFatG).GreaterThanOrEqualTo(0).When(x => x.SaturatedFatG.HasValue);
        RuleFor(x => x.TotalSugarsG).GreaterThanOrEqualTo(0).When(x => x.TotalSugarsG.HasValue);
        RuleFor(x => x.AddedSugarsG).GreaterThanOrEqualTo(0).When(x => x.AddedSugarsG.HasValue);
        RuleFor(x => x.SodiumMg).GreaterThanOrEqualTo(0).When(x => x.SodiumMg.HasValue);
        RuleFor(x => x.AddedSugarsG).LessThanOrEqualTo(x => x.TotalSugarsG!.Value).When(x => x.AddedSugarsG.HasValue && x.TotalSugarsG.HasValue);
        RuleFor(x => x.NetCarbsG).LessThanOrEqualTo(x => x.CarbsG!.Value).When(x => x.NetCarbsG.HasValue && x.CarbsG.HasValue);
        RuleFor(x => x.RestaurantNotes).MaximumLength(2000).When(x => x.RestaurantNotes is not null);
    }
}

public sealed class AssignMealProgramCommandValidator : AbstractValidator<AssignMealProgramCommand>
{
    public AssignMealProgramCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.ProgramId).NotEmpty();
    }
}

public sealed class ApproveMealCommandValidator : AbstractValidator<ApproveMealCommand>
{
    public ApproveMealCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
    }
}

public sealed class RejectMealCommandValidator : AbstractValidator<RejectMealCommand>
{
    public RejectMealCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
        RuleFor(x => x.RejectionReason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class LinkMealIngredientCommandValidator : AbstractValidator<LinkMealIngredientCommand>
{
    public LinkMealIngredientCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
        RuleFor(x => x.IngredientId).NotEmpty();
        RuleFor(x => x.PortionGrams).GreaterThan(0);
    }
}

public sealed class AddMealImageCommandValidator : AbstractValidator<AddMealImageCommand>
{
    public AddMealImageCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
        RuleFor(x => x.ImageUrl).NotEmpty().MaximumLength(2048);
    }
}

// Handlers
public sealed class MealHandlers :
    ICommandHandler<CreateMealCommand, MealDto>,
    ICommandHandler<BulkCreateMealsCommand, int>,
    ICommandHandler<ClearMealsCommand, int>,
    ICommandHandler<UpdateMealCommand, MealDto>,
    ICommandHandler<ToggleMealAvailabilityCommand, MealDto>,
    ICommandHandler<AssignMealProgramCommand, MealDto>,
    ICommandHandler<LinkMealIngredientCommand, MealDto>,
    ICommandHandler<UnlinkMealIngredientCommand, MealDto>,
    ICommandHandler<AddMealImageCommand, MealDto>,
    ICommandHandler<RemoveMealImageCommand, MealDto>,
    ICommandHandler<ApproveMealCommand, MealDto>,
    ICommandHandler<RejectMealCommand, MealDto>,
    ICommandHandler<ArchiveMealCommand, MealDto>,
    ICommandHandler<UnarchiveMealCommand, MealDto>,
    ICommandHandler<DeleteMealCommand, bool>,
    IQueryHandler<GetMealByIdQuery, MealDto>,
    IQueryHandler<GetMealsListQuery, PagedResult<MealDto>>,
    IQueryHandler<GetAdminMealsListQuery, PagedResult<MealDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser? _currentUser;
    private readonly IDateTimeProvider? _clock;

    public MealHandlers(IAppDbContext db, ICurrentUser? currentUser = null, IDateTimeProvider? clock = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<MealDto>> Handle(CreateMealCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var menu = await _db.Menus.FirstOrDefaultAsync(
            x => x.Id == request.MenuId
                && x.RestaurantId == request.RestaurantId
                && !x.IsDeleted,
            cancellationToken);
        if (menu is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Menu '{request.MenuId}' was not found.");
        }

        if (!menu.ProgramId.HasValue)
        {
            return Error.Validation("meal.program_required", "A program must be selected for the meal.");
        }

        var restaurantHasSelections = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
            x => x.RestaurantId == request.RestaurantId && x.IsActive,
            cancellationToken);
        var isSelectedProgram = restaurantHasSelections
            ? await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == menu.ProgramId.Value
                    && x.IsActive,
                cancellationToken)
            : await _db.Programs.AsNoTracking().AnyAsync(
                x => x.Id == menu.ProgramId.Value && x.IsActive,
                cancellationToken);
        if (!isSelectedProgram)
        {
            return Error.Validation("meal.program_not_selected", "The selected program is not available for this restaurant.");
        }

        var isSelectedBundle = await _db.Bundles.AsNoTracking().AnyAsync(
            x => x.Id == request.BundleId
                && x.ProgramId == menu.ProgramId.Value
                && x.IsActive,
            cancellationToken);
        if (restaurantHasSelections)
        {
            isSelectedBundle = isSelectedBundle && await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == menu.ProgramId.Value
                    && x.BundleId == request.BundleId
                    && x.IsActive,
                cancellationToken);
        }
        if (!isSelectedBundle)
        {
            return Error.Validation("meal.bundle_not_selected", "The selected bundle is not available for this restaurant and program.");
        }

        var codeExists = await _db.Meals.AnyAsync(x => x.RestaurantId == request.RestaurantId && x.Code == code && !x.IsDeleted, cancellationToken);
        if (codeExists)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Meal code '{code}' already exists for this restaurant.");
        }

        var meal = new Meal
        {
            MenuId = request.MenuId,
            Menu = menu,
            RestaurantId = request.RestaurantId,
            BundleId = request.BundleId,
            Code = code,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            Calories = request.Calories,
            ProteinG = request.ProteinG,
            CarbsG = request.CarbsG,
            FatG = request.FatG,
            ServingWeightG = request.ServingWeightG,
            FiberG = request.FiberG,
            NetCarbsG = request.NetCarbsG ?? (request.CarbsG is not null ? Math.Max(0, request.CarbsG.Value) : null),
            SugarAlcoholG = request.SugarAlcoholG,
            SugarAlcoholType = request.SugarAlcoholType?.Trim(),
            SaturatedFatG = request.SaturatedFatG,
            TotalSugarsG = request.TotalSugarsG,
            AddedSugarsG = request.AddedSugarsG,
            SodiumMg = request.SodiumMg,
            NutritionSource = request.NutritionSource?.Trim(),
            AnalysisReference = request.AnalysisReference?.Trim(),
            VerificationDate = request.VerificationDate,
            VerifiedBy = request.VerifiedBy?.Trim(),
            RecipeVersion = request.RecipeVersion?.Trim(),
            RestaurantNotes = request.RestaurantNotes?.Trim(),
            AllergensReviewed = request.AllergensReviewed,
            PresentAllergenCodes = request.PresentAllergenCodes?.Trim(),
            MayContainAllergenCodes = request.MayContainAllergenCodes?.Trim(),
            CrossContactNotes = request.CrossContactNotes?.Trim(),
            DataResponsibilityConfirmed = request.DataResponsibilityConfirmed,
            NutritionTagCodes = request.NutritionTagCodes?.Trim(),
            SearchExclusionTagCodes = request.SearchExclusionTagCodes?.Trim(),
            Slot = request.Slot,
            IsAvailableForNewOrders = true,
            ApprovalStatus = VerificationStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Meals.Add(meal);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(UpdateMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.Id, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.Id}' was not found.");
        }

        meal.NameAr = request.NameAr.Trim();
        meal.NameEn = request.NameEn.Trim();
        if (request.BundleId.HasValue)
        {
            if (!meal.Menu.ProgramId.HasValue)
            {
                return Error.Validation("meal.program_required", "A program must be selected for the meal.");
            }

            var bundleAllowed = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == request.BundleId.Value
                    && x.ProgramId == meal.Menu.ProgramId.Value
                    && x.IsActive,
                cancellationToken);
            if (!bundleAllowed)
            {
                return Error.Validation("meal.bundle_not_selected", "The selected bundle is not available for this restaurant and program.");
            }
            meal.BundleId = request.BundleId.Value;
        }
        meal.DescriptionAr = request.DescriptionAr?.Trim();
        meal.DescriptionEn = request.DescriptionEn?.Trim();
        meal.Calories = request.Calories;
        meal.ProteinG = request.ProteinG;
        meal.CarbsG = request.CarbsG;
        meal.FatG = request.FatG;
        meal.ServingWeightG = request.ServingWeightG;
        meal.FiberG = request.FiberG;
        meal.NetCarbsG = request.NetCarbsG ?? (request.CarbsG is not null ? Math.Max(0, request.CarbsG.Value) : null);
        meal.SugarAlcoholG = request.SugarAlcoholG;
        meal.SugarAlcoholType = request.SugarAlcoholType?.Trim();
        meal.SaturatedFatG = request.SaturatedFatG;
        meal.TotalSugarsG = request.TotalSugarsG;
        meal.AddedSugarsG = request.AddedSugarsG;
        meal.SodiumMg = request.SodiumMg;
        meal.NutritionSource = request.NutritionSource?.Trim();
        meal.AnalysisReference = request.AnalysisReference?.Trim();
        meal.VerificationDate = request.VerificationDate;
        meal.VerifiedBy = request.VerifiedBy?.Trim();
        meal.RecipeVersion = request.RecipeVersion?.Trim();
        meal.RestaurantNotes = request.RestaurantNotes?.Trim();
        meal.AllergensReviewed = request.AllergensReviewed;
        meal.PresentAllergenCodes = request.PresentAllergenCodes?.Trim();
        meal.MayContainAllergenCodes = request.MayContainAllergenCodes?.Trim();
        meal.CrossContactNotes = request.CrossContactNotes?.Trim();
        meal.DataResponsibilityConfirmed = request.DataResponsibilityConfirmed;
        meal.NutritionTagCodes = request.NutritionTagCodes?.Trim();
        meal.SearchExclusionTagCodes = request.SearchExclusionTagCodes?.Trim();
        meal.Slot = request.Slot;
        meal.ApprovalStatus = IsReadyForReview(meal) ? VerificationStatus.Submitted : VerificationStatus.Draft;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(ToggleMealAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.Id, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.Id}' was not found.");
        }

        meal.IsAvailableForNewOrders = !meal.IsAvailableForNewOrders;
        meal.DeletedForNewOrdersAtUtc = meal.IsAvailableForNewOrders ? null : DateTime.UtcNow;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(AssignMealProgramCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null || meal.RestaurantId != request.RestaurantId)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        var restaurantHasSelections = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
            x => x.RestaurantId == request.RestaurantId && x.IsActive,
            cancellationToken);
        var isAllowed = restaurantHasSelections
            ? await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == request.ProgramId
                    && x.IsActive,
                cancellationToken)
            : await _db.Programs.AsNoTracking().AnyAsync(
                x => x.Id == request.ProgramId && x.IsActive,
                cancellationToken);
        if (!isAllowed)
        {
            return Error.Validation("meal.program_not_selected", "The selected program is not available for this restaurant.");
        }

        var targetMenu = await _db.Menus.FirstOrDefaultAsync(
            x => x.RestaurantId == request.RestaurantId
                && x.ProgramId == request.ProgramId
                && !x.IsDeleted,
            cancellationToken);
        if (targetMenu is null)
        {
            var program = await _db.Programs.AsNoTracking().FirstAsync(
                x => x.Id == request.ProgramId,
                cancellationToken);
            targetMenu = new Menu
            {
                RestaurantId = request.RestaurantId,
                ProgramId = program.Id,
                NameAr = program.NameAr,
                NameEn = program.NameEn,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Menus.Add(targetMenu);
        }

        meal.MenuId = targetMenu.Id;
        meal.Menu = targetMenu;
        if (meal.BundleId.HasValue)
        {
            var bundleStillMatches = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == meal.BundleId.Value && x.ProgramId == request.ProgramId && x.IsActive,
                cancellationToken);
            if (!bundleStillMatches)
            {
                meal.BundleId = null;
            }
        }
        meal.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(LinkMealIngredientCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        var ingredient = await _db.Ingredients.FirstOrDefaultAsync(x => x.Id == request.IngredientId && !x.IsDeleted, cancellationToken);
        if (ingredient is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Ingredient '{request.IngredientId}' was not found.");
        }

        var existing = meal.Ingredients.FirstOrDefault(x => x.IngredientId == request.IngredientId && !x.IsDeleted);
        if (existing is not null)
        {
            existing.PortionGrams = request.PortionGrams;
            existing.IsOptional = request.IsOptional;
            existing.SortOrder = request.SortOrder;
            existing.ModifiedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var mealIngredient = new MealIngredient
            {
                MealId = meal.Id,
                IngredientId = request.IngredientId,
                Ingredient = ingredient,
                PortionGrams = request.PortionGrams,
                IsOptional = request.IsOptional,
                SortOrder = request.SortOrder,
                CreatedAtUtc = DateTime.UtcNow
            };

            // The entities use client-generated GUIDs. Adding a new dependent only
            // through a tracked navigation can therefore be interpreted as an
            // existing entity by EF and produce an UPDATE that affects zero rows.
            // Mark the dependent explicitly as Added so it is always inserted.
            _db.MealIngredients.Add(mealIngredient);
        }

        AutoCalculateNutritionalValues(meal);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(UnlinkMealIngredientCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        var existing = meal.Ingredients.FirstOrDefault(x => x.IngredientId == request.IngredientId && !x.IsDeleted);
        if (existing is not null)
        {
            existing.IsDeleted = true;
            existing.DeletedAtUtc = DateTime.UtcNow;
        }

        AutoCalculateNutritionalValues(meal);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(AddMealImageCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        if (request.IsPrimary)
        {
            foreach (var img in meal.Images.Where(x => !x.IsDeleted))
            {
                img.IsPrimary = false;
            }
        }

        var mealImage = new MealImage
        {
            MealId = meal.Id,
            ImageUrl = request.ImageUrl.Trim(),
            IsPrimary = request.IsPrimary || !meal.Images.Any(x => !x.IsDeleted),
            DisplayOrder = request.DisplayOrder,
            AltTextAr = request.AltTextAr?.Trim(),
            AltTextEn = request.AltTextEn?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.MealImages.Add(mealImage);

        meal.ApprovalStatus = IsReadyForReview(meal)
            ? VerificationStatus.Submitted
            : VerificationStatus.Draft;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(RemoveMealImageCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        var image = meal.Images.FirstOrDefault(x => x.Id == request.ImageId && !x.IsDeleted);
        if (image is not null)
        {
            image.IsDeleted = true;
            image.DeletedAtUtc = DateTime.UtcNow;

            if (image.IsPrimary)
            {
                var nextPrimary = meal.Images.FirstOrDefault(x => !x.IsDeleted && x.Id != request.ImageId);
                if (nextPrimary is not null)
                {
                    nextPrimary.IsPrimary = true;
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(GetMealByIdQuery request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.Id, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.Id}' was not found.");
        }

        return MapToDto(meal);
    }

    public async Task<Result<PagedResult<MealDto>>> Handle(GetMealsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Meals
            .AsNoTracking()
            .Include(x => x.Menu)
            .Include(x => x.Ingredients.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.Ingredient)
            .Include(x => x.Images.Where(img => !img.IsDeleted))
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted);

        if (request.MenuId.HasValue)
        {
            query = query.Where(x => x.MenuId == request.MenuId.Value);
        }

        if (request.AvailableOnly == true)
        {
            query = query.Where(x => x.IsAvailableForNewOrders);
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

        var dtos = items.Select(x => MapToDto(x, null, null)).ToList();
        return new PagedResult<MealDto>(dtos, totalCount, page, pageSize);
    }

    private async Task<Meal?> LoadMealWithRelations(Guid mealId, CancellationToken cancellationToken)
    {
        return await _db.Meals
            .Include(x => x.Menu)
            .Include(x => x.Ingredients.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.Ingredient)
            .Include(x => x.Images.Where(img => !img.IsDeleted))
            .FirstOrDefaultAsync(x => x.Id == mealId && !x.IsDeleted, cancellationToken);
    }

    private static void AutoCalculateNutritionalValues(Meal meal)
    {
        var activeIngredients = meal.Ingredients.Where(x => !x.IsDeleted && x.Ingredient is not null).ToList();

        decimal totalCalories = 0, totalProtein = 0, totalCarbs = 0, totalFat = 0;
        bool hasValues = false;

        foreach (var mi in activeIngredients)
        {
            var factor = mi.PortionGrams / 100m;
            if (mi.Ingredient.DefaultCaloriesPer100g.HasValue)
            {
                totalCalories += mi.Ingredient.DefaultCaloriesPer100g.Value * factor;
                hasValues = true;
            }
            if (mi.Ingredient.DefaultProteinPer100g.HasValue)
            {
                totalProtein += mi.Ingredient.DefaultProteinPer100g.Value * factor;
                hasValues = true;
            }
            if (mi.Ingredient.DefaultCarbsPer100g.HasValue)
            {
                totalCarbs += mi.Ingredient.DefaultCarbsPer100g.Value * factor;
                hasValues = true;
            }
            if (mi.Ingredient.DefaultFatPer100g.HasValue)
            {
                totalFat += mi.Ingredient.DefaultFatPer100g.Value * factor;
                hasValues = true;
            }
        }

        if (hasValues)
        {
            meal.Calories = Math.Round(totalCalories, 1);
            meal.ProteinG = Math.Round(totalProtein, 1);
            meal.CarbsG = Math.Round(totalCarbs, 1);
            meal.FatG = Math.Round(totalFat, 1);
            meal.NetCarbsG ??= meal.CarbsG;
        }

        meal.ApprovalStatus = IsReadyForReview(meal)
            ? VerificationStatus.Submitted
            : VerificationStatus.Draft;
    }

    private static bool IsReadyForReview(Meal meal)
    {
        return !string.IsNullOrWhiteSpace(meal.NameAr)
            && !string.IsNullOrWhiteSpace(meal.NameEn)
            && !string.IsNullOrWhiteSpace(meal.DescriptionAr)
            && !string.IsNullOrWhiteSpace(meal.DescriptionEn)
            && meal.Images.Any(x => !x.IsDeleted)
            && meal.Ingredients.Any(x => !x.IsDeleted)
            && meal.Calories is >= 0
            && meal.ProteinG is >= 0
            && meal.CarbsG is >= 0
            && meal.FatG is >= 0
            && meal.AllergensReviewed
            && meal.DataResponsibilityConfirmed;
    }

    public async Task<Result<MealDto>> Handle(ApproveMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        meal.ApprovalStatus = VerificationStatus.Approved;
        meal.RejectionReason = null;
        meal.ReviewedByUserId = _currentUser?.UserId;
        meal.ReviewedAtUtc = _clock?.UtcNow ?? DateTime.UtcNow;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(RejectMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        meal.ApprovalStatus = VerificationStatus.Rejected;
        meal.RejectionReason = request.RejectionReason.Trim();
        meal.ReviewedByUserId = _currentUser?.UserId;
        meal.ReviewedAtUtc = _clock?.UtcNow ?? DateTime.UtcNow;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(ArchiveMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        meal.ApprovalStatus = VerificationStatus.Draft;
        meal.IsAvailableForNewOrders = false;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal);
    }

    public async Task<Result<MealDto>> Handle(UnarchiveMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        meal.ApprovalStatus = VerificationStatus.Submitted;
        meal.IsAvailableForNewOrders = true;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal);
    }

    public async Task<Result<PagedResult<MealDto>>> Handle(GetAdminMealsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Meals
            .Include(x => x.Ingredients.Where(i => !i.IsDeleted)).ThenInclude(x => x.Ingredient)
            .Include(x => x.Images.Where(i => !i.IsDeleted))
            .Where(x => !x.IsDeleted);

        if (request.RestaurantId.HasValue)
        {
            query = query.Where(x => x.RestaurantId == request.RestaurantId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.ApprovalStatus == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(x => x.NameAr.ToLower().Contains(s) || x.NameEn.ToLower().Contains(s) || x.Code.ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var restaurantIds = items.Select(x => x.RestaurantId).Distinct().ToList();
        var restaurants = await _db.RestaurantRegistrations
            .Where(r => restaurantIds.Contains(r.Id) || (r.ResultingRestaurantId.HasValue && restaurantIds.Contains(r.ResultingRestaurantId.Value)))
            .ToListAsync(cancellationToken);

        var restaurantMap = new Dictionary<Guid, (string Ar, string En)>();
        foreach (var r in restaurants)
        {
            var names = (r.TradeNameAr ?? r.LegalNameAr, r.TradeNameEn ?? r.LegalNameEn);
            restaurantMap[r.Id] = names;
            if (r.ResultingRestaurantId.HasValue)
            {
                restaurantMap[r.ResultingRestaurantId.Value] = names;
            }
        }

        var dtos = items.Select(m =>
        {
            var (ar, en) = restaurantMap.TryGetValue(m.RestaurantId, out var names) ? names : (null, null);
            return MapToDto(m, ar, en);
        }).ToList();

        return new PagedResult<MealDto>(dtos, totalCount, page, pageSize);
    }

    public static MealDto MapToDto(Meal m, string? restaurantNameAr = null, string? restaurantNameEn = null)
    {
        var ingredientDtos = m.Ingredients
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .Select(x =>
            {
                var factor = x.PortionGrams / 100m;
                var ing = x.Ingredient;
                return new MealIngredientDto(
                    x.IngredientId,
                    ing?.Code ?? string.Empty,
                    ing?.NameAr ?? string.Empty,
                    ing?.NameEn ?? string.Empty,
                    x.PortionGrams,
                    x.IsOptional,
                    x.SortOrder,
                    ing?.DefaultCaloriesPer100g.HasValue == true ? Math.Round(ing.DefaultCaloriesPer100g.Value * factor, 1) : null,
                    ing?.DefaultProteinPer100g.HasValue == true ? Math.Round(ing.DefaultProteinPer100g.Value * factor, 1) : null,
                    ing?.DefaultCarbsPer100g.HasValue == true ? Math.Round(ing.DefaultCarbsPer100g.Value * factor, 1) : null,
                    ing?.DefaultFatPer100g.HasValue == true ? Math.Round(ing.DefaultFatPer100g.Value * factor, 1) : null);
            })
            .ToList();

        var imageDtos = m.Images
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new MealImageDto(
                x.Id,
                x.ImageUrl,
                x.DisplayOrder,
                x.IsPrimary,
                x.AltTextAr,
                x.AltTextEn))
            .ToList();

        return new MealDto(
            m.Id,
            m.MenuId,
            m.RestaurantId,
            m.Menu?.ProgramId,
            m.BundleId,
            restaurantNameAr,
            restaurantNameEn,
            m.Code,
            m.NameAr,
            m.NameEn,
            m.DescriptionAr,
            m.DescriptionEn,
            m.Slot,
            m.IsAvailableForNewOrders,
            m.ApprovalStatus,
            m.RejectionReason,
            m.ReviewedByUserId,
            m.ReviewedAtUtc,
            m.Calories,
            m.ProteinG,
            m.CarbsG,
            m.FatG,
            m.CreatedAtUtc,
            ingredientDtos,
            imageDtos,
            m.ServingWeightG,
            m.FiberG,
            m.NetCarbsG,
            m.SugarAlcoholG,
            m.SugarAlcoholType,
            m.SaturatedFatG,
            m.TotalSugarsG,
            m.AddedSugarsG,
            m.SodiumMg,
            m.NutritionSource,
            m.AnalysisReference,
            m.VerificationDate,
            m.VerifiedBy,
            m.RecipeVersion,
            m.RestaurantNotes,
            m.AllergensReviewed,
            m.PresentAllergenCodes,
            m.MayContainAllergenCodes,
            m.CrossContactNotes,
            m.DataResponsibilityConfirmed,
            m.NutritionTagCodes,
            m.SearchExclusionTagCodes);
    }

    public async Task<Result<int>> Handle(BulkCreateMealsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            return 0;
        }

        var allowedProgramBundles = await (
            from offer in _db.RestaurantProgramBundleOffers.AsNoTracking()
            join program in _db.Programs.AsNoTracking() on offer.ProgramId equals program.Id
            join bundle in _db.Bundles.AsNoTracking() on offer.BundleId equals bundle.Id
            where offer.RestaurantId == request.RestaurantId
                && offer.IsActive
                && program.IsActive
                && bundle.IsActive
            select new
            {
                ProgramId = program.Id,
                ProgramNameAr = program.NameAr,
                ProgramNameEn = program.NameEn,
                BundleId = bundle.Id,
            })
            .ToListAsync(cancellationToken);

        if (allowedProgramBundles.Count == 0)
        {
            allowedProgramBundles = await (
                from program in _db.Programs.AsNoTracking()
                join bundle in _db.Bundles.AsNoTracking() on program.Id equals bundle.ProgramId
                where program.IsActive && bundle.IsActive
                select new
                {
                    ProgramId = program.Id,
                    ProgramNameAr = program.NameAr,
                    ProgramNameEn = program.NameEn,
                    BundleId = bundle.Id,
                })
                .ToListAsync(cancellationToken);
        }

        var bundlesById = allowedProgramBundles
            .DistinctBy(row => row.BundleId)
            .ToDictionary(row => row.BundleId);

        var menusByProgram = await _db.Menus
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted && x.ProgramId.HasValue)
            .ToDictionaryAsync(x => x.ProgramId!.Value, cancellationToken);

        var availableIngredients = await _db.Ingredients
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted && x.IsActive)
            .ToDictionaryAsync(x => x.Code.ToUpper(), cancellationToken);

        // The import template is backed by the restaurant ingredient catalog. Never
        // silently drop a code: an imported meal must be linked to a real ingredient.
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Ingredients))
                continue;

            foreach (var part in item.Ingredients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                var ingredientCode = trimmed;
                var portionText = (string?)null;
                if (trimmed.Contains(':'))
                {
                    var pair = trimmed.Split(':', 2);
                    ingredientCode = pair[0].Trim();
                    portionText = pair.Length > 1 ? pair[1].Trim().TrimEnd('g', 'G') : null;
                }
                else if (trimmed.Contains('(') && trimmed.Contains(')'))
                {
                    var openIndex = trimmed.IndexOf('(');
                    var closeIndex = trimmed.IndexOf(')', openIndex + 1);
                    if (closeIndex <= openIndex + 1)
                    {
                        return Error.Validation(
                            "meal.ingredient_portion_invalid",
                            $"Ingredient '{trimmed}' has an invalid portion format.");
                    }

                    ingredientCode = trimmed[..openIndex].Trim();
                    portionText = trimmed[(openIndex + 1)..closeIndex].Trim().TrimEnd('g', 'G');
                }

                if (!availableIngredients.ContainsKey(ingredientCode.ToUpperInvariant()))
                {
                    return Error.Validation(
                        "meal.ingredient_not_found",
                        $"Ingredient code '{ingredientCode}' does not exist as an active ingredient for this restaurant.");
                }

                if (portionText is not null && (!decimal.TryParse(portionText, out var portion) || portion <= 0))
                {
                    return Error.Validation(
                        "meal.ingredient_portion_invalid",
                        $"Ingredient '{ingredientCode}' must have a positive portion in grams.");
                }
            }
        }

        var existingMeals = await _db.Meals
            .IgnoreQueryFilters()
            .Include(x => x.Ingredients)
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToDictionaryAsync(x => x.Code.ToUpper(), cancellationToken);

        var existingLinks = await _db.MealIngredients
            .IgnoreQueryFilters()
            .Where(x => _db.Meals
                .IgnoreQueryFilters()
                .Where(m => m.RestaurantId == request.RestaurantId)
                .Select(m => m.Id)
                .Contains(x.MealId))
            .ToListAsync(cancellationToken);

        var linksLookup = existingLinks
            .GroupBy(x => (x.MealId, x.IngredientId))
            .ToDictionary(g => g.Key, g => g.First());

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

            if (!bundlesById.TryGetValue(item.BundleId, out var selectedPair)
                || selectedPair.ProgramId != item.ProgramId)
            {
                return Error.Validation(
                    "meal.bundle_not_selected",
                    "The selected bundle is not available for this restaurant and program.");
            }

            if (!menusByProgram.TryGetValue(item.ProgramId, out var selectedMenu))
            {
                selectedMenu = new Menu
                {
                    RestaurantId = request.RestaurantId,
                    ProgramId = item.ProgramId,
                    NameAr = selectedPair.ProgramNameAr,
                    NameEn = selectedPair.ProgramNameEn,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _db.Menus.Add(selectedMenu);
                menusByProgram[item.ProgramId] = selectedMenu;
            }

            Meal mealTarget;
            if (existingMeals.TryGetValue(code, out var existing))
            {
                existing.MenuId = selectedMenu.Id;
                existing.BundleId = item.BundleId;
                existing.NameAr = item.NameAr.Trim();
                existing.NameEn = item.NameEn.Trim();
                existing.DescriptionAr = item.DescriptionAr?.Trim();
                existing.DescriptionEn = item.DescriptionEn?.Trim();
                existing.Calories = item.Calories;
                existing.ProteinG = item.ProteinG;
                existing.CarbsG = item.CarbsG;
                existing.FatG = item.FatG;
                existing.ServingWeightG = item.ServingWeightG;
                existing.FiberG = item.FiberG;
                existing.NetCarbsG = item.NetCarbsG;
                existing.SugarAlcoholG = item.SugarAlcoholG;
                existing.SugarAlcoholType = item.SugarAlcoholType?.Trim();
                existing.SaturatedFatG = item.SaturatedFatG;
                existing.TotalSugarsG = item.TotalSugarsG;
                existing.AddedSugarsG = item.AddedSugarsG;
                existing.SodiumMg = item.SodiumMg;
                existing.NutritionSource = item.NutritionSource?.Trim();
                existing.AnalysisReference = item.AnalysisReference?.Trim();
                existing.VerificationDate = item.VerificationDate;
                existing.VerifiedBy = item.VerifiedBy?.Trim();
                existing.RecipeVersion = item.RecipeVersion?.Trim();
                existing.RestaurantNotes = item.RestaurantNotes?.Trim();
                existing.AllergensReviewed = item.AllergensReviewed;
                existing.PresentAllergenCodes = item.PresentAllergenCodes?.Trim();
                existing.MayContainAllergenCodes = item.MayContainAllergenCodes?.Trim();
                existing.CrossContactNotes = item.CrossContactNotes?.Trim();
                existing.DataResponsibilityConfirmed = item.DataResponsibilityConfirmed;
                existing.NutritionTagCodes = item.NutritionTagCodes?.Trim();
                existing.SearchExclusionTagCodes = item.SearchExclusionTagCodes?.Trim();
                if (!string.IsNullOrWhiteSpace(item.Slot)) existing.Slot = item.Slot;
                existing.IsAvailableForNewOrders = true;
                existing.IsDeleted = false;
                existing.DeletedAtUtc = null;
                existing.ModifiedAtUtc = DateTime.UtcNow;
                mealTarget = existing;
                affectedCount++;
            }
            else
            {
                var meal = new Meal
                {
                    MenuId = selectedMenu.Id,
                    RestaurantId = request.RestaurantId,
                    BundleId = item.BundleId,
                    Code = code,
                    NameAr = item.NameAr.Trim(),
                    NameEn = item.NameEn.Trim(),
                    DescriptionAr = item.DescriptionAr?.Trim(),
                    DescriptionEn = item.DescriptionEn?.Trim(),
                    Calories = item.Calories,
                    ProteinG = item.ProteinG,
                    CarbsG = item.CarbsG,
                    FatG = item.FatG,
                    ServingWeightG = item.ServingWeightG,
                    FiberG = item.FiberG,
                    NetCarbsG = item.NetCarbsG,
                    SugarAlcoholG = item.SugarAlcoholG,
                    SugarAlcoholType = item.SugarAlcoholType?.Trim(),
                    SaturatedFatG = item.SaturatedFatG,
                    TotalSugarsG = item.TotalSugarsG,
                    AddedSugarsG = item.AddedSugarsG,
                    SodiumMg = item.SodiumMg,
                    NutritionSource = item.NutritionSource?.Trim(),
                    AnalysisReference = item.AnalysisReference?.Trim(),
                    VerificationDate = item.VerificationDate,
                    VerifiedBy = item.VerifiedBy?.Trim(),
                    RecipeVersion = item.RecipeVersion?.Trim(),
                    RestaurantNotes = item.RestaurantNotes?.Trim(),
                    AllergensReviewed = item.AllergensReviewed,
                    PresentAllergenCodes = item.PresentAllergenCodes?.Trim(),
                    MayContainAllergenCodes = item.MayContainAllergenCodes?.Trim(),
                    CrossContactNotes = item.CrossContactNotes?.Trim(),
                    DataResponsibilityConfirmed = item.DataResponsibilityConfirmed,
                    NutritionTagCodes = item.NutritionTagCodes?.Trim(),
                    SearchExclusionTagCodes = item.SearchExclusionTagCodes?.Trim(),
                    Slot = item.Slot,
                    IsAvailableForNewOrders = true,
                    ApprovalStatus = VerificationStatus.Draft,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.Meals.Add(meal);
                existingMeals[code] = meal;
                mealTarget = meal;
                affectedCount++;
            }

            // Link ingredients if provided (format: ING-001:150, ING-002:200)
            if (!string.IsNullOrWhiteSpace(item.Ingredients))
            {
                var parts = item.Ingredients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var sortOrder = 0;
                var processedIngredientIds = new HashSet<Guid>();

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    string ingCodeStr = trimmed;
                    decimal portionVal = 100m; // default portion

                    if (trimmed.Contains(':'))
                    {
                        var pair = trimmed.Split(':');
                        ingCodeStr = pair[0].Trim();
                        var portionRaw = pair[1].Trim().TrimEnd('g', 'G', 'j', 'm');
                        if (decimal.TryParse(portionRaw, out var p) && p > 0)
                            portionVal = p;
                    }
                    else if (trimmed.Contains('(') && trimmed.Contains(')'))
                    {
                        var openIdx = trimmed.IndexOf('(');
                        var closeIdx = trimmed.IndexOf(')');
                        ingCodeStr = trimmed.Substring(0, openIdx).Trim();
                        var portionRaw = trimmed.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim().TrimEnd('g', 'G');
                        if (decimal.TryParse(portionRaw, out var p) && p > 0)
                            portionVal = p;
                    }

                    if (availableIngredients.TryGetValue(ingCodeStr.ToUpperInvariant(), out var ingEntity))
                    {
                        if (processedIngredientIds.Contains(ingEntity.Id))
                            continue;

                        processedIngredientIds.Add(ingEntity.Id);

                        var key = (mealTarget.Id, ingEntity.Id);
                        if (linksLookup.TryGetValue(key, out var existingLink))
                        {
                            existingLink.PortionGrams = portionVal;
                            existingLink.SortOrder = sortOrder++;
                            existingLink.IsDeleted = false;
                            existingLink.DeletedAtUtc = null;
                            existingLink.ModifiedAtUtc = DateTime.UtcNow;
                        }
                        else
                        {
                            var newLink = new MealIngredient
                            {
                                MealId = mealTarget.Id,
                                IngredientId = ingEntity.Id,
                                Ingredient = ingEntity,
                                PortionGrams = portionVal,
                                SortOrder = sortOrder++,
                                CreatedAtUtc = DateTime.UtcNow
                            };
                            mealTarget.Ingredients.Add(newLink);
                            _db.MealIngredients.Add(newLink);
                            linksLookup[key] = newLink;
                        }
                    }
                }

                // Soft-delete old links that were omitted from this upload
                var oldUnusedLinks = existingLinks
                    .Where(x => x.MealId == mealTarget.Id && !processedIngredientIds.Contains(x.IngredientId) && !x.IsDeleted)
                    .ToList();

                foreach (var oldLink in oldUnusedLinks)
                {
                    oldLink.IsDeleted = true;
                    oldLink.DeletedAtUtc = DateTime.UtcNow;
                }
            }

            // Always recalculate nutrition from linked ingredients
            AutoCalculateNutritionalValues(mealTarget);
        }

        if (affectedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return affectedCount;
    }

    public async Task<Result<int>> Handle(ClearMealsCommand request, CancellationToken cancellationToken)
    {
        var meals = await _db.Meals
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var meal in meals)
        {
            meal.IsDeleted = true;
            meal.DeletedAtUtc = DateTime.UtcNow;
        }

        if (meals.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return meals.Count;
    }

    public async Task<Result<bool>> Handle(DeleteMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await _db.Meals
            .FirstOrDefaultAsync(x => x.Id == request.MealId && !x.IsDeleted, cancellationToken);

        if (meal == null)
        {
            return Error.NotFound("errors.meal.not_found", "الوجبة غير موجودة");
        }

        meal.IsDeleted = true;
        meal.DeletedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
