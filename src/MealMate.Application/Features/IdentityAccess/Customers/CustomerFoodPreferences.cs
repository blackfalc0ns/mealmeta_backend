using FluentValidation;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

[System.Text.Json.Serialization.JsonConverter(typeof(FoodPreferenceItemJsonConverter))]
public sealed record FoodPreferenceItemDto(string Code, string? NameAr, string? NameEn, string? Severity);
public sealed record DietaryPreferenceOptionDto(string Code, string NameAr, string NameEn, bool IsSelected = false, bool IsNone = false);

public sealed record CustomerFoodPreferencesDto(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto> Allergies,
    IReadOnlyList<FoodPreferenceItemDto> DislikedFoods,
    string? Notes,
    IReadOnlyList<string>? DietaryPreferenceCodes = null);

public sealed record FoodPreferenceIngredientOptionDto(string Code, string NameAr, string NameEn, string? Category, IReadOnlyList<string> AllergenCodes, bool IsSelected = false, bool IsNone = false);
public sealed record FoodPreferenceAllergenOptionDto(string Code, string? NameAr, string? NameEn, bool IsSelected = false, string? Severity = null, bool IsNone = false);
public sealed record CustomerFoodPreferenceCatalogDto(
    IReadOnlyList<FoodPreferenceIngredientOptionDto> Ingredients,
    IReadOnlyList<FoodPreferenceAllergenOptionDto> Allergens,
    int SelectedAllergenCount = 0,
    IReadOnlyList<DietaryPreferenceOptionDto>? DietaryPreferences = null);
public sealed record CustomerAllergiesScreenDto(
    IReadOnlyList<FoodPreferenceAllergenOptionDto> Allergens,
    int SelectedCount,
    int Page,
    int PageSize,
    int TotalCount)
{
    public bool HasNoAllergies => SelectedCount == 0;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}
public sealed record CustomerDislikedIngredientsScreenDto(
    IReadOnlyList<FoodPreferenceIngredientOptionDto> Ingredients,
    int SelectedCount,
    int Page,
    int PageSize,
    int TotalCount)
{
    public bool EatsEverything => SelectedCount == 0;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}
public sealed record CustomerDietaryPreferencesScreenDto(
    IReadOnlyList<DietaryPreferenceOptionDto> DietaryPreferences,
    int SelectedCount,
    int Page,
    int PageSize,
    int TotalCount)
{
    public bool HasNoDietaryRestriction => SelectedCount == 0;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}

public sealed record GetCustomerFoodPreferencesQuery : IQuery<CustomerFoodPreferencesDto>;
public sealed record GetCustomerFoodPreferenceCatalogQuery : IQuery<CustomerFoodPreferenceCatalogDto>;
public sealed record GetCustomerAllergiesQuery(int Page = 1, int PageSize = 20, string? Search = null) : IQuery<CustomerAllergiesScreenDto>;
public sealed record GetCustomerDislikedIngredientsQuery(int Page = 1, int PageSize = 20, string? Search = null) : IQuery<CustomerDislikedIngredientsScreenDto>;
public sealed record GetCustomerDietaryPreferencesQuery(int Page = 1, int PageSize = 20, string? Search = null) : IQuery<CustomerDietaryPreferencesScreenDto>;
public sealed record SaveCustomerFoodPreferencesCommand(string? DietaryPattern, IReadOnlyList<FoodPreferenceItemDto> Allergies, IReadOnlyList<FoodPreferenceItemDto> DislikedFoods, string? Notes, IReadOnlyList<string>? DietaryPreferenceCodes = null) : ICommand<CustomerFoodPreferencesDto>;
public sealed record SaveCustomerAllergiesCommand(IReadOnlyList<FoodPreferenceItemDto> Allergies) : ICommand<CustomerFoodPreferencesDto>;
public sealed record SaveCustomerDietaryPreferencesCommand(IReadOnlyList<string> DietaryPreferenceCodes) : ICommand<CustomerFoodPreferencesDto>;
public sealed record SaveCustomerDislikedIngredientsCommand(IReadOnlyList<string> IngredientCodes) : ICommand<CustomerFoodPreferencesDto>;

public sealed class SaveCustomerFoodPreferencesCommandValidator : AbstractValidator<SaveCustomerFoodPreferencesCommand>
{
    public SaveCustomerFoodPreferencesCommandValidator()
    {
        RuleFor(x => x.DietaryPattern).MaximumLength(80).When(x => x.DietaryPattern is not null);
        RuleFor(x => x.Notes).MaximumLength(1000).When(x => x.Notes is not null);
        RuleForEach(x => x.Allergies).SetValidator(new FoodPreferenceItemValidator());
        RuleForEach(x => x.DislikedFoods).SetValidator(new FoodPreferenceItemValidator());
        RuleForEach(x => x.DietaryPreferenceCodes!).NotEmpty().MaximumLength(50).When(x => x.DietaryPreferenceCodes is not null);
    }
}

public sealed class FoodPreferenceItemValidator : AbstractValidator<FoodPreferenceItemDto>
{
    public FoodPreferenceItemValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(60);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.NameEn).MaximumLength(200).When(x => x.NameEn is not null);
        RuleFor(x => x.Severity).MaximumLength(50).When(x => x.Severity is not null);
    }
}

public sealed class SaveCustomerAllergiesCommandValidator : AbstractValidator<SaveCustomerAllergiesCommand>
{
    public SaveCustomerAllergiesCommandValidator() => RuleForEach(x => x.Allergies).SetValidator(new FoodPreferenceItemValidator());
}

public sealed class CustomerFoodPreferencesHandlers :
    IQueryHandler<GetCustomerFoodPreferencesQuery, CustomerFoodPreferencesDto>,
    IQueryHandler<GetCustomerFoodPreferenceCatalogQuery, CustomerFoodPreferenceCatalogDto>,
    IQueryHandler<GetCustomerAllergiesQuery, CustomerAllergiesScreenDto>,
    IQueryHandler<GetCustomerDislikedIngredientsQuery, CustomerDislikedIngredientsScreenDto>,
    IQueryHandler<GetCustomerDietaryPreferencesQuery, CustomerDietaryPreferencesScreenDto>,
    ICommandHandler<SaveCustomerFoodPreferencesCommand, CustomerFoodPreferencesDto>,
    ICommandHandler<SaveCustomerAllergiesCommand, CustomerFoodPreferencesDto>,
    ICommandHandler<SaveCustomerDietaryPreferencesCommand, CustomerFoodPreferencesDto>,
    ICommandHandler<SaveCustomerDislikedIngredientsCommand, CustomerFoodPreferencesDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CustomerFoodPreferencesHandlers(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, IDateTimeProvider clock)
        => (_db, _currentUser, _audit, _clock) = (db, currentUser, audit, clock);

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(GetCustomerFoodPreferencesQuery request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        if (await ProfileAsync(userId.Value, ct) is null) return NotFound();
        return await ReadAsync(userId.Value, ct);
    }

    public async Task<Result<CustomerFoodPreferenceCatalogDto>> Handle(GetCustomerFoodPreferenceCatalogQuery request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var approvedRestaurants = _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
            .Select(x => x.Id);
        var ingredients = await _db.Ingredients.AsNoTracking()
            .Where(x => x.IsActive && approvedRestaurants.Contains(x.RestaurantId))
            .OrderBy(x => x.NameAr)
            .Select(x => new { x.Id, x.NameAr, x.NameEn, x.Category, x.AllergenCodes })
            .ToListAsync(ct);
        var selected = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId.Value).ToListAsync(ct);
        var selectedDietary = await _db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId.Value).Select(x => x.PreferenceCode).ToListAsync(ct);
        var dislikedCodes = selected.Where(x => x.IsDislike).Select(x => x.AllergenCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedAllergyCodes = selected.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ingredientAllergenCodes = ingredients.SelectMany(x => FoodCodeNormalizer.Split(x.AllergenCodes)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = await _db.AllergenDefinitions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToListAsync(ct);
        var allergens = definitions
            .Where(definition => ingredientAllergenCodes.Contains(definition.Code) || selectedAllergyCodes.Contains(definition.Code))
            .Select(definition =>
            {
                var choice = selected.FirstOrDefault(x => !x.IsDislike && x.AllergenCode.Equals(definition.Code, StringComparison.OrdinalIgnoreCase));
                return new FoodPreferenceAllergenOptionDto(definition.Code, definition.NameAr, definition.NameEn, choice is not null, choice?.Severity);
            }).ToList();
        var dietaryRows = await _db.FoodPreferenceDefinitions.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new { x.Code, x.NameAr, x.NameEn })
            .ToListAsync(ct);
        var dietary = dietaryRows
            .Select(x => new DietaryPreferenceOptionDto(x.Code, x.NameAr, x.NameEn, selectedDietary.Contains(x.Code), false))
            .ToList();
        var ingredientOptions = ingredients
            .GroupBy(x => MealPreferenceFilter.IngredientGroupKey(x.NameAr, x.NameEn), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().NameAr)
            .Select(group =>
            {
                var representative = group.OrderBy(x => x.Id).First();
                var code = $"INGREDIENT:{representative.Id}";
                var selectedInGroup = group.Any(x => dislikedCodes.Contains($"INGREDIENT:{x.Id}"));
                var allergenCodes = group.SelectMany(x => FoodCodeNormalizer.Split(x.AllergenCodes)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
                return new FoodPreferenceIngredientOptionDto(code, representative.NameAr, representative.NameEn, representative.Category, allergenCodes, selectedInGroup);
            }).ToList();
        return new CustomerFoodPreferenceCatalogDto(
            ingredientOptions,
            allergens,
            allergens.Count(x => x.IsSelected),
            dietary);
    }

    public async Task<Result<CustomerAllergiesScreenDto>> Handle(GetCustomerAllergiesQuery request, CancellationToken ct)
    {
        var catalog = await ScreenCatalogAsync(ct); if (catalog.IsFailure) return catalog.Error;
        var (page, pageSize) = NormalizePage(request.Page, request.PageSize);
        var filtered = catalog.Value.Allergens.Where(x => MatchesSearch(request.Search, x.NameAr, x.NameEn, x.Code)).ToList();
        var selectedCount = catalog.Value.Allergens.Count(x => x.IsSelected);
        var none = new FoodPreferenceAllergenOptionDto(FoodPreferenceNoneCodes.NoAllergies, "لا توجد حساسية", "No allergies", selectedCount == 0, null, true);
        return new CustomerAllergiesScreenDto(
            WithLeadingNone(page, none, Paginate(filtered, page, pageSize)),
            selectedCount,
            page,
            pageSize,
            filtered.Count);
    }

    public async Task<Result<CustomerDislikedIngredientsScreenDto>> Handle(GetCustomerDislikedIngredientsQuery request, CancellationToken ct)
    {
        var catalog = await ScreenCatalogAsync(ct); if (catalog.IsFailure) return catalog.Error;
        var (page, pageSize) = NormalizePage(request.Page, request.PageSize);
        var filtered = catalog.Value.Ingredients.Where(x => MatchesSearch(request.Search, x.NameAr, x.NameEn, x.Category)).ToList();
        var selectedCount = catalog.Value.Ingredients.Count(x => x.IsSelected);
        var none = new FoodPreferenceIngredientOptionDto(FoodPreferenceNoneCodes.NoDislikes, "أتناول كل أنواع الطعام", "I eat all foods", null, [], selectedCount == 0, true);
        return new CustomerDislikedIngredientsScreenDto(
            WithLeadingNone(page, none, Paginate(filtered, page, pageSize)),
            selectedCount,
            page,
            pageSize,
            filtered.Count);
    }

    public async Task<Result<CustomerDietaryPreferencesScreenDto>> Handle(GetCustomerDietaryPreferencesQuery request, CancellationToken ct)
    {
        var catalog = await ScreenCatalogAsync(ct); if (catalog.IsFailure) return catalog.Error;
        var dietary = catalog.Value.DietaryPreferences ?? [];
        var (page, pageSize) = NormalizePage(request.Page, request.PageSize);
        var filtered = dietary.Where(x => MatchesSearch(request.Search, x.NameAr, x.NameEn, x.Code)).ToList();
        var selectedCount = dietary.Count(x => x.IsSelected);
        var none = new DietaryPreferenceOptionDto(FoodPreferenceNoneCodes.NoDiet, "لا أتبع نظاماً غذائياً معيناً", "I don't follow a specific diet", selectedCount == 0, true);
        return new CustomerDietaryPreferencesScreenDto(
            WithLeadingNone(page, none, Paginate(filtered, page, pageSize)),
            selectedCount,
            page,
            pageSize,
            filtered.Count);
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerFoodPreferencesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await ProfileAsync(userId.Value, ct); if (profile is null) return NotFound();
        var allergies = await HydrateAllergyNamesAsync(NormalizeItems(WithoutNone(request.Allergies), false), ct);
        var dislikes = await HydrateIngredientNamesAsync(NormalizeItems(WithoutNone(request.DislikedFoods), true), ct);
        var dietary = NormalizeCodes(WithoutNone(request.DietaryPreferenceCodes ?? []));
        var valid = await ValidateChoicesAsync(allergies, dislikes, dietary, ct); if (valid.IsFailure) return valid.Error;
        await ReplaceAllergiesAsync(userId.Value, allergies, dislikes, ct);
        await ReplaceDietaryAsync(userId.Value, dietary, ct);
        profile.DietaryPattern = request.DietaryPattern?.Trim();
        profile.DietaryNotes = request.Notes?.Trim();
        profile.AllergiesCompletedAtUtc = _clock.UtcNow;
        profile.DietaryPreferencesCompletedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(new AuditEntry("CustomerFoodPreferencesSaved", nameof(CustomerProfile), profile.Id, SourceChannel: "customer-app"), ct);
        return await ReadAsync(userId.Value, ct);
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerAllergiesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await ProfileAsync(userId.Value, ct); if (profile is null) return NotFound();
        var allergies = await HydrateAllergyNamesAsync(NormalizeItems(WithoutNone(request.Allergies), false), ct);
        var valid = await ValidateChoicesAsync(allergies, [], [], ct); if (valid.IsFailure) return valid.Error;
        var existingAllergies = await _db.CustomerAllergyPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == userId.Value && !x.IsDislike).ToListAsync(ct);
        _db.CustomerAllergyPreferences.RemoveRange(existingAllergies);
        _db.CustomerAllergyPreferences.AddRange(allergies.Select(x => ToEntity(userId.Value, x, false)));
        profile.AllergiesCompletedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ReadAsync(userId.Value, ct);
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerDietaryPreferencesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await ProfileAsync(userId.Value, ct); if (profile is null) return NotFound();
        var codes = NormalizeCodes(WithoutNone(request.DietaryPreferenceCodes));
        var valid = await ValidateChoicesAsync([], [], codes, ct); if (valid.IsFailure) return valid.Error;
        await ReplaceDietaryAsync(userId.Value, codes, ct);
        profile.DietaryPreferencesCompletedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ReadAsync(userId.Value, ct);
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerDislikedIngredientsCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        if (await ProfileAsync(userId.Value, ct) is null) return NotFound();
        var dislikes = await HydrateIngredientNamesAsync(NormalizeItems(WithoutNone(request.IngredientCodes).Select(x => new FoodPreferenceItemDto(x, null, null, null)), true), ct);
        var valid = await ValidateChoicesAsync([], dislikes, [], ct); if (valid.IsFailure) return valid.Error;
        var existingDislikes = await _db.CustomerAllergyPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == userId.Value && x.IsDislike).ToListAsync(ct);
        _db.CustomerAllergyPreferences.RemoveRange(existingDislikes);
        _db.CustomerAllergyPreferences.AddRange(dislikes.Select(x => ToEntity(userId.Value, x, true)));
        await _db.SaveChangesAsync(ct);
        return await ReadAsync(userId.Value, ct);
    }

    private async Task<Result> ValidateChoicesAsync(IReadOnlyList<FoodPreferenceItemDto> allergies, IReadOnlyList<FoodPreferenceItemDto> dislikes, IReadOnlyList<string> dietary, CancellationToken ct)
    {
        var approvedRestaurants = _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
            .Select(x => x.Id);
        var allergenSet = (await _db.AllergenDefinitions.AsNoTracking().Where(x => x.IsActive).Select(x => x.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allergies.Any(x => !allergenSet.Contains(x.Code))) return Error.Validation("food_preferences.invalid_allergen", "One or more allergen codes are not available in the current restaurant catalog.");
        var ingredientIds = new List<Guid>();
        foreach (var item in dislikes)
        {
            if (!item.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(item.Code[11..], out var id))
                return Error.Validation("food_preferences.invalid_ingredient", "Disliked foods must use INGREDIENT:{id} codes.");
            ingredientIds.Add(id);
        }
        if (ingredientIds.Count > 0 && await _db.Ingredients.AsNoTracking().CountAsync(x => ingredientIds.Contains(x.Id) && x.IsActive, ct) != ingredientIds.Distinct().Count())
            return Error.Validation("food_preferences.invalid_ingredient", "One or more disliked ingredients are unavailable.");
        if (dietary.Count > 0 && await _db.FoodPreferenceDefinitions.AsNoTracking().CountAsync(x => dietary.Contains(x.Code) && x.IsActive, ct) != dietary.Count)
            return Error.Validation("food_preferences.invalid_dietary_preference", "One or more dietary preference codes are inactive or unknown.");
        return Result.Success();
    }

    private async Task ReplaceAllergiesAsync(Guid userId, IReadOnlyList<FoodPreferenceItemDto> allergies, IReadOnlyList<FoodPreferenceItemDto> dislikes, CancellationToken ct)
    {
        var existing = await _db.CustomerAllergyPreferences.IgnoreQueryFilters().Where(x => x.CustomerUserId == userId).ToListAsync(ct);
        _db.CustomerAllergyPreferences.RemoveRange(existing);
        _db.CustomerAllergyPreferences.AddRange(allergies.Select(x => ToEntity(userId, x, false)).Concat(dislikes.Select(x => ToEntity(userId, x, true))));
    }

    private async Task ReplaceDietaryAsync(Guid userId, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var existing = await _db.CustomerDietaryPreferences.IgnoreQueryFilters().Where(x => x.CustomerUserId == userId).ToListAsync(ct);
        _db.CustomerDietaryPreferences.RemoveRange(existing);
        _db.CustomerDietaryPreferences.AddRange(codes.Select(x => new CustomerDietaryPreference { CustomerUserId = userId, PreferenceCode = x }));
    }

    private async Task<CustomerFoodPreferencesDto> ReadAsync(Guid userId, CancellationToken ct)
    {
        var profile = await ProfileAsync(userId, ct) ?? throw new InvalidOperationException("Customer profile not found.");
        var values = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId).ToListAsync(ct);
        var dietary = await _db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId).OrderBy(x => x.PreferenceCode).Select(x => x.PreferenceCode).ToListAsync(ct);
        var allergenDefs = await _db.AllergenDefinitions.AsNoTracking().Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        return new CustomerFoodPreferencesDto(profile.DietaryPattern,
            values.Where(x => !x.IsDislike).Select(x => MapAllergy(x, allergenDefs)).ToList(),
            values.Where(x => x.IsDislike).Select(MapItem).ToList(), profile.DietaryNotes, dietary);
    }

    private async Task<IReadOnlyList<FoodPreferenceItemDto>> HydrateAllergyNamesAsync(IReadOnlyList<FoodPreferenceItemDto> items, CancellationToken ct)
    {
        var codes = items.Select(x => x.Code).ToList();
        var definitions = await _db.AllergenDefinitions.AsNoTracking().Where(x => codes.Contains(x.Code) && x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        return items.Select(x => definitions.TryGetValue(x.Code, out var definition)
            ? x with { NameAr = definition.NameAr, NameEn = definition.NameEn }
            : x with { NameAr = null, NameEn = null }).ToList();
    }

    private async Task<IReadOnlyList<FoodPreferenceItemDto>> HydrateIngredientNamesAsync(IReadOnlyList<FoodPreferenceItemDto> items, CancellationToken ct)
    {
        var rawItems = items.Where(x => !string.IsNullOrWhiteSpace(x.Code)).ToList();
        if (rawItems.Count == 0) return [];

        var normalized = rawItems.Select(x =>
        {
            var trimmed = x.Code.Trim();
            if (Guid.TryParse(trimmed, out var g))
                return x with { Code = $"INGREDIENT:{g.ToString().ToLowerInvariant()}" };
            return x with { Code = trimmed };
        }).ToList();

        var unmatchedNonGuid = normalized
            .Where(x => !x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dbMatches = new Dictionary<string, Ingredient>(StringComparer.OrdinalIgnoreCase);
        if (unmatchedNonGuid.Count > 0)
        {
            var found = await _db.Ingredients.AsNoTracking()
                .Where(x => unmatchedNonGuid.Contains(x.Code) || unmatchedNonGuid.Contains(x.NameAr) || unmatchedNonGuid.Contains(x.NameEn))
                .ToListAsync(ct);
            foreach (var ing in found)
            {
                if (!dbMatches.ContainsKey(ing.Code)) dbMatches[ing.Code] = ing;
                if (!dbMatches.ContainsKey(ing.NameAr)) dbMatches[ing.NameAr] = ing;
                if (!string.IsNullOrWhiteSpace(ing.NameEn) && !dbMatches.ContainsKey(ing.NameEn)) dbMatches[ing.NameEn] = ing;
            }
        }

        var mapped = normalized.Select(x =>
        {
            if (dbMatches.TryGetValue(x.Code, out var matched))
            {
                return x with
                {
                    Code = $"INGREDIENT:{matched.Id.ToString().ToLowerInvariant()}",
                    NameAr = x.NameAr ?? matched.NameAr,
                    NameEn = x.NameEn ?? matched.NameEn
                };
            }
            return x;
        }).DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();

        var ids = mapped
            .Where(x => x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.Code.AsSpan(11), out _))
            .Select(x => Guid.Parse(x.Code[11..]))
            .Distinct()
            .ToList();

        var ingredients = ids.Count == 0
            ? new Dictionary<Guid, Ingredient>()
            : await _db.Ingredients.AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        return mapped.Select(x =>
        {
            if (x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.Code.AsSpan(11), out var id) && ingredients.TryGetValue(id, out var ingredient))
            {
                return x with
                {
                    NameAr = x.NameAr ?? ingredient.NameAr,
                    NameEn = x.NameEn ?? ingredient.NameEn
                };
            }
            return x;
        }).ToList();
    }

    private async Task<Result<CustomerFoodPreferenceCatalogDto>> ScreenCatalogAsync(CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        if (await ProfileAsync(userId.Value, ct) is null) return NotFound();
        return await Handle(new GetCustomerFoodPreferenceCatalogQuery(), ct);
    }

    private Task<CustomerProfile?> ProfileAsync(Guid userId, CancellationToken ct) => _db.CustomerProfiles.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    private Result<Guid> CustomerId() => _currentUser.UserId is not { } id
        ? Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.")
        : _currentUser.UserType is UserType.Customer ? id : Error.Forbidden(MessageCodes.Auth.InvalidCredentials, "This endpoint is only available to customer accounts.");
    private static Error NotFound() => Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
    private static FoodPreferenceItemDto MapItem(CustomerAllergyPreference x) => new(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity);
    private static FoodPreferenceItemDto MapAllergy(CustomerAllergyPreference x, IReadOnlyDictionary<string, AllergenDefinition> definitions)
        => definitions.TryGetValue(x.AllergenCode, out var definition)
            ? new FoodPreferenceItemDto(x.AllergenCode, definition.NameAr, definition.NameEn, x.Severity)
            : MapItem(x);
    private static CustomerAllergyPreference ToEntity(Guid userId, FoodPreferenceItemDto x, bool dislike) => new() { CustomerUserId = userId, AllergenCode = x.Code, AllergenNameAr = x.NameAr?.Trim(), AllergenNameEn = x.NameEn?.Trim(), Severity = x.Severity?.Trim(), IsDislike = dislike };
    private static IReadOnlyList<string> NormalizeCodes(IEnumerable<string> values) => values.Select(FoodCodeNormalizer.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    private static IReadOnlyList<FoodPreferenceItemDto> NormalizeItems(IEnumerable<FoodPreferenceItemDto> values, bool ingredient) => values.Where(x => !string.IsNullOrWhiteSpace(x.Code)).Select(x =>
    {
        if (!ingredient) return x with { Code = FoodCodeNormalizer.Normalize(x.Code) };
        var trimmed = x.Code.Trim();
        if (trimmed.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            return x with { Code = $"INGREDIENT:{trimmed[11..].Trim().ToLowerInvariant()}" };
        if (Guid.TryParse(trimmed, out var g))
            return x with { Code = $"INGREDIENT:{g.ToString().ToLowerInvariant()}" };
        return x with { Code = FoodCodeNormalizer.Normalize(trimmed) };
    }).DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
    private static IReadOnlyList<FoodPreferenceItemDto> WithoutNone(IEnumerable<FoodPreferenceItemDto> values)
        => values.Where(x => !FoodPreferenceNoneCodes.IsNone(x.Code)).ToList();
    private static IReadOnlyList<string> WithoutNone(IEnumerable<string> values)
        => values.Where(x => !FoodPreferenceNoneCodes.IsNone(x)).ToList();
    private static (int Page, int PageSize) NormalizePage(int page, int pageSize)
        => (page < 1 ? 1 : page, pageSize < 1 ? 20 : Math.Min(pageSize, 50));
    private static IReadOnlyList<T> Paginate<T>(IReadOnlyList<T> items, int page, int pageSize)
        => items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    private static IReadOnlyList<T> WithLeadingNone<T>(int page, T none, IReadOnlyList<T> items)
        => page == 1 ? new[] { none }.Concat(items).ToList() : items;
    private static bool MatchesSearch(string? search, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var query = search.Trim();
        return values.Any(value => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}

public static class FoodPreferenceNoneCodes
{
    public const string NoAllergies = "NONE";
    public const string NoDislikes = "NONE";
    public const string NoDiet = "NONE";

    public static bool IsNone(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.Equals("NONE", StringComparison.OrdinalIgnoreCase);
}
