using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class FamilyDraftHandlers :
    IQueryHandler<GetFamilyDraftMetadataQuery, FamilyDraftMetadataDto>,
    IQueryHandler<GetFamilyRelationshipsQuery, IReadOnlyList<FamilyLookupDto>>,
    IQueryHandler<GetFamilySizePackagesQuery, IReadOnlyList<FamilySizePackageDto>>,
    IQueryHandler<GetCurrentFamilyGroupQuery, FamilyGroupScreenDto>,
    IQueryHandler<GetFamilyCheckoutSummaryQuery, FamilyCheckoutSummaryDto>,
    ICommandHandler<UpsertFamilyGroupCommand, FamilyGroupScreenDto>,
    ICommandHandler<AddFamilyDraftMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<UpdateFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<DeleteFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<SetFamilyMemberPlanCommand, FamilyGroupScreenDto>,
    ICommandHandler<AbandonFamilyDraftCommand, bool>
{
    private const string CheckoutEndpoint = "POST /api/v1/customer/family-subscription-flow/checkout";

    private static readonly FamilyGroupStatus[] EditableStatuses =
    [
        FamilyGroupStatus.Draft,
        FamilyGroupStatus.CheckoutPending,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyPricingSettingsReader _settings;
    private readonly IFamilyPlanEligibilityService _planEligibility;
    private readonly IFamilyQuoteService _quote;
    private readonly IDateTimeProvider _clock;

    public FamilyDraftHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyPricingSettingsReader settings,
        IFamilyPlanEligibilityService planEligibility,
        IFamilyQuoteService quote,
        IDateTimeProvider clock)
        => (_db, _currentUser, _users, _settings, _planEligibility, _quote, _clock)
            = (db, currentUser, users, settings, planEligibility, quote, clock);

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

    public async Task<Result<FamilyGroupScreenDto>> Handle(GetCurrentFamilyGroupQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var settings = await _settings.GetAsync(cancellationToken);
        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
        {
            var hasActive = await _db.FamilyGroups.AsNoTracking()
                .AnyAsync(x => x.ManagerCustomerUserId == managerId.Value && x.Status == FamilyGroupStatus.Active, cancellationToken);
            if (hasActive)
                return Error.NotFound("family.not_found", "No draft family group was found.");

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

        return MapScreen(group, settings);
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
            await _db.SaveChangesAsync(cancellationToken);
            return MapScreen(group, settings);
        }

        var created = await CreateDraftGroupAsync(managerId.Value, name, request.FamilyPackageId, settings, cancellationToken);
        if (created.IsFailure) return created.Error;
        await _db.SaveChangesAsync(cancellationToken);
        return MapScreen(created.Value, settings);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(AddFamilyDraftMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var settings = await _settings.GetAsync(cancellationToken);
        var packageResult = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        if (packageResult.IsFailure) return packageResult.Error;
        if (group.Members.Count >= packageResult.Value.MaxMembers)
            return Error.Conflict("family.max_members", "Family member limit reached for the selected package.");

        var validated = await ValidateMemberWriteAsync(managerId.Value, request.Member, cancellationToken);
        if (validated.IsFailure) return validated.Error;

        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
        };
        ApplyWrite(member, request.Member);
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);
        group.Members.Add(member);
        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        return MapScreen(group, settings);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(UpdateFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var member = group.Members.FirstOrDefault(x => x.Id == request.MemberId);
        if (member is null)
            return Error.NotFound("family.not_found", "Family member was not found.");

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
        await _db.SaveChangesAsync(cancellationToken);
        return MapScreen(group, settings);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(DeleteFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var member = group.Members.FirstOrDefault(x => x.Id == request.MemberId);
        if (member is null)
            return Error.NotFound("family.not_found", "Family member was not found.");
        if (member.Role == FamilyMemberRole.Manager)
            return Error.Conflict("family.cannot_delete_manager", "The manager member cannot be deleted.");
        if (group.Members.Count <= 1)
            return Error.Conflict("family.min_members", "At least one family member is required.");

        group.Members.Remove(member);
        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync(cancellationToken);

        var settings = await _settings.GetAsync(cancellationToken);
        return MapScreen(group, settings);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(SetFamilyMemberPlanCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family member plans can only be changed while the group is a draft.");

        var member = group.Members.FirstOrDefault(x => x.Id == request.MemberId);
        if (member is null)
            return Error.NotFound("family.not_found", "Family member was not found.");
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
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);

        var settings = await _settings.GetAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapScreen(group, settings);
    }

    public async Task<Result<FamilyCheckoutSummaryDto>> Handle(GetFamilyCheckoutSummaryQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");

        var settings = await _settings.GetAsync(cancellationToken);
        var quote = await _quote.QuoteAsync(group, cancellationToken);

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
        var (canCheckout, blockReason) = FamilyCheckoutGate.Evaluate(group, members, packageResult.Value, quote);

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
            members.Select(MapMember).ToList(),
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

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value && EditableStatuses.Contains(x.Status),
                cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
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

    private async Task<FamilyGroup?> LoadEditableGroupAsync(Guid managerId, CancellationToken cancellationToken)
        => await _db.FamilyGroups.AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId && EditableStatuses.Contains(x.Status),
                cancellationToken);

    private async Task<FamilyGroup?> LoadEditableGroupTrackedAsync(Guid managerId, CancellationToken cancellationToken)
        => await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId && EditableStatuses.Contains(x.Status),
                cancellationToken);

    private async Task<Result> ValidateMemberWriteAsync(Guid managerId, FamilyMemberWriteDto write, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(write.DisplayName))
            return Error.Validation("family.display_name_required", "Display name is required.");
        if (write.AgeYears is < 1 or > 120)
            return Error.Validation("family.age_invalid", "Age must be between 1 and 120.");
        if (FamilyCatalog.Relationships().All(x => x.Code != write.RelationshipCode))
            return Error.Validation("family.relationship_invalid", "Relationship code is invalid.");

        if (write.DeliveryAddressId is Guid addressId)
        {
            var addressOwned = await _db.CustomerAddresses.AsNoTracking()
                .AnyAsync(x => x.Id == addressId && x.UserId == managerId, cancellationToken);
            if (!addressOwned)
                return Error.Validation("family.address_invalid", "Delivery address must belong to the manager.");
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
        if (write.DeliveryAddressId is Guid addressId)
            member.DeliveryAddressId = addressId;
    }

    private static FamilyGroupScreenDto MapScreen(FamilyGroup group, FamilyPricingSettings settings)
    {
        var resolved = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        var package = resolved.IsSuccess ? resolved.Value : FamilyPackageResolver.DefaultActive(settings);
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
                .Select(MapMember)
                .ToList(),
            ResolveCurrentStep(group));
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

    private static FamilyMemberCardDto MapMember(FamilyMember member)
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
            member.Role == FamilyMemberRole.Manager);

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
