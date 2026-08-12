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

public sealed record GetCustomerFoodPreferencesQuery : IQuery<CustomerFoodPreferencesDto>;

public sealed record SaveCustomerFoodPreferencesCommand(
    string? DietaryPattern,
    IReadOnlyList<FoodPreferenceItemDto> Allergies,
    IReadOnlyList<FoodPreferenceItemDto> DislikedFoods,
    string? Notes) : ICommand<CustomerFoodPreferencesDto>;

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

public sealed class CustomerFoodPreferencesHandlers :
    IQueryHandler<GetCustomerFoodPreferencesQuery, CustomerFoodPreferencesDto>,
    ICommandHandler<SaveCustomerFoodPreferencesCommand, CustomerFoodPreferencesDto>
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

    public async Task<Result<CustomerFoodPreferencesDto>> Handle(SaveCustomerFoodPreferencesCommand request, CancellationToken ct)
    {
        var userId = CustomerId(); if (userId.IsFailure) return userId.Error;
        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(x => x.UserId == userId.Value, ct);
        if (profile is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");

        var existing = await _db.CustomerAllergyPreferences.Where(x => x.CustomerUserId == userId.Value).ToListAsync(ct);
        _db.CustomerAllergyPreferences.RemoveRange(existing);
        profile.DietaryPattern = request.DietaryPattern?.Trim();
        profile.DietaryNotes = request.Notes?.Trim();
        Add(request.Allergies, false); Add(request.DislikedFoods, true);
        await _audit.LogAsync(new AuditEntry("CustomerFoodPreferencesSaved", nameof(CustomerProfile), profile.Id, SourceChannel: "customer-app"), ct);
        return Map(profile.DietaryPattern, profile.DietaryNotes, request.Allergies.Select(x => ToEntity(x, false)).Concat(request.DislikedFoods.Select(x => ToEntity(x, true))).ToList());

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

    private Result<Guid> CustomerId() => _currentUser.UserId is not { } id
        ? Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.")
        : _currentUser.UserType is UserType.Customer ? id
        : Error.Forbidden(MessageCodes.Auth.InvalidCredentials, "This endpoint is only available to customer accounts.");

    private static CustomerFoodPreferencesDto Map(string? pattern, string? notes, IEnumerable<CustomerAllergyPreference> values) => new(
        pattern,
        values.Where(x => !x.IsDislike).Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity)).ToList(),
        values.Where(x => x.IsDislike).Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity)).ToList(),
        notes);
}
