using FluentValidation;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.MenuCycles;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantMeals;

public static class MealSlotRules
{
    public const string Breakfast = "breakfast";
    public const string Lunch = "lunch";
    public const string Dinner = "dinner";
    public const string Snack = "snack";

    public static bool IsSupportedInput(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Normalize(value) is not null;
    }

    public static string NormalizeOrDefault(string? value)
        => string.IsNullOrWhiteSpace(value) ? Lunch : Normalize(value) ?? Lunch;

    private static string? Normalize(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            Breakfast or "افطار" or "فطار" => Breakfast,
            Lunch or "main" or "غداء" or "غدا" => Lunch,
            Dinner or "salad" or "عشاء" or "عشا" => Dinner,
            Snack or "juice" or "سناك" => Snack,
            _ => null,
        };
    }
}

// Validators
public sealed class CreateMealCommandValidator : AbstractValidator<CreateMealCommand>
{
    public CreateMealCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BundleId).NotEmpty();
        RuleFor(x => x.Slot).NotEmpty().Must(MealSlotRules.IsSupportedInput)
            .WithMessage("Meal slot must be breakfast, lunch, dinner, or snack.");
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
        RuleFor(x => x.Slot).NotEmpty().Must(MealSlotRules.IsSupportedInput)
            .WithMessage("Meal slot must be breakfast, lunch, dinner, or snack.");
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

public sealed class SubmitMealCommandValidator : AbstractValidator<SubmitMealCommand>
{
    public SubmitMealCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
    }
}

public sealed class WithdrawMealCommandValidator : AbstractValidator<WithdrawMealCommand>
{
    public WithdrawMealCommandValidator()
    {
        RuleFor(x => x.MealId).NotEmpty();
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
    ICommandHandler<SubmitMealCommand, MealDto>,
    ICommandHandler<WithdrawMealCommand, MealDto>,
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
        var allergenError = await ValidateAllergensAsync(FoodCodeNormalizer.Split(request.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(request.MayContainAllergenCodes)), cancellationToken);
        if (allergenError is not null) return allergenError;

        Menu? requestedLegacyMenu = null;
        if (request.MenuId.HasValue)
            requestedLegacyMenu = await _db.Menus.FirstOrDefaultAsync(x => x.Id == request.MenuId.Value && x.RestaurantId == request.RestaurantId && !x.IsDeleted, cancellationToken);
        var assignments = (request.ProgramAssignments ?? [])
            .Where(x => x.ProgramId != Guid.Empty)
            .GroupBy(x => x.ProgramId)
            .Select(g => g.First())
            .ToList();
        var programId = request.ProgramId ?? assignments.FirstOrDefault()?.ProgramId ?? requestedLegacyMenu?.ProgramId;
        if (!programId.HasValue)
        {
            return Error.Validation("meal.program_required", "A program must be selected for the meal.");
        }
        if (assignments.Count == 0)
            assignments.Add(new MealProgramAssignmentInput(programId.Value, request.BundleId));
        else if (assignments.All(x => x.ProgramId != programId.Value))
            assignments.Insert(0, new MealProgramAssignmentInput(programId.Value, request.BundleId));

        var restaurantHasSelections = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
            x => x.RestaurantId == request.RestaurantId && x.IsActive,
            cancellationToken);
        var isSelectedProgram = restaurantHasSelections
            ? await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == programId.Value
                    && x.IsActive,
                cancellationToken)
            : await _db.Programs.AsNoTracking().AnyAsync(
                x => x.Id == programId.Value && x.IsActive,
                cancellationToken);
        if (!isSelectedProgram)
        {
            return Error.Validation("meal.program_not_selected", "The selected program is not available for this restaurant.");
        }

        var isSelectedBundle = await _db.Bundles.AsNoTracking().AnyAsync(
            x => x.Id == request.BundleId
                && x.ProgramId == programId.Value
                && x.IsActive,
            cancellationToken);
        if (restaurantHasSelections)
        {
            isSelectedBundle = isSelectedBundle && await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == programId.Value
                    && x.BundleId == request.BundleId
                    && x.IsActive,
                cancellationToken);
        }
        if (!isSelectedBundle)
        {
            return Error.Validation("meal.bundle_not_selected", "The selected bundle is not available for this restaurant and program.");
        }

        var menu = requestedLegacyMenu?.ProgramId == programId ? requestedLegacyMenu : await _db.Menus.FirstOrDefaultAsync(
            x => x.RestaurantId == request.RestaurantId && x.ProgramId == programId && !x.IsDeleted, cancellationToken);
        if (menu is null)
        {
            var program = await _db.Programs.AsNoTracking().FirstAsync(x => x.Id == programId.Value, cancellationToken);
            menu = new Menu { RestaurantId = request.RestaurantId, ProgramId = programId, NameAr = $"وجبات {program.NameAr}", NameEn = $"{program.NameEn} meals", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            _db.Menus.Add(menu);
        }

        var selectedDays = await ValidateEditableMenuDays(request.RestaurantId, request.MenuDayIds, cancellationToken);
        if (selectedDays.IsFailure) return selectedDays.Error;

        var codeExists = await _db.Meals.AnyAsync(x => x.RestaurantId == request.RestaurantId && x.Code == code && !x.IsDeleted, cancellationToken);
        if (codeExists)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Meal code '{code}' already exists for this restaurant.");
        }

        var meal = new Meal
        {
            MenuId = menu.Id,
            Menu = menu,
            RestaurantId = request.RestaurantId,
            ProgramId = programId,
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
            PresentAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.PresentAllergenCodes))),
            MayContainAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.MayContainAllergenCodes))),
            CrossContactNotes = request.CrossContactNotes?.Trim(),
            DataResponsibilityConfirmed = request.DataResponsibilityConfirmed,
            NutritionTagCodes = request.NutritionTagCodes?.Trim(),
            SearchExclusionTagCodes = request.SearchExclusionTagCodes?.Trim(),
            Slot = MealSlotRules.NormalizeOrDefault(request.Slot),
            IsAvailableForNewOrders = true,
            ApprovalStatus = VerificationStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var day in selectedDays.Value)
            meal.MenuDayMeals.Add(new MenuDayMeal { MenuDayId = day.Id, Meal = meal, ProgramId = programId.Value, Slot = meal.Slot!, CreatedAtUtc = DateTime.UtcNow });

        SyncProgramAssignments(meal, assignments);
        await ApplyScheduleAssignments(meal, request.ScheduleAssignments, request.RestaurantId, cancellationToken);

        var nutritionGate = await EvaluateNutritionGateAsync(meal, hardBlockWhenReady: true, cancellationToken);
        if (nutritionGate.IsFailure) return nutritionGate.Error;

        _db.Meals.Add(meal);
        var preferenceError = await SyncPreferencesAsync(meal.Id, request.DietaryPreferenceCodes, cancellationToken);
        if (preferenceError is not null) return preferenceError;
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal, nutritionWarnings: nutritionGate.Value, dietaryPreferenceCodes: NormalizePreferenceCodes(request.DietaryPreferenceCodes));
    }

    public async Task<Result<MealDto>> Handle(UpdateMealCommand request, CancellationToken cancellationToken)
    {
        var allergenError = await ValidateAllergensAsync(FoodCodeNormalizer.Split(request.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(request.MayContainAllergenCodes)), cancellationToken);
        if (allergenError is not null) return allergenError;
        var meal = await LoadMealWithRelations(request.Id, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.Id}' was not found.");
        }

        var programId = request.ProgramId ?? meal.ProgramId ?? meal.Menu?.ProgramId;
        if (!programId.HasValue) return Error.Validation("meal.program_required", "A program must be selected for the meal.");
        var requestedSlot = MealSlotRules.NormalizeOrDefault(request.Slot);
        if ((programId != meal.ProgramId || requestedSlot != meal.Slot) && await HasLockedMenuDayLinks(meal.Id, cancellationToken))
            return Error.Conflict("meal.locked_cycle_assignment", "Program or slot cannot change while the meal is used by a submitted or approved menu cycle version.");

        meal.NameAr = request.NameAr.Trim();
        meal.NameEn = request.NameEn.Trim();
        if (request.BundleId.HasValue)
        {
            var bundleAllowed = await _db.Bundles.AsNoTracking().AnyAsync(
                x => x.Id == request.BundleId.Value
                    && x.ProgramId == programId.Value
                    && x.IsActive,
                cancellationToken);
            if (!bundleAllowed)
            {
                return Error.Validation("meal.bundle_not_selected", "The selected bundle is not available for this restaurant and program.");
            }
            meal.BundleId = request.BundleId.Value;
        }
        else if (meal.BundleId.HasValue && !await _db.Bundles.AsNoTracking().AnyAsync(x => x.Id == meal.BundleId && x.ProgramId == programId, cancellationToken))
            return Error.Validation("meal.bundle_program_mismatch", "The meal bundle must belong to the selected program.");
        meal.ProgramId = programId;
        if (meal.Menu is null || meal.Menu.ProgramId != programId)
        {
            var compatibleMenu = await _db.Menus.FirstOrDefaultAsync(x => x.RestaurantId == meal.RestaurantId && x.ProgramId == programId && !x.IsDeleted, cancellationToken);
            if (compatibleMenu is null)
            {
                var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId.Value, cancellationToken);
                if (program is null)
                    return Error.Validation("meal.program_not_selected", "The selected program is not available for this restaurant.");
                compatibleMenu = new Menu { RestaurantId = meal.RestaurantId, ProgramId = programId, NameAr = $"وجبات {program.NameAr}", NameEn = $"{program.NameEn} meals", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
                _db.Menus.Add(compatibleMenu);
            }
            meal.Menu = compatibleMenu; meal.MenuId = compatibleMenu.Id;
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
        meal.PresentAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.PresentAllergenCodes)));
        meal.MayContainAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(request.MayContainAllergenCodes)));
        meal.CrossContactNotes = request.CrossContactNotes?.Trim();
        meal.DataResponsibilityConfirmed = request.DataResponsibilityConfirmed;
        meal.NutritionTagCodes = request.NutritionTagCodes?.Trim();
        meal.SearchExclusionTagCodes = request.SearchExclusionTagCodes?.Trim();
        meal.Slot = requestedSlot;
        foreach (var link in meal.MenuDayMeals.Where(x => !x.IsDeleted))
        {
            link.ProgramId = programId.Value;
            link.Slot = requestedSlot;
        }
        if (request.MenuDayIds is not null)
        {
            if (await HasLockedMenuDayLinks(meal.Id, cancellationToken))
                return Error.Conflict("meal.locked_cycle_assignment", "Menu assignments used by submitted or approved cycles cannot be replaced; create a future cycle version.");
            var selectedDays = await ValidateEditableMenuDays(meal.RestaurantId, request.MenuDayIds, cancellationToken);
            if (selectedDays.IsFailure) return selectedDays.Error;
            SynchronizeMenuDayLinks(meal, selectedDays.Value);
        }
        if (request.ProgramAssignments is { Count: > 0 })
            SyncProgramAssignments(meal, request.ProgramAssignments);
        else
            SyncProgramAssignments(meal, [new MealProgramAssignmentInput(programId.Value, meal.BundleId)]);
        await ApplyScheduleAssignments(meal, request.ScheduleAssignments, meal.RestaurantId, cancellationToken);
        if (meal.Ingredients.Any(x => !x.IsDeleted && x.Ingredient is not null))
            AutoCalculateNutritionalValues(meal);
        var preferenceError = await SyncPreferencesAsync(meal.Id, request.DietaryPreferenceCodes, cancellationToken);
        if (preferenceError is not null) return preferenceError;
        var nutritionGate = await EvaluateNutritionGateAsync(meal, hardBlockWhenReady: false, cancellationToken);
        if (nutritionGate.IsFailure) return nutritionGate.Error;
        meal.ApprovalStatus = ResolveSaveApprovalStatus(meal, nutritionGate.Value);
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal, nutritionWarnings: nutritionGate.Value, dietaryPreferenceCodes: NormalizePreferenceCodes(request.DietaryPreferenceCodes));
    }

    public async Task<Result<MealDto>> Handle(ToggleMealAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.Id, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.Id}' was not found.");
        }

        if (meal.ApprovalStatus != VerificationStatus.Approved)
        {
            return Error.Validation(
                "meal.not_approved",
                "Only approved meals can be paused or activated.");
        }

        meal.IsAvailableForNewOrders = !meal.IsAvailableForNewOrders;
        meal.DeletedForNewOrdersAtUtc = meal.IsAvailableForNewOrders ? null : DateTime.UtcNow;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
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
        meal.ProgramId = request.ProgramId;
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
        meal.ApprovalStatus = IsReadyForReview(meal) ? VerificationStatus.Submitted : VerificationStatus.Draft;
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
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
        if (ingredient.RestaurantId != meal.RestaurantId || !ingredient.IsActive)
            return Error.Validation("meal.ingredient_restaurant_mismatch", "Every meal ingredient must be active and owned by the same restaurant.");

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
            meal.Ingredients.Add(mealIngredient);
        }

        AutoCalculateNutritionalValues(meal);
        meal.ApprovalStatus = IsReadyForReview(meal) ? VerificationStatus.Submitted : VerificationStatus.Draft;
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
        meal.ApprovalStatus = IsReadyForReview(meal) ? VerificationStatus.Submitted : VerificationStatus.Draft;
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
        meal.Images.Add(mealImage);

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

        meal.ApprovalStatus = IsReadyForReview(meal) ? VerificationStatus.Submitted : VerificationStatus.Draft;

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

        return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
    }

    public async Task<Result<PagedResult<MealDto>>> Handle(GetMealsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Meals
            .AsNoTracking()
            .Include(x => x.Menu)
            .Include(x => x.Ingredients.Where(i => !i.IsDeleted))
                .ThenInclude(i => i.Ingredient)
            .Include(x => x.Images.Where(img => !img.IsDeleted))
            .Include(x => x.MenuDayMeals.Where(link => !link.IsDeleted))
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

        var itemIds = items.Select(x => x.Id).ToList();
        var preferenceLinks = await _db.MealFoodPreferences.AsNoTracking().Where(x => itemIds.Contains(x.MealId)).ToListAsync(cancellationToken);
        var dtos = items.Select(x => MapToDto(x, null, null, dietaryPreferenceCodes: preferenceLinks.Where(link => link.MealId == x.Id).Select(link => link.PreferenceCode).ToList())).ToList();
        return new PagedResult<MealDto>(dtos, totalCount, page, pageSize);
    }

    private async Task<Meal?> LoadMealWithRelations(Guid mealId, CancellationToken cancellationToken)
    {
        var meal = await _db.Meals
            .Include(x => x.Menu)
            .FirstOrDefaultAsync(x => x.Id == mealId && !x.IsDeleted, cancellationToken);
        if (meal is null) return null;

        await _db.MealIngredients
            .Where(x => x.MealId == mealId && !x.IsDeleted)
            .Include(x => x.Ingredient)
            .LoadAsync(cancellationToken);
        await _db.MealImages
            .Where(x => x.MealId == mealId && !x.IsDeleted)
            .LoadAsync(cancellationToken);
        await _db.MenuDayMeals
            .Where(x => x.MealId == mealId)
            .LoadAsync(cancellationToken);
        await _db.MealProgramAssignments
            .Where(x => x.MealId == mealId)
            .LoadAsync(cancellationToken);
        return meal;
    }

    private async Task<Result<IReadOnlyList<MenuDay>>> ValidateEditableMenuDays(
        Guid restaurantId,
        IReadOnlyList<Guid>? menuDayIds,
        CancellationToken cancellationToken)
    {
        var ids = menuDayIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return Result.Success<IReadOnlyList<MenuDay>>([]);
        var editableStatuses = new[] { VerificationStatus.Draft, VerificationStatus.Rejected, VerificationStatus.NeedsChanges, VerificationStatus.ResubmissionRequired };
        var days = await _db.MenuDays
            .Include(x => x.MenuPlanVersion).ThenInclude(x => x.MenuPlan)
            .Where(x => ids.Contains(x.Id)
                && x.MenuPlanVersion.MenuPlan.RestaurantId == restaurantId
                && editableStatuses.Contains(x.MenuPlanVersion.Status))
            .ToListAsync(cancellationToken);
        if (days.Count != ids.Count)
            return Error.Validation("meal.menu_days_invalid", "Every selected menu day must belong to this restaurant and an editable cycle version.");
        return Result.Success<IReadOnlyList<MenuDay>>(days);
    }

    private async Task<bool> HasLockedMenuDayLinks(Guid mealId, CancellationToken cancellationToken)
        => await _db.MenuDayMeals.AsNoTracking().Where(x => x.MealId == mealId)
            .Join(_db.MenuDays.AsNoTracking(), x => x.MenuDayId, x => x.Id, (link, day) => day)
            .Join(_db.MenuPlanVersions.AsNoTracking(), x => x.MenuPlanVersionId, x => x.Id, (day, version) => version)
            .AnyAsync(x => x.Status == VerificationStatus.Submitted || x.Status == VerificationStatus.UnderReview || x.Status == VerificationStatus.Approved, cancellationToken);

    public static void SyncProgramAssignments(Meal meal, IReadOnlyList<MealProgramAssignmentInput> assignments)
    {
        var wanted = assignments.Where(x => x.ProgramId != Guid.Empty).GroupBy(x => x.ProgramId).Select(g => g.First()).ToList();
        var wantedIds = wanted.Select(x => x.ProgramId).ToHashSet();
        foreach (var existing in meal.ProgramAssignments.Where(x => !x.IsDeleted && !wantedIds.Contains(x.ProgramId)))
        {
            existing.IsDeleted = true;
            existing.DeletedAtUtc = DateTime.UtcNow;
        }
        foreach (var input in wanted)
        {
            var row = meal.ProgramAssignments.FirstOrDefault(x => x.ProgramId == input.ProgramId);
            if (row is null)
            {
                meal.ProgramAssignments.Add(new MealProgramAssignment
                {
                    ProgramId = input.ProgramId,
                    BundleId = input.BundleId,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
            else
            {
                row.BundleId = input.BundleId;
                row.IsDeleted = false;
                row.DeletedAtUtc = null;
                row.ModifiedAtUtc = DateTime.UtcNow;
            }
        }
        var primary = wanted.FirstOrDefault();
        if (primary is not null)
        {
            meal.ProgramId = primary.ProgramId;
            meal.BundleId = primary.BundleId ?? meal.BundleId;
        }
    }

    private async Task ApplyScheduleAssignments(
        Meal meal,
        IReadOnlyList<MealScheduleAssignmentInput>? assignments,
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        if (assignments is null || assignments.Count == 0) return;
        var dayIds = assignments.Where(x => x.MenuDayId.HasValue).Select(x => x.MenuDayId!.Value).Distinct().ToList();
        if (dayIds.Count > 0)
        {
            var days = await ValidateEditableMenuDays(restaurantId, dayIds, cancellationToken);
            if (days.IsSuccess)
            {
                foreach (var day in days.Value)
                {
                    var input = assignments.First(x => x.MenuDayId == day.Id);
                    var programId = meal.ProgramId ?? meal.ProgramAssignments.FirstOrDefault(x => !x.IsDeleted)?.ProgramId;
                    if (programId is null) continue;
                    var existing = meal.MenuDayMeals.FirstOrDefault(x => x.MenuDayId == day.Id && !x.IsDeleted);
                    if (existing is null)
                        meal.MenuDayMeals.Add(new MenuDayMeal
                        {
                            MenuDayId = day.Id,
                            Meal = meal,
                            ProgramId = programId.Value,
                            Slot = meal.Slot ?? "lunch",
                            SortOrder = input.SortOrder,
                            IsPrimary = input.IsPrimary,
                            CreatedAtUtc = DateTime.UtcNow,
                        });
                    else
                    {
                        existing.SortOrder = input.SortOrder;
                        existing.IsPrimary = input.IsPrimary;
                        existing.ModifiedAtUtc = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    private static void SynchronizeMenuDayLinks(Meal meal, IReadOnlyList<MenuDay> selectedDays)
    {
        var selectedIds = selectedDays.Select(x => x.Id).ToHashSet();
        foreach (var link in meal.MenuDayMeals.Where(x => !x.IsDeleted && !selectedIds.Contains(x.MenuDayId)))
        {
            link.IsDeleted = true;
            link.DeletedAtUtc = DateTime.UtcNow;
        }
        foreach (var day in selectedDays)
        {
            var existing = meal.MenuDayMeals.FirstOrDefault(link => link.MenuDayId == day.Id);
            if (existing is null)
            {
                meal.MenuDayMeals.Add(new MenuDayMeal { MenuDayId = day.Id, Meal = meal, ProgramId = meal.ProgramId!.Value, Slot = meal.Slot!, CreatedAtUtc = DateTime.UtcNow });
                continue;
            }

            existing.IsDeleted = false;
            existing.DeletedAtUtc = null;
            existing.ProgramId = meal.ProgramId!.Value;
            existing.Slot = meal.Slot!;
            existing.ModifiedAtUtc = DateTime.UtcNow;
        }
        foreach (var link in meal.MenuDayMeals.Where(x => !x.IsDeleted))
        {
            link.ProgramId = meal.ProgramId!.Value;
            link.Slot = meal.Slot!;
        }
    }

    public static void AutoCalculateNutritionalValues(Meal meal)
    {
        var activeIngredients = meal.Ingredients.Where(x => !x.IsDeleted && x.Ingredient is not null).ToList();

        if (activeIngredients.Count == 0)
        {
            meal.ServingWeightG = null;
            meal.Calories = null;
            meal.ProteinG = null;
            meal.CarbsG = null;
            meal.FatG = null;
            meal.NetCarbsG = null;
            meal.PresentAllergenCodes = null;
            meal.ApprovalStatus = IsReadyForReview(meal)
                ? VerificationStatus.Submitted
                : VerificationStatus.Draft;
            return;
        }

        var serving = activeIngredients.Sum(x => x.PortionGrams);
        meal.ServingWeightG = serving > 0 ? serving : null;
        var fromIngredients = EmptyToNull(FoodCodeNormalizer.Join(
            activeIngredients.SelectMany(x => FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes))));
        if (!string.IsNullOrWhiteSpace(fromIngredients))
            meal.PresentAllergenCodes = fromIngredients;
        if (string.IsNullOrWhiteSpace(meal.NutritionSource))
            meal.NutritionSource = "ingredient_recipe";

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
        }

        if (hasValues || meal.CarbsG.HasValue)
        {
            meal.NetCarbsG = Math.Round(
                Math.Max(0, (meal.CarbsG ?? 0) - (meal.FiberG ?? 0) - (meal.SugarAlcoholG ?? 0)),
                1);
        }

        meal.ApprovalStatus = IsReadyForReview(meal)
            ? VerificationStatus.Submitted
            : VerificationStatus.Draft;
    }

    private async Task<Result<IReadOnlyList<string>>> EvaluateNutritionGateAsync(
        Meal meal,
        bool hardBlockWhenReady,
        CancellationToken cancellationToken,
        bool forceHardBlock = false)
    {
        var programId = meal.ProgramId ?? meal.Menu?.ProgramId;
        if (!programId.HasValue)
            return Array.Empty<string>();

        var hasMacroSignal = meal.Calories.HasValue || meal.ProteinG.HasValue || meal.CarbsG.HasValue || meal.FatG.HasValue;
        if (!hasMacroSignal && meal.ApprovalStatus == VerificationStatus.Draft && !forceHardBlock)
            return Array.Empty<string>();

        var category = NutritionMealCategories.FromSlot(meal.Slot);
        var standard = await _db.ProgramNutritionStandards.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProgramId == programId.Value
                    && x.IsActive
                    && x.MealCategory == category,
                cancellationToken);

        if (standard is null)
        {
            // No standards configured yet — do not block restaurants.
            return Array.Empty<string>();
        }

        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == programId.Value, cancellationToken);
        var issues = MealNutritionStandardValidator.Evaluate(
            standard,
            program?.ShortCode ?? program?.Code,
            meal.Calories,
            meal.ProteinG,
            meal.CarbsG,
            meal.NetCarbsG,
            meal.FatG,
            meal.FiberG);

        var mustBlock = forceHardBlock
            || (hardBlockWhenReady && meal.ApprovalStatus is VerificationStatus.Submitted or VerificationStatus.Approved);

        if (issues.Count > 0 && mustBlock)
        {
            return Error.Validation(
                "meal.nutrition_out_of_range",
                string.Join(" ", issues));
        }

        return issues.ToList();
    }

    public static VerificationStatus ResolveSaveApprovalStatus(Meal meal, IReadOnlyList<string>? nutritionIssues = null)
        => IsReadyForReview(meal)
            ? VerificationStatus.Submitted
            : VerificationStatus.Draft;

    private static bool IsReadyForReview(Meal meal)
    {
        return !string.IsNullOrWhiteSpace(meal.NameAr)
            && !string.IsNullOrWhiteSpace(meal.NameEn)
            && !string.IsNullOrWhiteSpace(meal.DescriptionAr)
            && !string.IsNullOrWhiteSpace(meal.DescriptionEn)
            && meal.Images.Any(x => !x.IsDeleted)
            && meal.Ingredients.Any(x => !x.IsDeleted)
            && meal.Ingredients.Where(x => !x.IsDeleted).All(x => x.Ingredient is { IsActive: true } && !string.IsNullOrWhiteSpace(x.Ingredient.OriginCountryCode))
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
        if (!IsReadyForReview(meal))
            return Error.Validation("meal.not_ready_for_approval", "Meal approval requires active same-restaurant ingredients with country of origin, image, complete macros, allergen review and responsibility confirmation.");

        // Admin review is the override. Nutrition mismatches stay as warnings on the DTO
        // and still hide the meal from customer auto-selection until macros fit the program.
        var nutritionGate = await EvaluateNutritionGateAsync(meal, hardBlockWhenReady: false, cancellationToken);
        if (nutritionGate.IsFailure) return nutritionGate.Error;

        meal.ApprovalStatus = VerificationStatus.Approved;
        meal.RejectionReason = null;
        meal.ReviewedByUserId = _currentUser?.UserId;
        meal.ReviewedAtUtc = _clock?.UtcNow ?? DateTime.UtcNow;
        meal.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal, nutritionWarnings: nutritionGate.Value);
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

    public async Task<Result<MealDto>> Handle(SubmitMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        if (meal.ApprovalStatus == VerificationStatus.Approved)
        {
            return Error.Conflict("meal.already_approved", "Approved meals cannot be submitted again.");
        }

        if (meal.ApprovalStatus is VerificationStatus.Submitted or VerificationStatus.UnderReview)
        {
            return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
        }

        if (!IsReadyForReview(meal))
        {
            return Error.Validation(
                "meal.not_ready_for_approval",
                "Meal approval requires active same-restaurant ingredients with country of origin, image, complete macros, allergen review and responsibility confirmation.");
        }

        meal.ApprovalStatus = VerificationStatus.Submitted;
        meal.RejectionReason = null;
        meal.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
    }

    public async Task<Result<MealDto>> Handle(WithdrawMealCommand request, CancellationToken cancellationToken)
    {
        var meal = await LoadMealWithRelations(request.MealId, cancellationToken);
        if (meal is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Meal '{request.MealId}' was not found.");
        }

        if (meal.ApprovalStatus is not (
            VerificationStatus.Submitted
            or VerificationStatus.UnderReview
            or VerificationStatus.NeedsChanges
            or VerificationStatus.ResubmissionRequired))
        {
            return Error.Conflict("meal.cannot_withdraw", "Only meals awaiting approval can be withdrawn to draft.");
        }

        meal.ApprovalStatus = VerificationStatus.Draft;
        meal.ModifiedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(meal, dietaryPreferenceCodes: await GetPreferenceCodesAsync(meal.Id, cancellationToken));
    }

    public async Task<Result<PagedResult<MealDto>>> Handle(GetAdminMealsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Meals
            .Include(x => x.Ingredients.Where(i => !i.IsDeleted)).ThenInclude(x => x.Ingredient)
            .Include(x => x.Images.Where(i => !i.IsDeleted))
            .Include(x => x.MenuDayMeals.Where(link => !link.IsDeleted))
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

        var adminMealIds = items.Select(x => x.Id).ToList();
        var preferenceLinksByMeal = (await _db.MealFoodPreferences.AsNoTracking().Where(x => adminMealIds.Contains(x.MealId)).ToListAsync(cancellationToken))
            .GroupBy(x => x.MealId).ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(link => link.PreferenceCode).OrderBy(code => code).ToList());

        var dtos = items.Select(m =>
        {
            var (ar, en) = restaurantMap.TryGetValue(m.RestaurantId, out var names) ? names : (null, null);
            return MapToDto(m, ar, en, dietaryPreferenceCodes: preferenceLinksByMeal.GetValueOrDefault(m.Id, []));
        }).ToList();

        return new PagedResult<MealDto>(dtos, totalCount, page, pageSize);
    }

    public static MealDto MapToDto(
        Meal m,
        string? restaurantNameAr = null,
        string? restaurantNameEn = null,
        IReadOnlyList<string>? nutritionWarnings = null,
        IReadOnlyList<string>? dietaryPreferenceCodes = null)
    {
        var ingredientDtos = (m.Ingredients ?? [])
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

        var imageDtos = (m.Images ?? [])
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
            m.ProgramId ?? m.Menu?.ProgramId,
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
            m.SearchExclusionTagCodes,
            (m.MenuDayMeals ?? []).Where(x => !x.IsDeleted).Select(x => x.MenuDayId).Distinct().ToList(),
            nutritionWarnings,
            dietaryPreferenceCodes);
    }

    public async Task<Result<int>> Handle(BulkCreateMealsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            return 0;
        }
        var allergenError = await ValidateAllergensAsync(request.Items.SelectMany(x => FoodCodeNormalizer.Split(x.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(x.MayContainAllergenCodes))), cancellationToken);
        if (allergenError is not null) return allergenError;

        if (request.Items.Any(item => !MealSlotRules.IsSupportedInput(item.Slot)))
        {
            return Error.Validation(
                "meal.slot_invalid",
                "Meal slot must be breakfast, lunch, dinner, or snack.");
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
                existing.ProgramId = item.ProgramId;
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
                existing.PresentAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.PresentAllergenCodes)));
                existing.MayContainAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.MayContainAllergenCodes)));
                existing.CrossContactNotes = item.CrossContactNotes?.Trim();
                existing.DataResponsibilityConfirmed = item.DataResponsibilityConfirmed;
                existing.NutritionTagCodes = item.NutritionTagCodes?.Trim();
                existing.SearchExclusionTagCodes = item.SearchExclusionTagCodes?.Trim();
                existing.Slot = MealSlotRules.NormalizeOrDefault(item.Slot);
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
                    ProgramId = item.ProgramId,
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
                    PresentAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.PresentAllergenCodes))),
                    MayContainAllergenCodes = EmptyToNull(FoodCodeNormalizer.Join(FoodCodeNormalizer.Split(item.MayContainAllergenCodes))),
                    CrossContactNotes = item.CrossContactNotes?.Trim(),
                    DataResponsibilityConfirmed = item.DataResponsibilityConfirmed,
                    NutritionTagCodes = item.NutritionTagCodes?.Trim(),
                    SearchExclusionTagCodes = item.SearchExclusionTagCodes?.Trim(),
                    Slot = MealSlotRules.NormalizeOrDefault(item.Slot),
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
            var preferenceError = await SyncPreferencesAsync(mealTarget.Id, item.DietaryPreferenceCodes, cancellationToken);
            if (preferenceError is not null) return preferenceError;
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

    private async Task<Error?> SyncPreferencesAsync(Guid mealId, IReadOnlyList<string>? codes, CancellationToken ct)
    {
        if (codes is null) return null;
        var normalized = codes.Select(FoodCodeNormalizer.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (await _db.FoodPreferenceDefinitions.AsNoTracking().CountAsync(x => normalized.Contains(x.Code) && x.IsActive, ct) != normalized.Count)
            return Error.Validation("meal.invalid_dietary_preference", "One or more dietary preference codes are inactive or unknown.");
        await _db.MealFoodPreferences.IgnoreQueryFilters().Where(x => x.MealId == mealId).ExecuteDeleteAsync(ct);
        _db.MealFoodPreferences.AddRange(normalized.Select(x => new MealFoodPreference { MealId = mealId, PreferenceCode = x }));
        return null;
    }

    private async Task<Error?> ValidateAllergensAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var normalized = codes.Select(FoodCodeNormalizer.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized.Count == 0) return null;
        return await _db.AllergenDefinitions.AsNoTracking().CountAsync(x => normalized.Contains(x.Code) && x.IsActive, ct) == normalized.Count
            ? null
            : Error.Validation("meal.invalid_allergen", "One or more allergen codes are inactive or unknown.");
    }

    private Task<List<string>> GetPreferenceCodesAsync(Guid mealId, CancellationToken ct)
        => _db.MealFoodPreferences.AsNoTracking().Where(x => x.MealId == mealId).OrderBy(x => x.PreferenceCode).Select(x => x.PreferenceCode).ToListAsync(ct);

    private static IReadOnlyList<string> NormalizePreferenceCodes(IReadOnlyList<string>? codes)
        => (codes ?? []).Select(FoodCodeNormalizer.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
