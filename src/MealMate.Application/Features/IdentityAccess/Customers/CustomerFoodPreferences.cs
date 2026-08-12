using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public sealed record FoodPreferenceItemDto(string Code, string? NameAr, string? NameEn, string? Severity);

public sealed record CustomerFoodPreferencesDto(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto> Allergies,
    IReadOnlyList<FoodPreferenceItemDto> DislikedFoods,
    string? Notes);

/// <summary>Customer-facing choices are sourced from restaurant-managed ingredients, never a client-maintained list.</summary>
public sealed record FoodPreferenceIngredientOptionDto(string Code, string NameAr, string NameEn, string? Category, IReadOnlyList<string> AllergenCodes);
public sealed record FoodPreferenceAllergenOptionDto(
    string Code,
    string? NameAr,
    string? NameEn,
    bool IsSelected = false,
    string? Severity = null);
public sealed record CustomerFoodPreferenceCatalogDto(
    IReadOnlyList<FoodPreferenceIngredientOptionDto> Ingredients,
    IReadOnlyList<FoodPreferenceAllergenOptionDto> Allergens,
    int SelectedAllergenCount = 0);

public sealed record GetCustomerFoodPreferencesQuery : IQuery<CustomerFoodPreferencesDto>;
public sealed record GetCustomerFoodPreferenceCatalogQuery : IQuery<CustomerFoodPreferenceCatalogDto>;

public sealed record SaveCustomerFoodPreferencesCommand(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto> Allergies,
    IReadOnlyList<FoodPreferenceItemDto> DislikedFoods,
    string? Notes) : ICommand<CustomerFoodPreferencesDto>;

/// <summary>Changes only the selections made on the allergy screen.</summary>
public sealed record SaveCustomerAllergiesCommand(
    IReadOnlyList<FoodPreferenceItemDto> Allergies) : ICommand<CustomerFoodPreferencesDto>;

public sealed class SaveCustomerFoodPreferencesCommandValidator : AbstractValidator<SaveCustomerFoodPreferencesCommand>
{
    public SaveCustomerFoodPreferencesCommandValidator()
    {
        RuleFor(x => x.DietaryPattern).MaximumLength(80).When(x => x.DietaryPattern is not null);
        RuleFor(x => x.Notes).MaximumLength(1000).When(x => x.Notes is not null);
        RuleForEach(x => x.Allergies).SetValidator(new FoodPreferenceItemValidator());
        RuleForEach(x => x.DislikedFoods).SetValidator(new FoodPreferenceItemValidator());
    }
}

public sealed class FoodPreferenceItemValidator : AbstractValidator<FoodPreferenceItemDto>
{
    public FoodPreferenceItemValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.NameEn).MaximumLength(200).When(x => x.NameEn is not null);
        RuleFor(x => x.Severity).MaximumLength(50).When(x => x.Severity is not null);
    }
}

public sealed class SaveCustomerAllergiesCommandValidator : AbstractValidator<SaveCustomerAllergiesCommand>
{
    public SaveCustomerAllergiesCommandValidator()
        => RuleForEach(x => x.Allergies).SetValidator(new FoodPreferenceItemValidator());
}

public sealed class CustomerFoodPreferencesHandlers :
    IQueryHandler<GetCustomerFoodPreferencesQuery, CustomerFoodPreferencesDto>,
    IQueryHandler<GetCustomerFoodPreferenceCatalogQuery, CustomerFoodPreferenceCatalogDto>,
    ICommandHandler<SaveCustomerFoodPreferencesCommand, CustomerFoodPreferencesDto>,
    ICommandHandler<SaveCustomerAllergiesCommand, CustomerFoodPreferencesDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public CustomerFoodPreferencesHandlers(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit)
        => (_db, _currentUser, _audit) = (db, currentUser, audit);

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(GetCustomerFoodPreferencesQuery request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await _db.CustomerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        if (profile is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
        var choices = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId.Value).ToListAsync(ct);
        return Map(profile.DietaryPattern, profile.DietaryNotes, choices);
    }

    public async Task<Result<CustomerFoodPreferenceCatalogDto>> Handle(GetCustomerFoodPreferenceCatalogQuery request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var ingredients = await _db.Ingredients.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.NameAr).Select(x => new { x.Id, x.NameAr, x.NameEn, x.Category, x.AllergenCodes }).ToListAsync(ct);
        var selectedAllergies = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == userId.Value && !x.IsDislike)
            .GroupBy(x => x.AllergenCode)
            .Select(x => new { Code = x.Key, Severity = x.Max(x => x.Severity) })
            .ToDictionaryAsync(x => x.Code, x => x.Severity, StringComparer.OrdinalIgnoreCase, ct);
        var allergenNames = await _db.MealAllergens.AsNoTracking().Where(x => !x.IsDeleted)
            .GroupBy(x => x.AllergenCode).Select(x => new { Code = x.Key, NameAr = x.Max(x => x.AllergenNameAr), NameEn = x.Max(x => x.AllergenNameEn) }).ToListAsync(ct);
        // Restaurants declare allergen codes on their ingredients. When a meal-level
        // allergen dictionary has not been created yet, retain the ingredient's names
        // so the mobile app never receives an unnamed choice.
        var ingredientAllergenNames = ingredients
            .SelectMany(x => SplitCodes(x.AllergenCodes).Select(code => new
            {
                Code = code,
                NameAr = x.NameAr,
                NameEn = x.NameEn
            }))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var allergens = ingredients.SelectMany(x => SplitCodes(x.AllergenCodes)).Concat(allergenNames.Select(x => x.Code))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Select(code =>
            {
                var name = allergenNames.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                ingredientAllergenNames.TryGetValue(code, out var ingredientName);
                return new FoodPreferenceAllergenOptionDto(
                    code,
                    name?.NameAr ?? ingredientName?.NameAr,
                    name?.NameEn ?? ingredientName?.NameEn,
                    selectedAllergies.TryGetValue(code, out var severity),
                    severity);
            }).ToList();
        return new CustomerFoodPreferenceCatalogDto(
            ingredients.Select(x => new FoodPreferenceIngredientOptionDto($"INGREDIENT:{x.Id}", x.NameAr, x.NameEn, x.Category, SplitCodes(x.AllergenCodes))).ToList(),
            allergens,
            allergens.Count(x => x.IsSelected));
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerFoodPreferencesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        if (profile is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");

        // These rows are customer selections, not historical records. They must be
        // physically removed here: CustomerAllergyPreference supports soft delete,
        // while its unique key deliberately does not include IsDeleted. A normal
        // RemoveRange would therefore leave the old key occupied and a later save
        // of the same allergy would fail with a database unique-constraint error.
        await _db.CustomerAllergyPreferences
            .Where(x => x.CustomerUserId == userId.Value)
            .ExecuteDeleteAsync(ct);
        profile.DietaryPattern = request.DietaryPattern?.Trim();
        profile.DietaryNotes = request.Notes?.Trim();
        var allergies = Normalize(request.Allergies);
        var dislikes = Normalize(request.DislikedFoods);
        Add(allergies, false); Add(dislikes, true);
        await _audit.LogAsync(new AuditEntry("CustomerFoodPreferencesSaved", nameof(CustomerProfile), profile.Id, SourceChannel: "customer-app"), ct);
        return Map(profile.DietaryPattern, profile.DietaryNotes, allergies.Select(x => ToEntity(x, false)).Concat(dislikes.Select(x => ToEntity(x, true))).ToList());

        void Add(IEnumerable<FoodPreferenceItemDto> values, bool dislike)
        {
            foreach (var item in values.GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
                _db.CustomerAllergyPreferences.Add(ToEntity(item, dislike));
        }
        CustomerAllergyPreference ToEntity(FoodPreferenceItemDto x, bool dislike) => new()
        {
            CustomerUserId = userId.Value, AllergenCode = x.Code.Trim(), AllergenNameAr = x.NameAr?.Trim(),
            AllergenNameEn = x.NameEn?.Trim(), Severity = x.Severity?.Trim(), IsDislike = dislike
        };
    }

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerAllergiesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        if (profile is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");

        // See the note in SaveCustomerFoodPreferences: this must bypass soft delete
        // so a previously selected allergen can be selected again without violating
        // the CustomerUserId/AllergenCode/IsDislike unique index.
        await _db.CustomerAllergyPreferences
            .Where(x => x.CustomerUserId == userId.Value && !x.IsDislike)
            .ExecuteDeleteAsync(ct);

        var allergies = Normalize(request.Allergies);
        foreach (var allergy in allergies)
            _db.CustomerAllergyPreferences.Add(ToEntity(userId.Value, allergy, false));

        var dislikes = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == userId.Value && x.IsDislike)
            .ToListAsync(ct);
        await _audit.LogAsync(new AuditEntry("CustomerAllergiesSaved", nameof(CustomerProfile), profile.Id, SourceChannel: "customer-app"), ct);
        return Map(profile.DietaryPattern, profile.DietaryNotes, allergies.Select(x => ToEntity(userId.Value, x, false)).Concat(dislikes));
    }

    private Result<Guid> CustomerId() => _currentUser.UserId is not { } id
        ? Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.")
        : _currentUser.UserType is UserType.Customer ? id
        : Error.Forbidden(MessageCodes.Auth.InvalidCredentials, "This endpoint is only available to customer accounts.");

    private static CustomerFoodPreferencesDto Map(string? pattern, string? notes, IEnumerable<CustomerAllergyPreference> values) => new(
        pattern,
        values.Where(x => !x.IsDislike).Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity)).ToList(),
        values.Where(x => x.IsDislike).Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity)).ToList(),
        notes);

    private static CustomerAllergyPreference ToEntity(Guid customerUserId, FoodPreferenceItemDto item, bool isDislike) => new()
    {
        CustomerUserId = customerUserId,
        AllergenCode = item.Code.Trim(),
        AllergenNameAr = item.NameAr?.Trim(),
        AllergenNameEn = item.NameEn?.Trim(),
        Severity = item.Severity?.Trim(),
        IsDislike = isDislike
    };

    private static IReadOnlyList<FoodPreferenceItemDto> Normalize(IEnumerable<FoodPreferenceItemDto> values) => values
        .Where(item => !string.IsNullOrWhiteSpace(item.Code))
        .GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToList();

    private static IReadOnlyList<string> SplitCodes(string? codes) => string.IsNullOrWhiteSpace(codes) ? [] : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).Distinct().ToList();
}
