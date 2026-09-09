using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class FamilyDraftHandlers :
    IQueryHandler<GetFamilyDraftMetadataQuery, FamilyDraftMetadataDto>,
    IQueryHandler<GetFamilyRelationshipsQuery, IReadOnlyList<FamilyLookupDto>>,
    IQueryHandler<GetFamilySizePackagesQuery, IReadOnlyList<FamilySizePackageDto>>,
    IQueryHandler<GetCurrentFamilyGroupQuery, FamilyGroupScreenDto>,
    IQueryHandler<GetFamilyCheckoutSummaryQuery, FamilyCheckoutSummaryDto>,
    IQueryHandler<LookupFamilyMemberByPhoneQuery, FamilyMemberLookupDto>,
    ICommandHandler<UpsertFamilyGroupCommand, FamilyGroupScreenDto>,
    ICommandHandler<AddFamilyDraftMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<UpdateFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<DeleteFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<SetFamilyMemberPlanCommand, FamilyGroupScreenDto>,
    ICommandHandler<SaveFamilyMemberAllergiesCommand, bool>,
    IQueryHandler<GetFamilyMemberAllergiesQuery, FamilyMemberAllergiesDto>,
    ICommandHandler<SaveFamilyMemberDislikedFoodsCommand, bool>,
    IQueryHandler<GetFamilyMemberDislikedFoodsQuery, FamilyMemberDislikedFoodsDto>,
    ICommandHandler<SaveFamilyMemberDietaryCommand, bool>,
    IQueryHandler<GetFamilyMemberDietaryQuery, FamilyMemberDietaryDto>,
    ICommandHandler<CreateFamilyMemberAddressCommand, CustomerAddressDto>,
    IQueryHandler<GetFamilyMemberAddressesQuery, IReadOnlyList<CustomerAddressDto>>,
    ICommandHandler<UpdateFamilyMemberAddressCommand, CustomerAddressDto>,
    ICommandHandler<PreviewFamilyMemberAddressCoverageCommand, AddressCoveragePreviewDto>,
    ICommandHandler<SetFamilyMemberDefaultAddressCommand, CustomerAddressDto>,
    ICommandHandler<AbandonFamilyDraftCommand, bool>
{
    private const string CheckoutEndpoint = "POST /api/v1/customer/family-subscription-flow/checkout";

    private static readonly FamilyGroupStatus[] EditableStatuses =
    [
        FamilyGroupStatus.Draft,
        FamilyGroupStatus.CheckoutPending,
    ];

    private static readonly FamilyGroupStatus[] RosterStatuses =
    [
        FamilyGroupStatus.Draft,
        FamilyGroupStatus.CheckoutPending,
        FamilyGroupStatus.Active,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyPricingSettingsReader _settings;
    private readonly IFamilyPlanEligibilityService _planEligibility;
    private readonly IFamilyQuoteService _quote;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerAddressBook _addresses;

    public FamilyDraftHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyPricingSettingsReader settings,
        IFamilyPlanEligibilityService planEligibility,
        IFamilyQuoteService quote,
        IDateTimeProvider clock,
        ICustomerAddressBook addresses)
        => (_db, _currentUser, _users, _settings, _planEligibility, _quote, _clock, _addresses)
            = (db, currentUser, users, settings, planEligibility, quote, clock, addresses);

    public async Task<Result<FamilyDraftMetadataDto>> Handle(GetFamilyDraftMetadataQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var settings = await _settings.GetAsync(cancellationToken);
        return new FamilyDraftMetadataDto(
            FamilyCatalog.Relationships(),
            MapPackages(settings),
            settings.MaxMembers,
            settings.DiscountPercent,
            settings.MinMembersForDiscount);
    }

    public Task<Result<IReadOnlyList<FamilyLookupDto>>> Handle(
        GetFamilyRelationshipsQuery request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure)
            return Task.FromResult<Result<IReadOnlyList<FamilyLookupDto>>>(managerId.Error);

        return Task.FromResult(Result.Success<IReadOnlyList<FamilyLookupDto>>(FamilyCatalog.Relationships()));
    }

    public async Task<Result<IReadOnlyList<FamilySizePackageDto>>> Handle(
        GetFamilySizePackagesQuery request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure)
            return managerId.Error;

        var settings = await _settings.GetAsync(cancellationToken);
        return Result.Success<IReadOnlyList<FamilySizePackageDto>>(MapPackages(settings));
    }

    public async Task<Result<FamilyMemberLookupDto>> Handle(
        LookupFamilyMemberByPhoneQuery request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var phone = request.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
            return Error.Validation("family.phone_required", "Phone number is required.");

        var account = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (account is null)
            return new FamilyMemberLookupDto(false, null, null, null);

        var occupied = await IsInAnotherFamilyAsync(account.Id, managerId.Value, cancellationToken);
        if (occupied)
            return Error.Conflict("family.already_in_family", "This phone already belongs to another family.");

        var existing = await _db.FamilyMembers.AsNoTracking()
            .Where(m => m.PhoneNumber == phone)
            .Select(m => new { m.DisplayName, m.AgeYears, m.Gender })
            .FirstOrDefaultAsync(cancellationToken);

        return new FamilyMemberLookupDto(
            true,
            account.FullName ?? existing?.DisplayName,
            existing?.AgeYears,
            existing?.Gender);
    }

    public async Task<Result<bool>> Handle(SaveFamilyMemberAllergiesCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;

        var access = await _db.FamilyMemberAccesses
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        var targetUserId = access?.LinkedCustomerUserId
            ?? (member.Role == FamilyMemberRole.Manager ? managerId.Value : null);
        if (targetUserId is null)
            return Error.Conflict("family.member_account_required", "Family member must be linked before saving allergies.");

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(x => x.UserId == targetUserId.Value, cancellationToken);
        if (profile is null)
        {
            profile = new CustomerProfile { UserId = targetUserId.Value };
            _db.CustomerProfiles.Add(profile);
        }

        var selected = NormalizePreferenceItems(request.Allergies, ingredient: false);
        var hydrated = await HydrateAllergenNamesAsync(selected, cancellationToken);
        if (hydrated.IsFailure) return hydrated.Error;

        var existingAllergies = await _db.CustomerAllergyPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == targetUserId.Value && !x.IsDislike)
            .ToListAsync(cancellationToken);
        _db.CustomerAllergyPreferences.RemoveRange(existingAllergies);
        _db.CustomerAllergyPreferences.AddRange(hydrated.Value.Select(x => ToAllergyEntity(targetUserId.Value, x, dislike: false)));

        profile.AllergiesCompletedAtUtc = _clock.UtcNow;

        if (request.ReplaceDislikedFoods)
        {
            var disliked = await ReplaceDislikedFoodsAsync(targetUserId.Value, request.DislikedFoods ?? [], cancellationToken);
            if (disliked.IsFailure) return disliked.Error;
        }

        if (request.ReplaceDietary)
        {
            var dietary = await ReplaceDietaryAsync(targetUserId.Value, request.DietaryPreferenceCodes ?? [], cancellationToken);
            if (dietary.IsFailure) return dietary.Error;
            profile.DietaryPreferencesCompletedAtUtc = _clock.UtcNow;
        }

        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<FamilyMemberAllergiesDto>> Handle(
        GetFamilyMemberAllergiesQuery request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken, allowWhenCheckoutPending: true);
        if (target.IsFailure) return target.Error;

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == target.Value.UserId, cancellationToken);
        var rows = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == target.Value.UserId && !x.IsDislike)
            .OrderBy(x => x.AllergenCode)
            .ToListAsync(cancellationToken);
        var definitions = await _db.AllergenDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var selected = rows.Select(x => definitions.TryGetValue(x.AllergenCode, out var definition)
            ? new FoodPreferenceItemDto(x.AllergenCode, definition.NameAr, definition.NameEn, x.Severity)
            : new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity)).ToList();
        var disliked = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == target.Value.UserId && x.IsDislike)
            .OrderBy(x => x.AllergenCode)
            .Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity))
            .ToListAsync(cancellationToken);

        return new FamilyMemberAllergiesDto(selected, profile?.AllergiesCompletedAtUtc is not null, disliked);
    }

    public async Task<Result<bool>> Handle(SaveFamilyMemberDislikedFoodsCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolvePreferenceMemberAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var replaced = await ReplaceDislikedFoodsAsync(target.Value.UserId, request.DislikedFoods, cancellationToken);
        if (replaced.IsFailure) return replaced.Error;
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<FamilyMemberDislikedFoodsDto>> Handle(
        GetFamilyMemberDislikedFoodsQuery request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken, allowWhenCheckoutPending: true);
        if (target.IsFailure) return target.Error;

        var rows = await _db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == target.Value.UserId && x.IsDislike)
            .OrderBy(x => x.AllergenCode)
            .Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity))
            .ToListAsync(cancellationToken);
        return new FamilyMemberDislikedFoodsDto(rows, Completed: true);
    }

    public async Task<Result<bool>> Handle(SaveFamilyMemberDietaryCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolvePreferenceMemberAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(x => x.UserId == target.Value.UserId, cancellationToken);
        if (profile is null)
        {
            profile = new CustomerProfile { UserId = target.Value.UserId };
            _db.CustomerProfiles.Add(profile);
        }

        var replaced = await ReplaceDietaryAsync(target.Value.UserId, request.DietaryPreferenceCodes, cancellationToken);
        if (replaced.IsFailure) return replaced.Error;
        profile.DietaryPreferencesCompletedAtUtc = _clock.UtcNow;
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<FamilyMemberDietaryDto>> Handle(
        GetFamilyMemberDietaryQuery request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken, allowWhenCheckoutPending: true);
        if (target.IsFailure) return target.Error;

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == target.Value.UserId, cancellationToken);
        var codes = await _db.CustomerDietaryPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == target.Value.UserId)
            .OrderBy(x => x.PreferenceCode)
            .Select(x => x.PreferenceCode)
            .ToListAsync(cancellationToken);
        var definitions = await _db.FoodPreferenceDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var selected = codes.Select(code => definitions.TryGetValue(code, out var definition)
            ? new DietaryPreferenceOptionDto(code, definition.NameAr, definition.NameEn, true)
            : new DietaryPreferenceOptionDto(code, code, code, true)).ToList();
        return new FamilyMemberDietaryDto(selected, profile?.DietaryPreferencesCompletedAtUtc is not null);
    }

    public async Task<Result<CustomerAddressDto>> Handle(CreateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var created = await _addresses.CreateAsync(
            target.Value.UserId,
            new CreateCustomerAddressCommand(
                request.Label,
                request.AddressLine,
                request.RegionId,
                request.Latitude,
                request.Longitude,
                request.Notes,
                request.IsDefault,
                request.NotifyOnDelivery,
                request.Governorate,
                request.Area,
                request.Block,
                request.Street,
                request.Avenue,
                request.Building,
                request.Unit,
                request.Floor,
                request.BlockRegionId),
            cancellationToken);
        if (created.IsFailure) return created.Error;

        target.Value.Member.DeliveryAddressId = created.Value.Id;
        target.Value.Member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(target.Value.Member);
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return created.Value;
    }

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> Handle(
        GetFamilyMemberAddressesQuery request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken, allowWhenCheckoutPending: true);
        if (target.IsFailure) return target.Error;
        return await _addresses.ListAsync(target.Value.UserId, cancellationToken);
    }

    public async Task<Result<CustomerAddressDto>> Handle(UpdateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var updated = await _addresses.UpdateAsync(
            target.Value.UserId,
            new UpdateCustomerAddressCommand(
                request.AddressId,
                request.Label,
                request.AddressLine,
                request.RegionId,
                request.Latitude,
                request.Longitude,
                request.Notes,
                request.IsDefault,
                request.NotifyOnDelivery,
                request.Governorate,
                request.Area,
                request.Block,
                request.Street,
                request.Avenue,
                request.Building,
                request.Unit,
                request.Floor,
                request.BlockRegionId),
            cancellationToken);
        if (updated.IsFailure) return updated.Error;

        if (updated.Value.IsDefault)
        {
            target.Value.Member.DeliveryAddressId = updated.Value.Id;
            target.Value.Member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(target.Value.Member);
        }

        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return updated.Value;
    }

    public async Task<Result<AddressCoveragePreviewDto>> Handle(
        PreviewFamilyMemberAddressCoverageCommand request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken, allowWhenCheckoutPending: true);
        if (target.IsFailure) return target.Error;

        return await _addresses.PreviewCoverageAsync(
            target.Value.UserId,
            new PreviewCustomerAddressCoverageCommand(
                request.AddressId,
                request.RegionId,
                request.BlockRegionId,
                request.Latitude,
                request.Longitude,
                request.Governorate,
                request.Area,
                request.Block,
                request.IsDefault),
            cancellationToken);
    }

    public async Task<Result<CustomerAddressDto>> Handle(
        SetFamilyMemberDefaultAddressCommand request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var updated = await _addresses.SetDefaultAsync(target.Value.UserId, request.AddressId, cancellationToken);
        if (updated.IsFailure) return updated.Error;

        target.Value.Member.DeliveryAddressId = updated.Value.Id;
        target.Value.Member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(target.Value.Member);
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return updated.Value;
    }

    private sealed record LinkedMemberAddressContext(FamilyGroup Group, FamilyMember Member, Guid UserId);

    private async Task<Result<LinkedMemberAddressContext>> ResolveLinkedMemberForAddressAsync(
        Guid memberId,
        CancellationToken cancellationToken,
        bool allowWhenCheckoutPending = false)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (!allowWhenCheckoutPending && group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family addresses can only be changed while checkout is not in progress.");

        var memberResult = await RequireMemberAsync(group, memberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;

        var access = await _db.FamilyMemberAccesses
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        var targetUserId = access?.LinkedCustomerUserId
            ?? (member.Role == FamilyMemberRole.Manager ? managerId.Value : null);
        if (targetUserId is null)
            return Error.Conflict("family.member_account_required", "Family member must be linked before saving an address.");

        return new LinkedMemberAddressContext(group, member, targetUserId.Value);
    }

    private async Task<Result<LinkedMemberAddressContext>> ResolvePreferenceMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
        => await ResolveLinkedMemberForAddressAsync(memberId, cancellationToken, allowWhenCheckoutPending: true);

    private static IReadOnlyList<FoodPreferenceItemDto> NormalizePreferenceItems(
        IEnumerable<FoodPreferenceItemDto>? items,
        bool ingredient)
        => (items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !FoodPreferenceNoneCodes.IsNone(x.Code))
            .Select(x => x with
            {
                Code = ingredient && x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase)
                    ? $"INGREDIENT:{x.Code[11..].Trim().ToLowerInvariant()}"
                    : FoodCodeNormalizer.Normalize(x.Code),
            })
            .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<Result<IReadOnlyList<FoodPreferenceItemDto>>> HydrateAllergenNamesAsync(
        IReadOnlyList<FoodPreferenceItemDto> selected,
        CancellationToken cancellationToken)
    {
        if (selected.Count == 0)
            return Result.Success<IReadOnlyList<FoodPreferenceItemDto>>(selected);

        var codes = selected.Select(x => x.Code).ToList();
        var definitions = await _db.AllergenDefinitions.AsNoTracking()
            .Where(x => x.IsActive && codes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (definitions.Count != codes.Count)
            return Error.Validation("food_preferences.invalid_allergen", "One or more allergen codes are not available.");

        return selected
            .Select(x => definitions.TryGetValue(x.Code, out var definition)
                ? x with { NameAr = definition.NameAr, NameEn = definition.NameEn }
                : x)
            .ToList();
    }

    private async Task<Result> ReplaceDislikedFoodsAsync(
        Guid userId,
        IReadOnlyList<FoodPreferenceItemDto> dislikedFoods,
        CancellationToken cancellationToken)
    {
        var selected = NormalizePreferenceItems(dislikedFoods, ingredient: true);
        foreach (var item in selected)
        {
            if (!item.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
                return Error.Validation("food_preferences.invalid_ingredient", "Disliked foods must use INGREDIENT:{id} codes.");
        }

        var ids = selected
            .Where(x => Guid.TryParse(x.Code.AsSpan(11), out _))
            .Select(x => Guid.Parse(x.Code[11..]))
            .ToList();
        var ingredients = ids.Count == 0
            ? new Dictionary<Guid, Ingredient>()
            : await _db.Ingredients.AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var hydrated = selected.Select(x =>
        {
            if (Guid.TryParse(x.Code.AsSpan(11), out var id) && ingredients.TryGetValue(id, out var ingredient))
                return x with { NameAr = ingredient.NameAr, NameEn = ingredient.NameEn };
            return x;
        }).ToList();

        var existing = await _db.CustomerAllergyPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == userId && x.IsDislike)
            .ToListAsync(cancellationToken);
        _db.CustomerAllergyPreferences.RemoveRange(existing);
        _db.CustomerAllergyPreferences.AddRange(hydrated.Select(x => ToAllergyEntity(userId, x, dislike: true)));
        return Result.Success();
    }

    private async Task<Result> ReplaceDietaryAsync(
        Guid userId,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var selected = (codes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x) && !FoodPreferenceNoneCodes.IsNone(x))
            .Select(FoodCodeNormalizer.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count > 0)
        {
            var found = await _db.FoodPreferenceDefinitions.AsNoTracking()
                .CountAsync(x => selected.Contains(x.Code) && x.IsActive, cancellationToken);
            if (found != selected.Count)
                return Error.Validation("food_preferences.invalid_dietary_preference", "One or more dietary preference codes are inactive or unknown.");
        }

        var existing = await _db.CustomerDietaryPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == userId)
            .ToListAsync(cancellationToken);
        _db.CustomerDietaryPreferences.RemoveRange(existing);
        _db.CustomerDietaryPreferences.AddRange(selected.Select(code => new CustomerDietaryPreference
        {
            CustomerUserId = userId,
            PreferenceCode = code,
        }));
        return Result.Success();
    }

    private static CustomerAllergyPreference ToAllergyEntity(Guid userId, FoodPreferenceItemDto item, bool dislike)
        => new()
        {
            CustomerUserId = userId,
            AllergenCode = item.Code,
            AllergenNameAr = item.NameAr?.Trim(),
            AllergenNameEn = item.NameEn?.Trim(),
            Severity = item.Severity?.Trim(),
            IsDislike = dislike,
        };

    public async Task<Result<FamilyGroupScreenDto>> Handle(GetCurrentFamilyGroupQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var expiredThisCall = await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value);
        var sessionExpired = await DetectSessionExpiredAsync(managerId.Value, expiredThisCall, cancellationToken);

        var settings = await _settings.GetAsync(cancellationToken);
        var group = await LoadRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
        {
            var hasConvertibleIndividual = await _db.Subscriptions.AsNoTracking().AnyAsync(
                x => x.CustomerUserId == managerId.Value
                     && x.FamilyGroupId == null
                     && (x.Status == SubscriptionStatus.Paid
                         || x.Status == SubscriptionStatus.Scheduled
                         || x.Status == SubscriptionStatus.Active
                         || x.Status == SubscriptionStatus.Frozen
                         || x.Status == SubscriptionStatus.SuspendedForReview),
                cancellationToken);
            if (hasConvertibleIndividual)
                return Error.Conflict(
                    "conversion.convert_required",
                    "Convert the individual subscription via POST .../convert-from-individual before creating a family.");

            var seeded = await CreateDraftGroupAsync(managerId.Value, name: null, familyPackageId: null, settings, cancellationToken);
            if (seeded.IsFailure) return seeded.Error;
            group = seeded.Value;
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await HydrateManagerFromProfileAsync(group, managerId.Value, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await MapScreenAsync(group, settings, cancellationToken, sessionExpired);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(UpsertFamilyGroupCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("family.name_required", "Family group name is required.");

        var settings = await _settings.GetAsync(cancellationToken);

        var active = await _db.FamilyGroups
            .AsNoTracking()
            .AnyAsync(x => x.ManagerCustomerUserId == managerId.Value && x.Status == FamilyGroupStatus.Active, cancellationToken);
        if (active)
            return Error.Conflict("family.active_exists", "An active family subscription already exists.");

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value && EditableStatuses.Contains(x.Status),
                cancellationToken);

        if (group is null)
        {
            var hasConvertibleIndividual = await _db.Subscriptions.AsNoTracking().AnyAsync(
                x => x.CustomerUserId == managerId.Value
                     && x.FamilyGroupId == null
                     && (x.Status == SubscriptionStatus.Paid
                         || x.Status == SubscriptionStatus.Scheduled
                         || x.Status == SubscriptionStatus.Active
                         || x.Status == SubscriptionStatus.Frozen
                         || x.Status == SubscriptionStatus.SuspendedForReview),
                cancellationToken);
            if (hasConvertibleIndividual)
                return Error.Conflict(
                    "conversion.convert_required",
                    "Convert the individual subscription via POST .../convert-from-individual before creating a family.");
        }

        if (group is not null)
        {
            group.Name = name;
            var packageAssign = AssignPackage(group, request.FamilyPackageId, settings);
            if (packageAssign.IsFailure) return packageAssign.Error;
            await HydrateManagerFromProfileAsync(group, managerId.Value, cancellationToken);
            FamilyDraftExpiry.Touch(group, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return await MapScreenAsync(group, settings, cancellationToken);
        }

        var created = await CreateDraftGroupAsync(managerId.Value, name, request.FamilyPackageId, settings, cancellationToken);
        if (created.IsFailure) return created.Error;
        await _db.SaveChangesAsync(cancellationToken);
        return await MapScreenAsync(created.Value, settings, cancellationToken);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(AddFamilyDraftMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family members can only be changed while checkout is not in progress.");

        var phone = request.Member.Phone?.Trim();
        var managerUser = await _users.FindByIdAsync(managerId.Value, cancellationToken);
        var managerPhone = managerUser?.PhoneNumber?.Trim();
        var isManagerPhone = !string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(managerPhone) && string.Equals(managerPhone, phone, StringComparison.OrdinalIgnoreCase);

        var settings = await _settings.GetAsync(cancellationToken);
        var maxAllowed = group.Status == FamilyGroupStatus.Active
            ? Math.Max(settings.MaxMembers, 12)
            : (FamilyPackageResolver.FindActive(settings, group.SelectedFamilyPackageId)?.MaxMembers ?? settings.MaxMembers);

        if (group.Members.Count >= maxAllowed)
            return Error.Conflict("family.max_members", "Family member limit reached for the selected package.");

        UserAccountDto? account = null;
        bool hasSeparateAccount = false;

        if (string.IsNullOrWhiteSpace(phone) || isManagerPhone)
        {
            if (string.IsNullOrWhiteSpace(request.Member.DisplayName))
                return Error.Validation("family.display_name_required", "Display name is required.");

            var shadowEmail = $"dep_{Guid.NewGuid():N}@mealmate.local";
            var created = await _users.CreateAsync(
                new CreateUserRequest(
                    shadowEmail,
                    null,
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    request.Member.DisplayName.Trim(),
                    UserType.Customer,
                    AccountStatus.Active),
                cancellationToken);
            if (created.IsFailure) return created.Error;
            account = created.Value;
            hasSeparateAccount = false;
        }
        else
        {
            if (group.Members.Any(m => string.Equals(m.PhoneNumber, phone, StringComparison.OrdinalIgnoreCase)))
                return Error.Conflict("family.already_in_family", "This phone is already in the family.");

            account = await _users.FindByPhoneAsync(phone, cancellationToken);
            if (account is null)
            {
                if (string.IsNullOrWhiteSpace(request.Member.DisplayName))
                    return Error.Validation("family.display_name_required", "Display name is required.");

                var created = await _users.CreateAsync(
                    new CreateUserRequest(
                        null,
                        phone,
                        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                        request.Member.DisplayName.Trim(),
                        UserType.Customer,
                        AccountStatus.Active),
                    cancellationToken);
                if (created.IsFailure) return created.Error;
                account = created.Value;
            }
            else
            {
                var occupied = await IsInAnotherFamilyAsync(account.Id, managerId.Value, cancellationToken);
                if (occupied)
                    return Error.Conflict("family.already_in_family", "This phone already belongs to another family.");
            }
            hasSeparateAccount = true;
        }

        var write = request.Member;
        if (string.IsNullOrWhiteSpace(write.DisplayName) && !string.IsNullOrWhiteSpace(account.FullName))
            write = write with { DisplayName = account.FullName };

        write = write with { Phone = phone };
        var validated = await ValidateMemberWriteAsync(managerId.Value, write, cancellationToken);
        if (validated.IsFailure) return validated.Error;

        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
        };
        ApplyWrite(member, write);
        member.PhoneNumber = phone;
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);
        group.Members.Add(member);
        _db.FamilyMembers.Add(member);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            HasSeparateAccount = hasSeparateAccount,
            LinkedCustomerUserId = account.Id,
        });
        if (!await _db.CustomerProfiles.AnyAsync(x => x.UserId == account.Id, cancellationToken))
            _db.CustomerProfiles.Add(new CustomerProfile { UserId = account.Id });
        await _db.SaveChangesAsync(cancellationToken);

        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(UpdateFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;

        var write = member.Role == FamilyMemberRole.Manager
            ? request.Member with { RelationshipCode = "self" }
            : request.Member;

        var validated = await ValidateMemberWriteAsync(managerId.Value, write, cancellationToken);
        if (validated.IsFailure) return validated.Error;

        ApplyWrite(member, write);
        if (member.Role == FamilyMemberRole.Manager)
        {
            member.Role = FamilyMemberRole.Manager;
            member.RelationshipCode = "self";
        }

        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);

        var settings = await _settings.GetAsync(cancellationToken);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(DeleteFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;
        if (member.Role == FamilyMemberRole.Manager)
            return Error.Conflict("family.cannot_delete_manager", "The manager member cannot be deleted.");
        if (group.Members.Count <= 1)
            return Error.Conflict("family.min_members", "At least one family member is required.");

        group.Members.Remove(member);
        _db.FamilyMembers.Remove(member);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var settings = await _settings.GetAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(SetFamilyMemberPlanCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family member plans can only be changed while checkout is not in progress.");

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;
        if (member.MemberSubscriptionId is not null && group.Status == FamilyGroupStatus.Active)
            return Error.Conflict("family.plan_locked", "Paid family members cannot change plan from this flow.");
        if (group.ConvertedFromSubscriptionId is not null && member.Role == FamilyMemberRole.Manager)
            return Error.Conflict(
                "conversion.manager_plan_locked",
                "The converted manager plan is locked. Change individual plan is not available in Phase 1.");

        var plan = request.Plan;
        if (!plan.HasAnyPatch)
            return Error.Validation("family.plan_empty", "At least one plan field is required.");

        var programId = plan.ProgramId.IsSpecified ? plan.ProgramId.Value : member.ProgramId;
        var bundlePatch = plan.ResolvedBundleId;
        var bundleId = bundlePatch.IsSpecified ? bundlePatch.Value : member.BundleId;
        var tierPatch = plan.ResolvedTier;
        var tier = tierPatch.IsSpecified ? tierPatch.Value : member.SelectedTier;
        var durationDays = plan.DurationDays.IsSpecified ? plan.DurationDays.Value : member.DurationDays;
        var includeFriday = member.IncludeFriday;
        if (plan.IncludeFriday.IsSpecified)
            includeFriday = plan.IncludeFriday.Value;

        if (!programId.HasValue)
        {
            bundleId = null;
            if (!tierPatch.IsSpecified)
                tier = null;
        }
        else if (bundleId.HasValue)
        {
            var pairOk = await _planEligibility.ValidateBundleForFamilyAsync(
                programId.Value, bundleId.Value, cancellationToken);
            if (pairOk.IsFailure)
            {
                if (bundlePatch.IsSpecified)
                    return pairOk.Error;

                // Retained bundle no longer compatible with program — clear dependents.
                bundleId = null;
                if (!tierPatch.IsSpecified)
                    tier = null;
            }
        }
        else if (!tierPatch.IsSpecified)
        {
            // No bundle yet: drop a previously retained tier, but keep an explicit tier
            // patch so validation can return family.plan_incomplete instead of silent drop.
            tier = null;
        }

        if (durationDays.HasValue
            && !plan.DurationDays.IsSpecified
            && !plan.IncludeFriday.IsSpecified)
        {
            var retainedDuration = await _planEligibility.ValidateDurationAsync(
                durationDays.Value, includeFriday, cancellationToken);
            if (retainedDuration.IsFailure)
                durationDays = null;
        }

        if (programId.HasValue && bundleId.HasValue && tier.HasValue && !tierPatch.IsSpecified)
        {
            var retainedTier = await _planEligibility.ValidateTierInCatalogAsync(
                programId.Value, bundleId.Value, tier.Value, cancellationToken);
            if (retainedTier.IsFailure)
                tier = null;
        }

        if (programId.HasValue)
        {
            var programResult = await _planEligibility.ValidateProgramForFamilyAsync(
                programId.Value, cancellationToken);
            if (programResult.IsFailure) return programResult.Error;
        }

        if (bundleId.HasValue)
        {
            if (!programId.HasValue)
                return Error.Validation("family.program_required", "Select a program before choosing a bundle.");

            var bundleResult = await _planEligibility.ValidateBundleForFamilyAsync(
                programId.Value, bundleId.Value, cancellationToken);
            if (bundleResult.IsFailure) return bundleResult.Error;
        }

        if ((plan.DurationDays.IsSpecified || plan.IncludeFriday.IsSpecified) && durationDays.HasValue)
        {
            var durationResult = await _planEligibility.ValidateDurationAsync(
                durationDays.Value, includeFriday, cancellationToken);
            if (durationResult.IsFailure) return durationResult.Error;
        }

        if (tierPatch.IsSpecified && tier.HasValue)
        {
            if (!programId.HasValue || !bundleId.HasValue)
                return Error.Validation("family.plan_incomplete", "Program and bundle are required before choosing a tier.");

            var tierResult = await _planEligibility.ValidateTierInCatalogAsync(
                programId.Value, bundleId.Value, tier.Value, cancellationToken);
            if (tierResult.IsFailure) return tierResult.Error;
        }

        if (programId.HasValue && bundleId.HasValue && tier.HasValue && durationDays.HasValue)
        {
            var full = await _planEligibility.ValidateAsync(
                programId.Value,
                bundleId.Value,
                tier.Value,
                durationDays.Value,
                includeFriday,
                cancellationToken);
            if (full.IsFailure) return full.Error;
        }

        member.ProgramId = programId;
        member.BundleId = bundleId;
        member.SelectedTier = tier;
        member.DurationDays = durationDays;
        member.IncludeFriday = includeFriday;
        if (plan.StartDate.IsSpecified)
            member.StartDate = plan.StartDate.Value;
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);

        var settings = await _settings.GetAsync(cancellationToken);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public async Task<Result<FamilyCheckoutSummaryDto>> Handle(GetFamilyCheckoutSummaryQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value);
        var group = await LoadRosterGroupAsync(managerId.Value, cancellationToken);
        if (group is null)
            return SessionExpiredError();

        var settings = await _settings.GetAsync(cancellationToken);
        var quoteResult = await _quote.QuoteAsync(group, cancellationToken);
        if (quoteResult.IsFailure) return quoteResult.Error;
        var quote = quoteResult.Value;

        var members = group.Members
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();

        var selected = ResolveSelectedMember(members, request.MemberId);
        if (selected is null)
            return Error.NotFound("family.not_found", "Family member was not found.");

        var bundleIds = members.Where(m => m.BundleId.HasValue).Select(m => m.BundleId!.Value).Distinct().ToList();
        var programIds = members.Where(m => m.ProgramId.HasValue).Select(m => m.ProgramId!.Value).Distinct().ToList();

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(b => bundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var programs = await _db.Programs.AsNoTracking()
            .Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var priceByMember = quote.Lines.ToDictionary(l => l.MemberId, l => l.MemberPrice);

        var overview = members.Select(m =>
        {
            bundles.TryGetValue(m.BundleId ?? Guid.Empty, out var bundle);
            var breakfast = (bundle?.BreakfastCount ?? 0) > 0;
            var lunch = (bundle?.LunchCount ?? 0) > 0;
            var snack = (bundle?.SnackCount ?? 0) > 0;
            var dinner = (bundle?.DinnerCount ?? 0) > 0;
            return new FamilyMealOverviewRowDto(
                m.Id,
                m.DisplayName,
                m.Role,
                IsManager: m.Role == FamilyMemberRole.Manager,
                m.SetupStatus,
                IsReady: m.SetupStatus == FamilyMemberSetupStatus.Ready,
                Breakfast: breakfast,
                Lunch: lunch,
                Snack: snack,
                Dinner: dinner,
                MealsPerDay: bundle?.MealsPerDay ?? 0);
        }).ToList();

        var packageResult = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        if (packageResult.IsFailure)
            return packageResult.Error;
        var payable = FamilyCheckoutGate.PayableMembers(members);
        var allergiesCompleted = await FamilyCheckoutAllergyLookup.CompletedMemberIdsAsync(
            _db, group, payable, cancellationToken);
        var (canCheckout, blockReason) = FamilyCheckoutGate.Evaluate(
            group, members, packageResult.Value, quote, allergiesCompleted);

        var managerPrepaid = group.ConvertedFromSubscriptionId is not null;
        var managerSubscriptionId = group.ConvertedFromSubscriptionId
            ?? members.FirstOrDefault(m => m.Role == FamilyMemberRole.Manager)?.MemberSubscriptionId;
        var newMembersCount = members.Count(m => !(managerPrepaid && m.Role == FamilyMemberRole.Manager));
        // Quote.Subtotal is already payable-only when manager is prepaid.
        var newMembersSubtotal = quote.Subtotal;

        return new FamilyCheckoutSummaryDto(
            group.Id,
            group.Name,
            selected.Id,
            MapMemberSummary(selected, programs, bundles, priceByMember),
            members.Select(m => MapMember(m)).ToList(),
            quote.Subtotal,
            quote.FamilyDiscountPercent,
            quote.FamilyDiscountAmount,
            quote.FinalPrice,
            quote.CurrencyCode,
            overview,
            canCheckout,
            blockReason,
            CheckoutEndpoint,
            ManagerPrepaid: managerPrepaid,
            ManagerSubscriptionId: managerSubscriptionId,
            NewMembersCount: newMembersCount,
            NewMembersSubtotal: newMembersSubtotal);
    }

    public async Task<Result<bool>> Handle(AbandonFamilyDraftCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value);
        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value && EditableStatuses.Contains(x.Status),
                cancellationToken);
        if (group is null)
            return SessionExpiredError();
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Only a draft family group can be abandoned.");

        if (group.ConvertedFromSubscriptionId is Guid convertedSubId)
        {
            var subscription = await _db.Subscriptions
                .FirstOrDefaultAsync(x => x.Id == convertedSubId && x.CustomerUserId == managerId.Value, cancellationToken);
            if (subscription is not null)
            {
                subscription.FamilyGroupId = null;
                subscription.FamilyMemberId = null;
            }
        }
        else
        {
            // Non-converted drafts may already have linked PendingPayment leftovers from a prior failed checkout.
            var linked = await _db.Subscriptions
                .Where(x => x.FamilyGroupId == group.Id)
                .ToListAsync(cancellationToken);
            foreach (var sub in linked)
            {
                if (sub.Status == SubscriptionStatus.PendingPayment || sub.Status == SubscriptionStatus.PaymentFailed)
                {
                    sub.FamilyGroupId = null;
                    sub.FamilyMemberId = null;
                }
            }
        }

        foreach (var member in group.Members.ToList())
        {
            member.MemberSubscriptionId = null;
            _db.FamilyMembers.Remove(member);
        }

        _db.FamilyGroups.Remove(group);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Result<Guid> RequireCustomerId()
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid userId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");
        return userId;
    }

    private static Error SessionExpiredError()
        => Error.Conflict(MessageCodes.Family.SessionExpired, FamilyDraftExpiry.SessionEndedMessageAr);

    private async Task<bool> DetectSessionExpiredAsync(
        Guid managerId,
        int expiredThisCall,
        CancellationToken cancellationToken)
    {
        if (expiredThisCall > 0)
            return true;

        var cutoff = FamilyDraftExpiry.LookbackUtc(_clock.UtcNow);
        if (cutoff is null)
            return false;

        return await _db.FamilyGroups.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(
                g => g.ManagerCustomerUserId == managerId
                     && g.IsDeleted
                     && g.DeletedAtUtc != null
                     && g.DeletedAtUtc >= cutoff,
                cancellationToken);
    }

    private async Task<Result<FamilyGroup>> LoadLiveRosterGroupTrackedAsync(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId);
        var group = await LoadRosterGroupTrackedAsync(managerId, cancellationToken);
        if (group is null)
            return SessionExpiredError();
        return group;
    }

    private async Task<Result<FamilyMember>> RequireMemberAsync(
        FamilyGroup group,
        Guid memberId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var member = group.Members.FirstOrDefault(x => x.Id == memberId);
        if (member is not null)
            return member;

        var stale = await _db.FamilyMembers.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(
                m => m.Id == memberId
                     && m.IsDeleted
                     && m.FamilyGroup.ManagerCustomerUserId == managerId,
                cancellationToken);
        if (stale)
            return SessionExpiredError();
        return Error.NotFound("family.not_found", "Family member was not found.");
    }

    private async Task<FamilyGroup?> LoadRosterGroupAsync(Guid managerId, CancellationToken cancellationToken)
        => await _db.FamilyGroups.AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId && RosterStatuses.Contains(x.Status),
                cancellationToken);

    private async Task<FamilyGroup?> LoadRosterGroupTrackedAsync(Guid managerId, CancellationToken cancellationToken)
        => await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId && RosterStatuses.Contains(x.Status),
                cancellationToken);

    private async Task<bool> IsInAnotherFamilyAsync(Guid customerUserId, Guid currentManagerId, CancellationToken cancellationToken)
    {
        var isOtherManager = await _db.FamilyGroups.AsNoTracking()
            .AnyAsync(
                g => g.ManagerCustomerUserId == customerUserId
                     && g.ManagerCustomerUserId != currentManagerId
                     && g.Status != FamilyGroupStatus.Cancelled,
                cancellationToken);
        if (isOtherManager)
            return true;

        return await (
            from access in _db.FamilyMemberAccesses.AsNoTracking()
            join member in _db.FamilyMembers.AsNoTracking() on access.FamilyMemberId equals member.Id
            join family in _db.FamilyGroups.AsNoTracking() on member.FamilyGroupId equals family.Id
            where access.LinkedCustomerUserId == customerUserId
                  && access.RemovedAtUtc == null
                  && family.ManagerCustomerUserId != currentManagerId
                  && family.Status != FamilyGroupStatus.Cancelled
            select access.Id).AnyAsync(cancellationToken);
    }

    private async Task<Result> ValidateMemberWriteAsync(Guid managerId, FamilyMemberWriteDto write, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(write.DisplayName))
            return Error.Validation("family.display_name_required", "Display name is required.");
        if (write.AgeYears is < 1 or > 120)
            return Error.Validation("family.age_invalid", "Age must be between 1 and 120.");
        if (FamilyCatalog.Relationships().All(x => x.Code != write.RelationshipCode))
            return Error.Validation("family.relationship_invalid", "Relationship code is invalid.");

        if (write.DeliveryAddressId is Guid addressId && addressId != Guid.Empty)
        {
            var address = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == addressId, cancellationToken);
            if (address is null)
                return Error.Validation("family.address_invalid", "Delivery address must belong to the manager or the family member.");
            if (address.UserId != managerId)
            {
                var linkedOk = await (
                    from access in _db.FamilyMemberAccesses.AsNoTracking()
                    join member in _db.FamilyMembers.AsNoTracking() on access.FamilyMemberId equals member.Id
                    join family in _db.FamilyGroups.AsNoTracking() on member.FamilyGroupId equals family.Id
                    where family.ManagerCustomerUserId == managerId
                          && access.LinkedCustomerUserId == address.UserId
                          && access.RemovedAtUtc == null
                    select access.Id).AnyAsync(cancellationToken);
                if (!linkedOk)
                    return Error.Validation("family.address_invalid", "Delivery address must belong to the manager or the family member.");
            }
        }

        return Result.Success();
    }

    private static void ApplyWrite(FamilyMember member, FamilyMemberWriteDto write)
    {
        member.DisplayName = write.DisplayName.Trim();
        member.RelationshipCode = write.RelationshipCode;
        member.Gender = write.Gender;
        member.AgeYears = write.AgeYears;
        member.NutritionalGoalCode = string.Empty;
        member.ActivityLevel = FamilyActivityLevel.Sedentary;
        // Keep existing address when Flutter sends "" / null (common missing-picker case).
        if (write.DeliveryAddressId is Guid addressId && addressId != Guid.Empty)
            member.DeliveryAddressId = addressId;
        if (!string.IsNullOrWhiteSpace(write.Phone))
            member.PhoneNumber = write.Phone.Trim();
        if (write.Occupation is not null)
            member.Occupation = string.IsNullOrWhiteSpace(write.Occupation) ? null : write.Occupation.Trim();
    }

    private async Task<FamilyGroupScreenDto> MapScreenAsync(
        FamilyGroup group,
        FamilyPricingSettings settings,
        CancellationToken cancellationToken,
        bool sessionExpired = false)
    {
        var resolved = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        var package = resolved.IsSuccess ? resolved.Value : FamilyPackageResolver.DefaultActive(settings);

        var memberIds = group.Members.Select(x => x.Id).ToList();
        var accesses = memberIds.Count == 0
            ? []
            : await _db.FamilyMemberAccesses.AsNoTracking()
                .Where(x => memberIds.Contains(x.FamilyMemberId) && x.RemovedAtUtc == null)
                .ToListAsync(cancellationToken);
        var linkedUserByMember = accesses
            .Where(x => x.LinkedCustomerUserId.HasValue)
            .ToDictionary(x => x.FamilyMemberId, x => x.LinkedCustomerUserId!.Value);

        Guid? LinkedUserId(FamilyMember member)
        {
            if (linkedUserByMember.TryGetValue(member.Id, out var linked))
                return linked;
            return member.Role == FamilyMemberRole.Manager ? group.ManagerCustomerUserId : null;
        }

        var addressIds = group.Members
            .Where(x => x.DeliveryAddressId.HasValue)
            .Select(x => x.DeliveryAddressId!.Value)
            .Distinct()
            .ToList();
        var addresses = addressIds.Count == 0
            ? []
            : await _db.CustomerAddresses.AsNoTracking()
                .Where(x => addressIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var addressById = addresses.ToDictionary(x => x.Id);

        var userIds = group.Members.Select(LinkedUserId).OfType<Guid>().Distinct().ToList();
        var profiles = userIds.Count == 0
            ? []
            : await _db.CustomerProfiles.AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .ToListAsync(cancellationToken);
        var profileByUser = profiles.ToDictionary(x => x.UserId);

        var allergyRows = userIds.Count == 0
            ? []
            : await _db.CustomerAllergyPreferences.AsNoTracking()
                .Where(x => userIds.Contains(x.CustomerUserId))
                .OrderBy(x => x.AllergenCode)
                .ToListAsync(cancellationToken);
        var dietaryRows = userIds.Count == 0
            ? []
            : await _db.CustomerDietaryPreferences.AsNoTracking()
                .Where(x => userIds.Contains(x.CustomerUserId))
                .OrderBy(x => x.PreferenceCode)
                .ToListAsync(cancellationToken);

        var allergenDefs = await _db.AllergenDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var dietaryDefs = await _db.FoodPreferenceDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return new(
            group.Id,
            group.Name,
            group.Status,
            package?.Id ?? group.SelectedFamilyPackageId,
            package?.NameAr,
            package?.NameEn,
            package?.MinMembers ?? settings.MinMembersForDiscount,
            package?.MaxMembers ?? settings.MaxMembers,
            package?.DiscountPercent ?? settings.DiscountPercent,
            group.Members
                .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(x => x.CreatedAtUtc)
                .Select(member =>
                {
                    var userId = LinkedUserId(member);
                    profileByUser.TryGetValue(userId ?? Guid.Empty, out var profile);
                    CustomerAddressDto? deliveryAddress = null;
                    if (member.DeliveryAddressId is Guid addressId && addressById.TryGetValue(addressId, out var address))
                        deliveryAddress = MapResumeAddress(address, profile?.ActiveDeliveryAddressId);

                    var allergies = allergyRows
                        .Where(x => userId.HasValue && x.CustomerUserId == userId.Value && !x.IsDislike)
                        .Select(x => MapAllergyItem(x, allergenDefs))
                        .ToList();
                    var disliked = allergyRows
                        .Where(x => userId.HasValue && x.CustomerUserId == userId.Value && x.IsDislike)
                        .Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity))
                        .ToList();
                    var dietary = dietaryRows
                        .Where(x => userId.HasValue && x.CustomerUserId == userId.Value)
                        .Select(x => dietaryDefs.TryGetValue(x.PreferenceCode, out var definition)
                            ? new DietaryPreferenceOptionDto(x.PreferenceCode, definition.NameAr, definition.NameEn, true)
                            : new DietaryPreferenceOptionDto(x.PreferenceCode, x.PreferenceCode, x.PreferenceCode, true))
                        .ToList();

                    return MapMember(
                        member,
                        deliveryAddress,
                        allergies,
                        profile?.AllergiesCompletedAtUtc is not null,
                        disliked,
                        dietary,
                        profile?.DietaryPreferencesCompletedAtUtc is not null);
                })
                .ToList(),
            ResolveCurrentStep(group),
            sessionExpired,
            group.Status == FamilyGroupStatus.Draft ? FamilyDraftExpiry.ExpiresAtUtc(group) : null,
            sessionExpired ? FamilyDraftExpiry.SessionEndedMessageAr : null);
    }

    private static IReadOnlyList<FamilySizePackageDto> MapPackages(FamilyPricingSettings settings)
        => FamilyPackageResolver.ActivePackages(settings)
            .Select(x => new FamilySizePackageDto(
                x.Id,
                x.NameAr,
                x.NameEn,
                x.MinMembers,
                x.MaxMembers,
                x.DiscountPercent,
                x.SortOrder,
                x.ImageStorageKey,
                CatalogImageStorage.ToContentUrl(x.ImageStorageKey)))
            .ToList();

    private static Result AssignPackage(FamilyGroup group, Guid? familyPackageId, FamilyPricingSettings settings)
    {
        if (familyPackageId is null || familyPackageId == Guid.Empty)
        {
            if (group.SelectedFamilyPackageId is Guid existing)
            {
                var keep = FamilyPackageResolver.FindActive(settings, existing);
                if (keep is null)
                    return Error.Validation("family.package_not_found", "Selected family package is not available.");
                if (group.Members.Count > keep.MaxMembers)
                    return Error.Conflict("family.max_members", "Current members exceed the selected package maximum.");
                return Result.Success();
            }

            var fallback = FamilyPackageResolver.DefaultActive(settings);
            if (fallback is null)
                return Error.Validation("family.package_required", "No active family size package is configured.");
            group.SelectedFamilyPackageId = fallback.Id;
            group.DiscountPercentSnapshot = fallback.DiscountPercent;
            return Result.Success();
        }

        var package = FamilyPackageResolver.FindActive(settings, familyPackageId);
        if (package is null)
            return Error.Validation("family.package_not_found", "Selected family package is not available.");
        if (group.Members.Count > package.MaxMembers)
            return Error.Conflict("family.max_members", "Current members exceed the selected package maximum.");

        group.SelectedFamilyPackageId = package.Id;
        group.DiscountPercentSnapshot = package.DiscountPercent;
        return Result.Success();
    }

    private async Task<Result<FamilyGroup>> CreateDraftGroupAsync(
        Guid managerId,
        string? name,
        Guid? familyPackageId,
        FamilyPricingSettings settings,
        CancellationToken cancellationToken)
    {
        FamilySizePackageSettings package;
        if (familyPackageId is Guid requestedId && requestedId != Guid.Empty)
        {
            var found = FamilyPackageResolver.FindActive(settings, requestedId);
            if (found is null)
                return Error.Validation("family.package_not_found", "Selected family package is not available.");
            package = found;
        }
        else
        {
            var resolved = FamilyPackageResolver.ResolveForGroup(settings, null);
            if (resolved.IsFailure) return resolved.Error;
            package = resolved.Value;
        }

        var snapshot = await LoadManagerProfileSnapshotAsync(managerId, cancellationToken);
        var familyName = string.IsNullOrWhiteSpace(name)
            ? DefaultFamilyName(snapshot.DisplayName)
            : name.Trim();

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = familyName,
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = package.Id,
            DiscountPercentSnapshot = package.DiscountPercent,
        };

        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = snapshot.DisplayName,
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 0,
            NutritionalGoalCode = string.Empty,
            ActivityLevel = FamilyActivityLevel.Sedentary,
        };
        FamilyManagerProfileHydration.Apply(
            managerMember,
            snapshot.DisplayName,
            snapshot.AgeYears,
            snapshot.DefaultAddressId);
        group.Members.Add(managerMember);
        _db.FamilyGroups.Add(group);
        return group;
    }

    private async Task HydrateManagerFromProfileAsync(FamilyGroup group, Guid managerId, CancellationToken cancellationToken)
    {
        var manager = group.Members.FirstOrDefault(m => m.Role == FamilyMemberRole.Manager);
        if (manager is null)
        {
            manager = new FamilyMember
            {
                FamilyGroupId = group.Id,
                Role = FamilyMemberRole.Manager,
                DisplayName = "عضو",
                RelationshipCode = "self",
                Gender = FamilyMemberGender.Male,
                AgeYears = 0,
                NutritionalGoalCode = string.Empty,
                ActivityLevel = FamilyActivityLevel.Sedentary,
            };
            group.Members.Add(manager);
        }

        var snapshot = await LoadManagerProfileSnapshotAsync(managerId, cancellationToken);
        FamilyManagerProfileHydration.Apply(
            manager,
            snapshot.DisplayName,
            snapshot.AgeYears,
            snapshot.DefaultAddressId);
    }

    private async Task<(string DisplayName, int AgeYears, Guid? DefaultAddressId)> LoadManagerProfileSnapshotAsync(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var account = await _users.FindByIdAsync(managerId, cancellationToken);
        var displayName = string.IsNullOrWhiteSpace(account?.FullName) ? "عضو" : account!.FullName!.Trim();

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == managerId, cancellationToken);
        var age = FamilyManagerProfileHydration.AgeYearsFromDateOfBirth(profile?.DateOfBirth, _clock.TodayUtc);

        var defaultAddressId = await _db.CustomerAddresses.AsNoTracking()
            .Where(a => a.UserId == managerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAtUtc)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return (displayName, age, defaultAddressId);
    }

    private static string DefaultFamilyName(string displayName)
        => string.IsNullOrWhiteSpace(displayName) || displayName == "عضو"
            ? "عائلتي"
            : $"عائلة {displayName}";

    private static FamilyMemberCardDto MapMember(
        FamilyMember member,
        CustomerAddressDto? deliveryAddress = null,
        IReadOnlyList<FoodPreferenceItemDto>? allergies = null,
        bool allergiesCompleted = false,
        IReadOnlyList<FoodPreferenceItemDto>? dislikedFoods = null,
        IReadOnlyList<DietaryPreferenceOptionDto>? dietaryPreferences = null,
        bool dietaryCompleted = false)
        => new(
            member.Id,
            member.Role,
            member.DisplayName,
            member.RelationshipCode,
            member.Gender,
            member.AgeYears,
            member.DeliveryAddressId,
            member.SetupStatus,
            member.ProgramId,
            member.BundleId,
            member.SelectedTier,
            member.DurationDays,
            member.IncludeFriday,
            member.Role == FamilyMemberRole.Manager,
            member.PhoneNumber,
            member.Occupation,
            member.StartDate,
            deliveryAddress,
            allergies ?? [],
            allergiesCompleted,
            dislikedFoods ?? [],
            dietaryPreferences ?? [],
            dietaryCompleted);

    private static CustomerAddressDto MapResumeAddress(CustomerAddress address, Guid? activeDeliveryAddressId)
        => new(
            address.Id,
            address.Label,
            address.AddressLine,
            address.RegionId,
            address.Latitude,
            address.Longitude,
            address.Notes,
            address.IsDefault,
            address.NotifyOnDelivery,
            address.Governorate,
            address.Area,
            address.Block,
            address.Street,
            address.Avenue,
            address.Building,
            address.Unit,
            address.Floor,
            address.BlockRegionId,
            IsDeliveryActive: activeDeliveryAddressId == address.Id);

    private static FoodPreferenceItemDto MapAllergyItem(
        CustomerAllergyPreference row,
        IReadOnlyDictionary<string, AllergenDefinition> definitions)
        => definitions.TryGetValue(row.AllergenCode, out var definition)
            ? new FoodPreferenceItemDto(row.AllergenCode, definition.NameAr, definition.NameEn, row.Severity)
            : new FoodPreferenceItemDto(row.AllergenCode, row.AllergenNameAr, row.AllergenNameEn, row.Severity);

    private static FamilyMember? ResolveSelectedMember(IReadOnlyList<FamilyMember> members, Guid? memberId)
    {
        if (memberId is Guid id)
            return members.FirstOrDefault(m => m.Id == id);
        return members.FirstOrDefault(m => m.Role == FamilyMemberRole.Manager) ?? members.FirstOrDefault();
    }

    private static FamilyMemberSummaryDto MapMemberSummary(
        FamilyMember member,
        IReadOnlyDictionary<Guid, Program> programs,
        IReadOnlyDictionary<Guid, Bundle> bundles,
        IReadOnlyDictionary<Guid, decimal> priceByMember)
    {
        programs.TryGetValue(member.ProgramId ?? Guid.Empty, out var program);
        bundles.TryGetValue(member.BundleId ?? Guid.Empty, out var bundle);
        priceByMember.TryGetValue(member.Id, out var memberPrice);
        var (roleAr, roleEn) = MapRole(member.Role);
        var (tierAr, tierEn) = MapTierNames(member.SelectedTier);
        var durationDays = member.DurationDays ?? 0;
        var breakfastCount = bundle?.BreakfastCount ?? 0;
        var lunchCount = bundle?.LunchCount ?? 0;
        var snackCount = bundle?.SnackCount ?? 0;
        var dinnerCount = bundle?.DinnerCount ?? 0;

        return new FamilyMemberSummaryDto(
            member.Id,
            member.DisplayName,
            member.Role,
            IsManager: member.Role == FamilyMemberRole.Manager,
            roleAr,
            roleEn,
            member.SetupStatus,
            IsReady: member.SetupStatus == FamilyMemberSetupStatus.Ready,
            member.ProgramId,
            program?.NameAr ?? string.Empty,
            program?.NameEn ?? string.Empty,
            member.BundleId,
            bundle?.NameAr ?? string.Empty,
            bundle?.NameEn ?? string.Empty,
            member.SelectedTier,
            tierAr,
            tierEn,
            durationDays,
            DurationLabelAr: durationDays > 0 ? $"{durationDays} يوم" : string.Empty,
            DurationLabelEn: durationDays > 0 ? $"{durationDays} days" : string.Empty,
            member.IncludeFriday,
            Breakfast: breakfastCount > 0,
            Lunch: lunchCount > 0,
            Snack: snackCount > 0,
            Dinner: dinnerCount > 0,
            MealsPerDay: bundle?.MealsPerDay ?? 0,
            breakfastCount,
            lunchCount,
            snackCount,
            dinnerCount,
            memberPrice);
    }

    private static (string? TierAr, string? TierEn) MapTierNames(RestaurantTier? tier)
        => tier switch
        {
            RestaurantTier.Basic => ("أساسي", "Basic"),
            RestaurantTier.Platinum => ("بلاتينيوم", "Platinum"),
            RestaurantTier.Elite => ("إيليت", "Elite"),
            _ => (null, null),
        };

    private static (string RoleAr, string RoleEn) MapRole(FamilyMemberRole role)
        => role switch
        {
            FamilyMemberRole.Manager => ("عضو رئيسي", "Primary member"),
            _ => ("عضو", "Member"),
        };

    /// <summary>
    /// 1 = profile setup, 2 = partial plans, 3 = all ready (pre-payment), 4 = checkout/active.
    /// </summary>
    private static int ResolveCurrentStep(FamilyGroup group)
    {
        if (group.Status is FamilyGroupStatus.CheckoutPending or FamilyGroupStatus.Active)
            return 4;

        var members = group.Members.ToList();
        if (members.Count == 0) return 1;

        var allReady = members.All(m => m.SetupStatus == FamilyMemberSetupStatus.Ready);
        if (allReady) return 3;

        var anyPlanChosen = members.Any(m =>
            m.ProgramId.HasValue || m.BundleId.HasValue || m.SelectedTier.HasValue || m.DurationDays.HasValue);
        return anyPlanChosen ? 2 : 1;
    }
}
