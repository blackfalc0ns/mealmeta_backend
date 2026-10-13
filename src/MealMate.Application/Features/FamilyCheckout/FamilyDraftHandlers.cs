using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Helpers;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    ICommandHandler<SetFamilyMemberStartDateCommand, FamilyGroupScreenDto>,
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
    ICommandHandler<AbandonFamilyDraftCommand, bool>,
    ICommandHandler<UpgradeFamilyCapacityCommand, FamilyCapacityUpgradeDto>
{
    private const string CheckoutEndpoint = "POST /api/v1/customer/family-subscription-flow/checkout";

    private static readonly FamilyGroupStatus[] EditableStatuses =
    [
        FamilyGroupStatus.Draft,
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
    private readonly ICheckoutTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly ILocalizedMessageProvider _messages;
    private readonly FamilyInvoiceOptions _invoiceOptions;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IRestaurantEligibilityService? _restaurantEligibility;
    private readonly IRestaurantTierClassificationService _tiers;
    private readonly IRestaurantDiversificationCalculator _diversification;
    private readonly ICapacityHoldExpiryService? _holdExpiry;

    public FamilyDraftHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyPricingSettingsReader settings,
        IFamilyPlanEligibilityService planEligibility,
        IFamilyQuoteService quote,
        IDateTimeProvider clock,
        ICustomerAddressBook addresses, ICheckoutTransactionExecutor? transactions = null, IApplicationLockManager? locks = null,
        ILocalizedMessageProvider? messages = null, IOptions<FamilyInvoiceOptions>? invoiceOptions = null,
        ISubscriptionScheduleService? schedule = null, IRestaurantEligibilityService? restaurantEligibility = null,
        IRestaurantTierClassificationService? tiers = null, IRestaurantDiversificationCalculator? diversification = null,
        ICapacityHoldExpiryService? capacityHoldExpiry = null)
        => (_db, _currentUser, _users, _settings, _planEligibility, _quote, _clock, _addresses, _transactions, _locks, _messages, _invoiceOptions, _schedule, _restaurantEligibility, _tiers, _diversification, _holdExpiry)
            = (db, currentUser, users, settings, planEligibility, quote, clock, addresses, transactions, locks,
                messages ?? NullLocalizedMessageProvider.Instance, invoiceOptions?.Value ?? new FamilyInvoiceOptions(),
                schedule ?? new SubscriptionScheduleService(clock), restaurantEligibility,
                tiers ?? new RestaurantTierClassificationService(db, clock), diversification ?? new RestaurantDiversificationCalculator(), capacityHoldExpiry);

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
            settings.MinMembersForDiscount,
            FamilyInvoiceGate.SupportedContractVersion,
            _invoiceOptions.MinimumContractVersion);
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

    public Task<Result<FamilyCapacityUpgradeDto>> Handle(UpgradeFamilyCapacityCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => UpgradeCapacityCoreAsync(request.Request, ct), cancellationToken);

    private async Task<Result<FamilyCapacityUpgradeDto>> UpgradeCapacityCoreAsync(FamilyCapacityUpgradeRequest request, CancellationToken ct)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;
        if (request.FamilyPackageId == Guid.Empty)
            return Error.Validation("family.package_required", "A family package is required.");

        var group = await _db.FamilyGroups.Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.ManagerCustomerUserId == managerId.Value && x.Status == FamilyGroupStatus.Active, ct);
        if (group is null) return Error.NotFound("family.not_found", "Active family subscription was not found.");
        if (group.SelectedFamilyPackageId is not Guid currentId)
            return Error.Conflict("family.package_required", "The active family package snapshot is missing.");
        if (request.ExpectedCurrentPackageId is Guid expected && expected != currentId)
            return Error.Conflict("family.package_stale", "The family package changed; refresh and try again.");

        var settings = await _settings.GetAsync(ct);
        var current = FamilyPackageResolver.FindActive(settings, currentId);
        var target = FamilyPackageResolver.FindActive(settings, request.FamilyPackageId);
        if (current is null || target is null) return Error.Validation("family.package_not_found", "Selected family package is not available.");
        if (target.MaxMembers <= current.MaxMembers)
            return Error.Validation("family.package_upgrade_required", "The new package must provide more seats.");

        var participation = await new FamilyParticipationContextReader(_db, _clock, _settings).GetAsync(group, null, ct);
        if (participation.OccupiedSeats > target.MaxMembers)
            return Error.Conflict("family.max_members", "Current family members exceed the selected package maximum.");

        group.SelectedFamilyPackageId = target.Id;
        group.DiscountPercentSnapshot = target.DiscountPercent;
        await SaveDraftChangesAsync(ct);
        return new FamilyCapacityUpgradeDto(group.Id, current.Id, target.Id, participation.OccupiedSeats,
            target.MaxMembers, Math.Max(0, target.MaxMembers - participation.OccupiedSeats), target.DiscountPercent);
    }

    public async Task<Result<FamilyMemberLookupDto>> Handle(
        LookupFamilyMemberByPhoneQuery request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var phone = PhoneNumberNormalizer.Normalize(request.Phone);
        if (string.IsNullOrWhiteSpace(phone))
            return Error.Validation("family.phone_required", "Phone number is required.");

        var candidates = PhoneNumberNormalizer.GetLookupCandidates(phone);
        UserAccountDto? account = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (account is null)
        {
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate, phone, StringComparison.OrdinalIgnoreCase))
                {
                    account = await _users.FindByPhoneAsync(candidate, cancellationToken);
                    if (account is not null) break;
                }
            }
        }

        if (account is null)
            return new FamilyMemberLookupDto(false, null, null, null);

        var occupied = await IsInAnotherFamilyAsync(account.Id, managerId.Value, cancellationToken);
        if (occupied)
            return Error.Conflict("family.already_in_family", "This phone already belongs to another family.");

        var existing = await (
            from m in _db.FamilyMembers.AsNoTracking()
            where (m.PhoneNumber != null && candidates.Contains(m.PhoneNumber))
               || (from a in _db.FamilyMemberAccesses.AsNoTracking()
                   where a.LinkedCustomerUserId == account.Id && a.RemovedAtUtc == null
                   select a.FamilyMemberId).Contains(m.Id)
            select new { m.DisplayName, m.AgeYears, m.Gender }
        ).FirstOrDefaultAsync(cancellationToken);

        int? ageYears = (existing?.AgeYears is >= 1 and <= 120) ? existing.AgeYears : null;
        if (ageYears is null)
        {
            var profile = await _db.CustomerProfiles.AsNoTracking()
                .Where(p => p.UserId == account.Id)
                .Select(p => new { p.DateOfBirth })
                .FirstOrDefaultAsync(cancellationToken);

            if (profile?.DateOfBirth is not null)
            {
                var calculated = FamilyManagerProfileHydration.AgeYearsFromDateOfBirth(profile.DateOfBirth, _clock.TodayUtc);
                if (calculated is >= 1 and <= 120)
                    ageYears = calculated;
            }
        }

        FamilyMemberGender? gender = (existing?.Gender is FamilyMemberGender.Male or FamilyMemberGender.Female)
            ? existing.Gender
            : null;

        return new FamilyMemberLookupDto(
            true,
            account.FullName ?? existing?.DisplayName,
            ageYears,
            gender);
    }

    public Task<Result<bool>> Handle(SaveFamilyMemberAllergiesCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken, preferenceMemberId: request.MemberId);

    private async Task<Result<bool>> HandleCore(SaveFamilyMemberAllergiesCommand request, CancellationToken cancellationToken)
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

        if (FamilyPreferenceSelectionCodes.HasConflictingNone(request.Allergies.Select(x => x.Code), FamilyPreferenceSelectionCodes.NoAllergies))
            return Error.Validation("family.preference_none_conflict");
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
        await SaveDraftChangesAsync(cancellationToken);
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

    public Task<Result<bool>> Handle(SaveFamilyMemberDislikedFoodsCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken, preferenceMemberId: request.MemberId);

    private async Task<Result<bool>> HandleCore(SaveFamilyMemberDislikedFoodsCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolvePreferenceMemberAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var replaced = await ReplaceDislikedFoodsAsync(target.Value.UserId, request.DislikedFoods, cancellationToken);
        if (replaced.IsFailure) return replaced.Error;
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await SaveDraftChangesAsync(cancellationToken);
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

    public Task<Result<bool>> Handle(SaveFamilyMemberDietaryCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken, preferenceMemberId: request.MemberId);

    private async Task<Result<bool>> HandleCore(SaveFamilyMemberDietaryCommand request, CancellationToken cancellationToken)
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
        await SaveDraftChangesAsync(cancellationToken);
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

    public Task<Result<CustomerAddressDto>> Handle(CreateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<CustomerAddressDto>> HandleCore(CreateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
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
        await SaveDraftChangesAsync(cancellationToken);
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

    public Task<Result<CustomerAddressDto>> Handle(UpdateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<CustomerAddressDto>> HandleCore(UpdateFamilyMemberAddressCommand request, CancellationToken cancellationToken)
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
        await SaveDraftChangesAsync(cancellationToken);
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
                request.IsDefault,
                request.Page,
                request.PageSize),
            cancellationToken);
    }

    public Task<Result<CustomerAddressDto>> Handle(SetFamilyMemberDefaultAddressCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<CustomerAddressDto>> HandleCore(SetFamilyMemberDefaultAddressCommand request, CancellationToken cancellationToken)
    {
        var target = await ResolveLinkedMemberForAddressAsync(request.MemberId, cancellationToken);
        if (target.IsFailure) return target.Error;

        var updated = await _addresses.SetDefaultAsync(target.Value.UserId, request.AddressId, cancellationToken);
        if (updated.IsFailure) return updated.Error;

        target.Value.Member.DeliveryAddressId = updated.Value.Id;
        target.Value.Member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(target.Value.Member);
        FamilyDraftExpiry.Touch(target.Value.Group, _clock.UtcNow);
        await SaveDraftChangesAsync(cancellationToken);
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

    private static string NormalizeIngredientCode(string code)
    {
        if (code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            return $"INGREDIENT:{code[11..].Trim().ToLowerInvariant()}";
        // Auto-prefix bare GUIDs so mobile can omit the schema prefix.
        var trimmed = code.Trim();
        if (Guid.TryParse(trimmed, out _))
            return $"INGREDIENT:{trimmed.ToLowerInvariant()}";
        return FoodCodeNormalizer.Normalize(code);
    }

    private static IReadOnlyList<FoodPreferenceItemDto> NormalizePreferenceItems(
        IEnumerable<FoodPreferenceItemDto>? items,
        bool ingredient)
        => (items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !FoodPreferenceNoneCodes.IsNone(x.Code)
                && (ingredient || !FamilyPreferenceSelectionCodes.IsNone(x.Code, FamilyPreferenceSelectionCodes.NoAllergies)))
            .Select(x => x with
            {
                Code = ingredient
                    ? NormalizeIngredientCode(x.Code)
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
        var rawItems = (dislikedFoods ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !FoodPreferenceNoneCodes.IsNone(x.Code))
            .ToList();

        if (rawItems.Count == 0)
        {
            var existingEmpty = await _db.CustomerAllergyPreferences.IgnoreQueryFilters()
                .Where(x => x.CustomerUserId == userId && x.IsDislike)
                .ToListAsync(cancellationToken);
            _db.CustomerAllergyPreferences.RemoveRange(existingEmpty);
            return Result.Success();
        }

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
                .ToListAsync(cancellationToken);
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
            if (!x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            {
                return x with { Code = $"INGREDIENT:{x.Code}" };
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
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var hydrated = mapped.Select(x =>
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
        if (FamilyPreferenceSelectionCodes.HasConflictingNone(codes ?? [], FamilyPreferenceSelectionCodes.NoDietaryRestrictions))
            return Error.Validation("family.preference_none_conflict");
        var selected = (codes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x) && !FamilyPreferenceSelectionCodes.IsNone(x, FamilyPreferenceSelectionCodes.NoDietaryRestrictions))
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

    public Task<Result<FamilyGroupScreenDto>> Handle(GetCurrentFamilyGroupQuery request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken, false);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(GetCurrentFamilyGroupQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var expiredThisCall = await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value, groupLockHeld: true);
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
            await SaveDraftChangesAsync(cancellationToken);
        }
        else
        {
            if (!await _db.FamilyCheckoutBatches.AnyAsync(x => x.FamilyGroupId == group.Id
                && (x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired), cancellationToken))
            {
                await HydrateManagerFromProfileAsync(group, managerId.Value, cancellationToken);
                await SaveDraftChangesAsync(cancellationToken);
            }
        }

        return await MapScreenAsync(group, settings, cancellationToken, sessionExpired);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(UpsertFamilyGroupCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(UpsertFamilyGroupCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var checkoutPending = await _db.FamilyGroups.AsNoTracking().AnyAsync(
            x => x.ManagerCustomerUserId == managerId.Value && x.Status == FamilyGroupStatus.CheckoutPending,
            cancellationToken);
        if (checkoutPending)
            return Error.Conflict("family.not_draft", "Family checkout is already in progress.");

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
            var packageAssign = await AssignPackageAsync(group, request.FamilyPackageId, settings, cancellationToken);
            if (packageAssign.IsFailure) return packageAssign.Error;
            group.Name = name;
            await HydrateManagerFromProfileAsync(group, managerId.Value, cancellationToken);
            FamilyDraftExpiry.Touch(group, _clock.UtcNow);
            await SaveDraftChangesAsync(cancellationToken);
            return await MapScreenAsync(group, settings, cancellationToken);
        }

        var created = await CreateDraftGroupAsync(managerId.Value, name, request.FamilyPackageId, settings, cancellationToken);
        if (created.IsFailure) return created.Error;
        await SaveDraftChangesAsync(cancellationToken);
        return await MapScreenAsync(created.Value, settings, cancellationToken);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(AddFamilyDraftMemberCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(AddFamilyDraftMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family members can only be changed while checkout is not in progress.");

        var rawPhone = request.Member.Phone?.Trim();
        var phone = PhoneNumberNormalizer.Normalize(rawPhone);
        var managerUser = await _users.FindByIdAsync(managerId.Value, cancellationToken);
        var managerPhone = PhoneNumberNormalizer.Normalize(managerUser?.PhoneNumber);
        var isManagerPhone = !string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(managerPhone) && string.Equals(managerPhone, phone, StringComparison.OrdinalIgnoreCase);

        var settings = await _settings.GetAsync(cancellationToken);
        var activeRoster = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, cancellationToken);
        if (activeRoster.Count >= settings.MaxMembers)
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
            var candidatePhones = PhoneNumberNormalizer.GetLookupCandidates(phone);
            if (group.Members.Any(m => !string.IsNullOrWhiteSpace(m.PhoneNumber) && candidatePhones.Contains(PhoneNumberNormalizer.Normalize(m.PhoneNumber)!)))
                return Error.Conflict("family.already_in_family", "This phone is already in the family.");

            account = await _users.FindByPhoneAsync(phone, cancellationToken);
            if (account is null)
            {
                foreach (var candidate in candidatePhones)
                {
                    if (!string.Equals(candidate, phone, StringComparison.OrdinalIgnoreCase))
                    {
                        account = await _users.FindByPhoneAsync(candidate, cancellationToken);
                        if (account is not null) break;
                    }
                }
            }

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

        if (write.AgeYears is < 1 or > 120)
        {
            var profile = await _db.CustomerProfiles.AsNoTracking()
                .Where(p => p.UserId == account.Id)
                .Select(p => new { p.DateOfBirth })
                .FirstOrDefaultAsync(cancellationToken);

            if (profile?.DateOfBirth is not null)
            {
                var calculatedAge = FamilyManagerProfileHydration.AgeYearsFromDateOfBirth(profile.DateOfBirth, _clock.TodayUtc);
                if (calculatedAge is >= 1 and <= 120)
                    write = write with { AgeYears = calculatedAge };
            }
        }

        if (write.Gender is not (FamilyMemberGender.Male or FamilyMemberGender.Female))
        {
            var candidatePhones = PhoneNumberNormalizer.GetLookupCandidates(phone);
            var existingGender = await (
                from m in _db.FamilyMembers.AsNoTracking()
                where (m.PhoneNumber != null && candidatePhones.Contains(m.PhoneNumber))
                   || (from a in _db.FamilyMemberAccesses.AsNoTracking()
                       where a.LinkedCustomerUserId == account.Id && a.RemovedAtUtc == null
                       select a.FamilyMemberId).Contains(m.Id)
                where m.Gender == FamilyMemberGender.Male || m.Gender == FamilyMemberGender.Female
                select (FamilyMemberGender?)m.Gender
            ).FirstOrDefaultAsync(cancellationToken);

            if (existingGender is FamilyMemberGender.Male or FamilyMemberGender.Female)
                write = write with { Gender = existingGender.Value };
        }

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
        if (account is not null)
        {
            await FamilyMemberAddressHydration.PreferLinkedCustomerAddressAsync(
                _db, member, account.Id, cancellationToken);
        }
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);
        group.Members.Add(member);
        _db.FamilyMembers.Add(member);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await SaveDraftChangesAsync(cancellationToken);

        _db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            HasSeparateAccount = false,
            LinkedCustomerUserId = account.Id,
        });
        if (!await _db.CustomerProfiles.AnyAsync(x => x.UserId == account.Id, cancellationToken))
            _db.CustomerProfiles.Add(new CustomerProfile { UserId = account.Id });
        await SaveDraftChangesAsync(cancellationToken);

        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(UpdateFamilyMemberCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(UpdateFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family members can only be changed while checkout is not in progress.");
        if (group.Status != FamilyGroupStatus.Draft && group.Status != FamilyGroupStatus.Active)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft or active.");

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
        await SaveDraftChangesAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(DeleteFamilyMemberCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(DeleteFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Family members can only be changed while checkout is not in progress.");
        if (group.Status != FamilyGroupStatus.Draft && group.Status != FamilyGroupStatus.Active)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft or active.");

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;
        if (member.Role == FamilyMemberRole.Manager)
            return Error.Conflict("family.cannot_delete_manager", "The manager member cannot be deleted.");
        if (member.MemberSubscriptionId is not null && group.Status == FamilyGroupStatus.Active)
            return Error.Conflict("family.cannot_delete_active_member", "Members with an active subscription cannot be removed from this flow.");
        if (group.Members.Count <= 1)
            return Error.Conflict("family.min_members", "At least one family member is required.");

        group.Members.Remove(member);
        _db.FamilyMembers.Remove(member);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await SaveDraftChangesAsync(cancellationToken);

        var settings = await _settings.GetAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(SetFamilyMemberPlanCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(SetFamilyMemberPlanCommand request, CancellationToken cancellationToken)
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
        if (group.ConvertedFromSubscriptionId is not null
            && member.Role == FamilyMemberRole.Manager
            && await HasLiveManagerIndividualServiceAsync(group, member, cancellationToken))
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
        await SaveDraftChangesAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    private async Task<bool> HasLiveManagerIndividualServiceAsync(
        FamilyGroup group,
        FamilyMember manager,
        CancellationToken cancellationToken)
    {
        var today = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
        var qualifying = new[]
        {
            SubscriptionStatus.Paid,
            SubscriptionStatus.Scheduled,
            SubscriptionStatus.Active,
            SubscriptionStatus.Frozen,
            SubscriptionStatus.SuspendedForReview,
        };
        if (await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.CustomerUserId == group.ManagerCustomerUserId
                && x.FamilyGroupId == null && x.FamilyMemberId == null
                && qualifying.Contains(x.Status) && x.EndDate >= today, cancellationToken))
            return true;

        return group.ConvertedFromSubscriptionId is Guid convertedId
            && manager.MemberSubscriptionId == convertedId
            && await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == convertedId
                && x.CustomerUserId == group.ManagerCustomerUserId
                && x.FamilyGroupId == group.Id && x.FamilyMemberId == manager.Id
                && qualifying.Contains(x.Status) && x.EndDate >= today, cancellationToken);
    }

    public Task<Result<FamilyGroupScreenDto>> Handle(SetFamilyMemberStartDateCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<FamilyGroupScreenDto>> HandleCore(SetFamilyMemberStartDateCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var groupResult = await LoadLiveRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.not_draft", "Start dates can only be changed while checkout is not in progress.");

        var memberResult = await RequireMemberAsync(group, request.MemberId, managerId.Value, cancellationToken);
        if (memberResult.IsFailure) return memberResult.Error;
        var member = memberResult.Value;
        if (member.MemberSubscriptionId is not null && group.Status == FamilyGroupStatus.Active)
            return Error.Conflict("family.plan_locked", "Paid family members cannot change the start date from this flow.");

        member.StartDate = request.StartDate;
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);

        var settings = await _settings.GetAsync(cancellationToken);
        FamilyDraftExpiry.Touch(group, _clock.UtcNow);
        await SaveDraftChangesAsync(cancellationToken);
        return await MapScreenAsync(group, settings, cancellationToken);
    }

    public Task<Result<FamilyCheckoutSummaryDto>> Handle(GetFamilyCheckoutSummaryQuery request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken, false);

    private async Task<Result<FamilyCheckoutSummaryDto>> HandleCore(GetFamilyCheckoutSummaryQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value, groupLockHeld: true);
        var group = await LoadRosterGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return SessionExpiredError();

        await EnsureDefaultStartDatesAsync(group, cancellationToken);

        var activeIds = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, cancellationToken);
        var selection = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(_db, group.Id, cancellationToken);
        var defaultSelection = selection ?? group.Members
            .Where(x => activeIds.Contains(x.Id) && x.MemberSubscriptionId is null && FamilyMemberPlanRules.HasPricablePlan(x))
            .Select(x => x.Id).ToHashSet();
        var batch = await _db.FamilyCheckoutBatches.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.FamilyGroupId == group.Id && (x.Status == FamilyCheckoutBatchStatus.Draft
                || x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired))
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var locked = batch is not null && batch.Status != FamilyCheckoutBatchStatus.Draft;
        var quoteResult = locked
            ? Result<FamilyQuoteResult>.Success(new FamilyQuoteResult(batch!.Lines.Where(x => x.CheckoutSelected)
                .Select(x => new FamilyQuoteLine(x.FamilyMemberId, group.Members.FirstOrDefault(m => m.Id == x.FamilyMemberId)?.DisplayName ?? "",
                    x.PriceBeforeFamilyDiscount, 0m, 0m, 0m)).ToList(), batch.Subtotal, batch.FamilyDiscountPercent,
                batch.FamilyDiscountAmount, batch.FinalPrice, batch.CurrencyCode, batch.FamilyDiscountAmount > 0m,
                batch.PackageId, batch.PackageMinMembers, batch.PackageMaxMembers,
                batch.Lines.Count(x => x.CheckoutSelected), "locked"))
            : await _quote.QuoteAsync(group, cancellationToken, new FamilyQuoteScope(activeIds, defaultSelection));
        if (quoteResult.IsFailure) return quoteResult.Error;
        var quote = quoteResult.Value;
        var members = group.Members.Where(x => activeIds.Contains(x.Id)).OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.CreatedAtUtc).ToList();

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

        var quotedMemberIds = quote.Lines.Select(x => x.MemberId).ToHashSet();
        var payable = members.Where(x => defaultSelection.Contains(x.Id) && quotedMemberIds.Contains(x.Id)).ToList();
        var allergiesCompleted = await FamilyCheckoutAllergyLookup.CompletedMemberIdsAsync(
            _db, group, payable, cancellationToken);
        var (canCheckout, blockReason) = FamilyCheckoutGate.Evaluate(
            group, members, quote, allergiesCompleted);

        var managerPrepaid = quote.ManagerSeatExempt;
        var managerSubscriptionId = quote.ManagerIndividualSubscriptionId
            ?? members.FirstOrDefault(m => m.Role == FamilyMemberRole.Manager)?.MemberSubscriptionId;
        var newMembersCount = payable.Count;
        // Quote.Subtotal is already payable-only when manager is prepaid.
        var newMembersSubtotal = quote.Subtotal;
        var hydration = await LoadPreferenceHydrationAsync(group, cancellationToken);

        var summaryAddressIds = members.Where(x => x.DeliveryAddressId.HasValue).Select(x => x.DeliveryAddressId!.Value).Distinct().ToList();
        var summaryAddresses = summaryAddressIds.Count == 0
            ? []
            : await _db.CustomerAddresses.AsNoTracking()
                .Where(x => summaryAddressIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var summaryAddressById = summaryAddresses.ToDictionary(x => x.Id);

        var memberSchedules = new List<FamilyMemberScheduleDto>(members.Count);
        foreach (var m in members)
        {
            bundles.TryGetValue(m.BundleId ?? Guid.Empty, out var memberBundle);
            var mealsPerDay = memberBundle?.MealsPerDay ?? 0;
            DateOnly? firstDelivery = null;
            DateOnly? endDate = null;
            var deliveryDays = 0;
            var restaurants = new List<FamilyMemberRestaurantDto>();
            var availability = new IndividualRestaurantAvailabilityDto(
                IndividualRestaurantAvailabilityStatus.SelectionRequired, 0, 0, _clock.UtcNow, null,
                m.StartDate is null ? "checkout.start_date_required" : "subscription_flow.invalid_context");
            if (m.StartDate is DateOnly startDate && m.DurationDays is > 0)
            {
                var deliveryStart = _schedule.ResolveFirstDeliveryDate(startDate, m.IncludeFriday);
                if (deliveryStart == default) deliveryStart = startDate;
                var serviceDates = _schedule.GetServiceDates(deliveryStart, m.DurationDays.Value, m.IncludeFriday);
                deliveryDays = serviceDates.Count;
                if (deliveryDays > 0)
                {
                    firstDelivery = serviceDates[0];
                    endDate = serviceDates[^1];
                }
                var regionId = m.DeliveryAddressId is Guid addrId
                    && summaryAddressById.TryGetValue(addrId, out var memberAddress)
                        ? memberAddress.RegionId
                        : null;
                if (regionId is null || regionId == Guid.Empty)
                    availability = new IndividualRestaurantAvailabilityDto(
                        IndividualRestaurantAvailabilityStatus.AddressRequired, 0, 0, _clock.UtcNow, deliveryStart, "checkout.address_required");
                if (_restaurantEligibility is not null
                    && m.ProgramId.HasValue && m.BundleId.HasValue && m.SelectedTier.HasValue && regionId.HasValue)
                {
                    var rows = await _restaurantEligibility.EvaluateAsync(new RestaurantEligibilityRequest(
                        m.ProgramId.Value, m.BundleId.Value, m.SelectedTier.Value, regionId.Value,
                        deliveryStart, m.DurationDays.Value, m.IncludeFriday,
                        CustomerUserId: hydration.LinkedUserId(m), ServiceDates: serviceDates), cancellationToken);
                    var eligible = rows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
                    var failures = rows.Where(x => !x.IsEligible).SelectMany(x => x.ReasonCodes)
                        .GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
                    var coveredCount = rows.Count(x => !x.ReasonCodes.Contains(EligibilityReasonCodes.RegionNotCovered)
                        && !x.ReasonCodes.Contains(EligibilityReasonCodes.OfferingMissing)
                        && !x.ReasonCodes.Contains(EligibilityReasonCodes.RestaurantNotApproved)
                        && !x.ReasonCodes.Contains(EligibilityReasonCodes.RestaurantInactive));
                    availability = new IndividualRestaurantAvailabilityDto(
                        eligible.Count > 0 ? IndividualRestaurantAvailabilityStatus.Available
                            : coveredCount > 0 ? IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection
                            : IndividualRestaurantAvailabilityStatus.NoRestaurantsForAddress,
                        coveredCount, eligible.Count, _clock.UtcNow, deliveryStart,
                        eligible.Count > 0 ? null : coveredCount > 0 ? SubscriptionCheckoutGateCodes.NoRestaurantsForSelection
                            : SubscriptionCheckoutGateCodes.NoRestaurantsForAddress,
                        failures.Count > 0 ? failures : null);
                    var limit = eligible.Count == 0 ? 0
                        : _diversification.CalculateLimit(Guid.Empty, serviceDates.Count, eligible.Count).LimitValue;
                    var classification = await _tiers.ResolveAsync(m.ProgramId.Value, m.BundleId.Value, eligibleOnly: true, cancellationToken);
                    var tierByRestaurant = classification.Assignments.ToDictionary(x => x.RestaurantId, x => x.Tier);
                    var logos = await SubscriptionRestaurantSummaryImages.LoadAsync(_db, eligible.Select(x => x.RestaurantId).ToList(), cancellationToken);
                    restaurants.AddRange(eligible.Select(x => new FamilyMemberRestaurantDto(x.RestaurantId, x.NameAr, x.NameEn,
                        limit, logos.GetValueOrDefault(x.RestaurantId), tierByRestaurant.GetValueOrDefault(x.RestaurantId, m.SelectedTier.Value))));
                }
            }

            memberSchedules.Add(new FamilyMemberScheduleDto(
                m.Id,
                PaymentDate: m.StartDate is null ? null : _clock.TodayUtc,
                firstDelivery,
                endDate,
                deliveryDays,
                mealsPerDay,
                deliveryDays * mealsPerDay,
                restaurants,
                new RestaurantRepetitionSummaryDto(restaurants.Count == 0 ? 0 : restaurants[0].OccurrenceCount,
                    restaurants.Count, m.DurationDays ?? 0), availability));
        }

        if (canCheckout && _restaurantEligibility is not null)
        {
            var unavailable = memberSchedules.FirstOrDefault(s => payable.Any(m => m.Id == s.MemberId)
                && s.Availability?.Status != IndividualRestaurantAvailabilityStatus.Available);
            if (unavailable is not null)
            {
                canCheckout = false;
                blockReason = unavailable.Availability?.MessageCode ?? SubscriptionCheckoutGateCodes.NoRestaurantsForSelection;
            }
        }

        return new FamilyCheckoutSummaryDto(
            group.Id,
            group.Name,
            selected.Id,
            MapMemberSummary(selected, programs, bundles, priceByMember),
            members.Select(m =>
            {
                CustomerAddressDto? deliveryAddress = null;
                if (m.DeliveryAddressId is Guid addressId && summaryAddressById.TryGetValue(addressId, out var address))
                    deliveryAddress = MapResumeAddress(address, hydration.ProfileFor(m)?.ActiveDeliveryAddressId);
                return HydrateMemberCard(m, hydration, deliveryAddress) with
                {
                    SubscribeNow = selection is null ? m.MemberSubscriptionId is null : selection.Contains(m.Id)
                };
            }).ToList(),
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
            NewMembersSubtotal: newMembersSubtotal,
            SelectedFamilyPackageId: group.SelectedFamilyPackageId,
            EffectiveFamilyPackageId: quote.EffectiveFamilyPackageId,
            EffectiveFamilyPackageMinMembers: quote.EffectiveFamilyPackageMinMembers,
            EffectiveFamilyPackageMaxMembers: quote.EffectiveFamilyPackageMaxMembers,
            ParticipantCount: quote.ParticipantCount,
            PackageSelectionMode: quote.PackageSelectionMode,
            RosterCount: members.Count, SelectedNewSubscriberCount: payable.Count,
            EligibleExistingSubscriberCount: Math.Max(0, quote.ParticipantCount - payable.Count),
            UnsubscribedCount: members.Count(x => x.MemberSubscriptionId == null),
            BatchId: batch?.Id, BatchVersion: batch?.Version, LockedQuote: locked,
            MemberSchedules: memberSchedules,
            OccupiedSeatCount: quote.OccupiedSeatCount,
            AvailableSeatCount: quote.AvailableSeatCount,
            MaxSeats: quote.EffectiveFamilyPackageMaxMembers ?? 0,
            DeferredMemberCount: quote.DeferredMemberCount,
            CurrentPurchaseCount: quote.CurrentPurchaseCount,
            ManagerSeatExempt: quote.ManagerSeatExempt,
            ManagerIndividualIsLive: quote.ManagerIndividualIsLive,
            IsInitialPurchaseOrReactivation: quote.IsInitialPurchaseOrReactivation,
            Restaurants: memberSchedules.First(x => x.MemberId == selected.Id).Restaurants,
            RestaurantRepetition: memberSchedules.First(x => x.MemberId == selected.Id).RestaurantRepetition,
            Availability: memberSchedules.First(x => x.MemberId == selected.Id).Availability);
    }

    public Task<Result<bool>> Handle(AbandonFamilyDraftCommand request, CancellationToken cancellationToken)
        => WithGroupLockAsync(ct => HandleCore(request, ct), cancellationToken);

    private async Task<Result<bool>> HandleCore(AbandonFamilyDraftCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId.Value, groupLockHeld: true);
        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value && EditableStatuses.Contains(x.Status),
                cancellationToken);
        if (group is null)
            return SessionExpiredError();
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Only a draft family group can be abandoned.");

        await FamilyDraftExpiry.RemoveAsync(_db, group, _clock.UtcNow, cancellationToken);
        await SaveDraftChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Result<T>> WithGroupLockAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, CancellationToken ct, bool mutation = true, Guid? preferenceMemberId = null)
    {
        // Expiry acquires its own financial/group locks; never invoke it inside the
        // draft transaction while holding the family row lock.
        if (_currentUser.UserType == UserType.Customer && _currentUser.UserId is Guid managerId)
            await FamilyCheckoutResumeReader.RecoverExpiredAsync(_db, _clock, _holdExpiry, managerId, ct);
        if (_transactions is null || _locks is null)
        {
            if (_db is DbContext context && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
                throw new InvalidOperationException("Family draft writes require transactional family locks.");
            return await operation(ct);
        }
        return await _transactions.ExecuteAsync(async transactionCt =>
        {
            var groupId = await _db.FamilyGroups.AsNoTracking().Where(x => x.ManagerCustomerUserId == _currentUser.UserId
                && (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.Active || x.Status == FamilyGroupStatus.CheckoutPending))
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(transactionCt);
            if (groupId is Guid id)
            {
                await _locks.LockFamilyGroupAsync(id, transactionCt);
                var affectedMembers = new List<Guid>();
                if (preferenceMemberId is Guid preferenceMember)
                {
                    // Preferences belong to a linked profile. Protect every member
                    // sharing that profile, not just the requested card.
                    var profileId = await _db.FamilyMemberAccesses.AsNoTracking()
                        .Where(x => x.FamilyMemberId == preferenceMember && x.RemovedAtUtc == null)
                        .Select(x => x.LinkedCustomerUserId).FirstOrDefaultAsync(transactionCt);
                    profileId ??= await _db.FamilyMembers.AsNoTracking()
                        .Where(x => x.Id == preferenceMember && x.Role == FamilyMemberRole.Manager)
                        .Select(x => (Guid?)x.FamilyGroup.ManagerCustomerUserId).FirstOrDefaultAsync(transactionCt);
                    affectedMembers = await _db.FamilyMembers.AsNoTracking()
                        .Where(x => x.Id == preferenceMember || (profileId != null
                            && ((x.Role == FamilyMemberRole.Manager && x.FamilyGroup.ManagerCustomerUserId == profileId)
                                || _db.FamilyMemberAccesses.Any(a => a.FamilyMemberId == x.Id
                                    && a.RemovedAtUtc == null && a.LinkedCustomerUserId == profileId))))
                        .Select(x => x.Id).ToListAsync(transactionCt);
                }
                if (mutation && await _db.FamilyCheckoutBatches.AnyAsync(x => x.FamilyGroupId == id
                    && (x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired)
                    && (preferenceMemberId == null || x.Lines.Any(l => l.CheckoutSelected && affectedMembers.Contains(l.FamilyMemberId))), transactionCt))
                    return Result.Failure<T>(Error.ConflictWithMetadata("family.checkout_pending", new Dictionary<string, object?>
                    {
                        ["familyGroupId"] = id,
                        ["resumeEndpoint"] = "/api/v1/customer/family-subscription-flow/current",
                        ["retryPaymentEndpoint"] = FamilyCheckoutResumeReader.RetryEndpoint,
                        ["nextScreen"] = "family_payment",
                    }));
            }
            return await operation(transactionCt);
        }, ct);
    }

    private async Task SaveDraftChangesAsync(CancellationToken ct)
    {
        if (_db is DbContext context && !context.ChangeTracker.HasChanges()) return;
        var groupIds = await _db.FamilyGroups.Where(x => x.ManagerCustomerUserId == _currentUser.UserId)
            .Select(x => x.Id).ToListAsync(ct);
        foreach (var id in groupIds) await FamilyCheckoutBatchVersioning.TouchDraftVersionsAsync(_db, id, ct);
        await _db.SaveChangesAsync(ct);
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
        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId, groupLockHeld: true);
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
                .ThenInclude(x => x.MemberSubscription)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId && RosterStatuses.Contains(x.Status),
                cancellationToken);

    private async Task<FamilyGroup?> LoadRosterGroupTrackedAsync(Guid managerId, CancellationToken cancellationToken)
        => await _db.FamilyGroups
            .Include(x => x.Members)
                .ThenInclude(x => x.MemberSubscription)
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
            member.PhoneNumber = PhoneNumberNormalizer.Normalize(write.Phone);
        if (write.Occupation is not null)
            member.Occupation = string.IsNullOrWhiteSpace(write.Occupation) ? null : write.Occupation.Trim();
    }

    private sealed record FamilyPreferenceHydration(
        Guid ManagerUserId,
        IReadOnlyDictionary<Guid, Guid> LinkedUserByMember,
        IReadOnlyDictionary<Guid, CustomerProfile> ProfileByUser,
        IReadOnlyList<CustomerAllergyPreference> AllergyRows,
        IReadOnlyList<CustomerDietaryPreference> DietaryRows,
        IReadOnlyDictionary<string, AllergenDefinition> AllergenDefs,
        IReadOnlyDictionary<string, FoodPreferenceDefinition> DietaryDefs)
    {
        public Guid? LinkedUserId(FamilyMember member)
            => LinkedUserByMember.TryGetValue(member.Id, out var linked)
                ? linked
                : member.Role == FamilyMemberRole.Manager ? ManagerUserId : null;

        public CustomerProfile? ProfileFor(FamilyMember member)
            => LinkedUserId(member) is Guid userId && ProfileByUser.TryGetValue(userId, out var profile)
                ? profile
                : null;
    }

    private async Task<FamilyPreferenceHydration> LoadPreferenceHydrationAsync(
        FamilyGroup group,
        CancellationToken cancellationToken)
    {
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

        var userIds = group.Members.Select(LinkedUserId).OfType<Guid>().Distinct().ToList();
        var profiles = userIds.Count == 0
            ? []
            : await _db.CustomerProfiles.AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .ToListAsync(cancellationToken);
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
            group.ManagerCustomerUserId,
            linkedUserByMember,
            profiles.ToDictionary(x => x.UserId),
            allergyRows,
            dietaryRows,
            allergenDefs,
            dietaryDefs);
    }

    private static FamilyMemberCardDto HydrateMemberCard(
        FamilyMember member,
        FamilyPreferenceHydration hydration,
        CustomerAddressDto? deliveryAddress = null)
    {
        var userId = hydration.LinkedUserId(member);
        var profile = hydration.ProfileFor(member);

        var allergies = hydration.AllergyRows
            .Where(x => userId.HasValue && x.CustomerUserId == userId.Value && !x.IsDislike)
            .Select(x => MapAllergyItem(x, hydration.AllergenDefs))
            .ToList();
        var disliked = hydration.AllergyRows
            .Where(x => userId.HasValue && x.CustomerUserId == userId.Value && x.IsDislike)
            .Select(x => new FoodPreferenceItemDto(x.AllergenCode, x.AllergenNameAr, x.AllergenNameEn, x.Severity))
            .ToList();
        var dietary = hydration.DietaryRows
            .Where(x => userId.HasValue && x.CustomerUserId == userId.Value)
            .Select(x => hydration.DietaryDefs.TryGetValue(x.PreferenceCode, out var definition)
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
    }

    private async Task<FamilyGroupScreenDto> MapScreenAsync(
        FamilyGroup group,
        FamilyPricingSettings settings,
        CancellationToken cancellationToken,
        bool sessionExpired = false)
    {
        await EnsureDefaultStartDatesAsync(group, cancellationToken);
        var activeIds = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, cancellationToken);
        var selection = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(_db, group.Id, cancellationToken);
        var package = FamilyPackageResolver.FindActive(settings, group.SelectedFamilyPackageId);

        var hydration = await LoadPreferenceHydrationAsync(group, cancellationToken);

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

        return new(
            group.Id,
            group.Name,
            group.Status,
            group.SelectedFamilyPackageId,
            package?.NameAr,
            package?.NameEn,
            package?.MinMembers ?? settings.MinMembersForDiscount,
            package?.MaxMembers ?? settings.MaxMembers,
            package?.DiscountPercent ?? settings.DiscountPercent,
            group.Members.Where(x => activeIds.Contains(x.Id))
                .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(x => x.CreatedAtUtc)
                .Select(member =>
                {
                    var profile = hydration.ProfileFor(member);
                    CustomerAddressDto? deliveryAddress = null;
                    if (member.DeliveryAddressId is Guid addressId && addressById.TryGetValue(addressId, out var address))
                        deliveryAddress = MapResumeAddress(address, profile?.ActiveDeliveryAddressId);

                    return HydrateMemberCard(member, hydration, deliveryAddress)
                        with { SubscribeNow = selection is null ? member.MemberSubscriptionId is null : selection.Contains(member.Id) };
                })
                .ToList(),
            ResolveCurrentStep(group),
            sessionExpired,
            group.Status == FamilyGroupStatus.Draft ? FamilyDraftExpiry.ExpiresAtUtc(group) : null,
            sessionExpired ? _messages.GetMessage(MessageCodes.Family.SessionExpired) : null,
            PackageAvailable: package is not null,
            PendingCheckout: await FamilyCheckoutResumeReader.GetAsync(_db, group.Id, _clock.UtcNow, cancellationToken),
            RetryPaymentEndpoint: await FamilyCheckoutResumeReader.CanRetryAsync(_db, group.Id, cancellationToken)
                ? FamilyCheckoutResumeReader.RetryEndpoint : null);
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

    private async Task<Result> AssignPackageAsync(FamilyGroup group, Guid? familyPackageId, FamilyPricingSettings settings, CancellationToken ct)
    {
        var active = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, ct);
        var selected = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(_db, group.Id, ct);
        var paidIds = group.Members.Where(x => active.Contains(x.Id) && x.MemberSubscriptionId != null)
            .Select(x => x.MemberSubscriptionId!.Value).Distinct().ToList();
        var paidCount = await _db.Subscriptions.CountAsync(x => paidIds.Contains(x.Id)
            && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Scheduled || x.Status == SubscriptionStatus.Active
                || x.Status == SubscriptionStatus.Frozen || x.Status == SubscriptionStatus.SuspendedForReview), ct);
        var participantCount = paidCount + group.Members.Count(x => active.Contains(x.Id)
            && selected?.Contains(x.Id) == true && x.MemberSubscriptionId == null);

        if (familyPackageId is null || familyPackageId == Guid.Empty)
        {
            if (group.SelectedFamilyPackageId is Guid existing)
            {
                var keep = FamilyPackageResolver.FindActive(settings, existing);
                if (keep is not null && participantCount > keep.MaxMembers)
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
        if (participantCount > package.MaxMembers)
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

    private async Task EnsureDefaultStartDatesAsync(FamilyGroup group, CancellationToken ct)
    {
        // A chosen start day is distinct from first delivery. Use the shared Kuwait
        // schedule so the preparation interval and Friday rules are applied once.
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return;
        var missing = group.Members.Where(m => m.MemberSubscriptionId is null
            && (m.StartDate is null || m.StartDate == default(DateOnly))).ToList();
        if (missing.Count == 0 || await _db.FamilyCheckoutBatches.AnyAsync(b => b.FamilyGroupId == group.Id
            && (b.Status == FamilyCheckoutBatchStatus.PaymentPending || b.Status == FamilyCheckoutBatchStatus.ReviewRequired), ct))
            return;

        var chosenStart = _schedule.GetKuwaitToday();
        foreach (var member in missing)
            member.StartDate = chosenStart;
        await SaveDraftChangesAsync(ct);
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
            _db.FamilyMembers.Add(manager);
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
