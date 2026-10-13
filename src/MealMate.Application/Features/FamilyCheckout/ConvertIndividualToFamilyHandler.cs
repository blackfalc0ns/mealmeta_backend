using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class ConvertIndividualToFamilyHandler :
    ICommandHandler<ConvertIndividualToFamilyCommand, FamilyGroupScreenDto>,
    IQueryHandler<GetSubscriptionConversionOptionsQuery, SubscriptionConversionOptionsDto>
{
    private static readonly SubscriptionStatus[] ConvertibleStatuses =
    [
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];

    private static readonly FamilyGroupStatus[] EditableStatuses =
    [
        FamilyGroupStatus.Draft,
        FamilyGroupStatus.CheckoutPending,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyPricingSettingsReader _settings;
    private readonly IDateTimeProvider _clock;

    public ConvertIndividualToFamilyHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyPricingSettingsReader settings,
        IDateTimeProvider clock)
        => (_db, _currentUser, _users, _settings, _clock) = (db, currentUser, users, settings, clock);

    public async Task<Result<SubscriptionConversionOptionsDto>> Handle(
        GetSubscriptionConversionOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var subscription = await FindConvertibleOrPendingAsync(managerId.Value, cancellationToken);
        if (subscription is null)
            return Error.NotFound("conversion.no_individual_subscription", "No current individual subscription was found.");

        var convertible = ConvertibleStatuses.Contains(subscription.Status);
        return new SubscriptionConversionOptionsDto(
            subscription.Id,
            subscription.Status.ToString(),
            [
                new SubscriptionConversionOptionDto(
                    "CONVERT_TO_FAMILY_MANAGER",
                    convertible,
                    "أعمل عائلة وأبقى مدير",
                    "Create a family and become manager",
                    convertible
                        ? "POST /api/v1/customer/family-subscription-flow/convert-from-individual"
                        : null,
                    convertible ? null : "conversion.individual_not_convertible"),
                new SubscriptionConversionOptionDto(
                    "CHANGE_INDIVIDUAL_PLAN",
                    false,
                    "أغيّر خطتي الفردية",
                    "Change my individual plan",
                    null,
                    "change_individual_plan.not_available"),
            ]);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(
        ConvertIndividualToFamilyCommand request,
        CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var settings = await _settings.GetAsync(cancellationToken);

        var existingConverted = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value
                     && EditableStatuses.Contains(x.Status)
                     && x.ConvertedFromSubscriptionId != null,
                cancellationToken);
        if (existingConverted is not null)
            return MapScreen(existingConverted, settings);

        if (await _db.FamilyGroups.AsNoTracking().AnyAsync(
                x => x.ManagerCustomerUserId == managerId.Value && x.Status == FamilyGroupStatus.Active,
                cancellationToken))
            return Error.Conflict("conversion.already_in_family", "An active family subscription already exists.");

        var pending = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == managerId.Value
                        && x.FamilyGroupId == null
                        && x.Status == SubscriptionStatus.PendingPayment)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending is not null
            && !await _db.Subscriptions.AsNoTracking().AnyAsync(
                x => x.CustomerUserId == managerId.Value
                     && x.FamilyGroupId == null
                     && ConvertibleStatuses.Contains(x.Status),
                cancellationToken))
            return Error.Conflict("conversion.individual_not_convertible", "Finish or expire pending payment before converting.");

        var subscription = await _db.Subscriptions
            .Where(x => x.CustomerUserId == managerId.Value
                        && x.FamilyGroupId == null
                        && ConvertibleStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is null)
            return Error.NotFound("conversion.no_individual_subscription", "No convertible individual subscription was found.");

        var leftover = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId.Value && EditableStatuses.Contains(x.Status),
                cancellationToken);
        if (leftover is not null && leftover.ConvertedFromSubscriptionId is null)
        {
            if (leftover.Status != FamilyGroupStatus.Draft)
                return Error.Conflict("conversion.already_in_family", "A family checkout is already in progress.");

            var adopted = await AdoptDraftAsync(leftover, subscription, request, settings, managerId.Value, cancellationToken);
            if (adopted.IsFailure) return adopted.Error;
            return MapScreen(leftover, settings);
        }

        var package = ResolvePackage(settings, request.FamilyPackageId, selectedPackageId: null);
        if (package.IsFailure) return package.Error;

        var snapshot = await LoadManagerSnapshotAsync(managerId.Value, cancellationToken);
        var familyName = string.IsNullOrWhiteSpace(request.Name)
            ? (snapshot.DisplayName == "عضو" ? "عائلتي" : $"عائلة {snapshot.DisplayName}")
            : request.Name.Trim();

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId.Value,
            Name = familyName,
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = package.Value.Id,
            DiscountPercentSnapshot = package.Value.DiscountPercent,
            ConvertedFromSubscriptionId = subscription.Id,
            ManagerIndividualSubscriptionId = subscription.Id,
            ManagerIndividualSeatExempt = true,
        };

        var managerMember = CreateManagerMember(group.Id, snapshot.DisplayName, subscription);
        FamilyManagerProfileHydration.Apply(managerMember, snapshot.DisplayName, snapshot.AgeYears, snapshot.DefaultAddressId);
        managerMember.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(managerMember);

        group.Members.Add(managerMember);
        _db.FamilyGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);

        return MapScreen(group, settings);
    }

    private async Task<Result> AdoptDraftAsync(
        FamilyGroup leftover,
        Subscription subscription,
        ConvertIndividualToFamilyCommand request,
        FamilyPricingSettings settings,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var package = ResolvePackage(settings, request.FamilyPackageId, leftover.SelectedFamilyPackageId);
        if (package.IsFailure) return package.Error;

        leftover.SelectedFamilyPackageId = package.Value.Id;
        leftover.DiscountPercentSnapshot = package.Value.DiscountPercent;
        leftover.ConvertedFromSubscriptionId = subscription.Id;
        leftover.ManagerIndividualSubscriptionId = subscription.Id;
        leftover.ManagerIndividualSeatExempt = true;
        if (!string.IsNullOrWhiteSpace(request.Name))
            leftover.Name = request.Name.Trim();

        var snapshot = await LoadManagerSnapshotAsync(managerId, cancellationToken);
        var managerMember = leftover.Members.FirstOrDefault(m => m.Role == FamilyMemberRole.Manager);
        if (managerMember is null)
        {
            managerMember = CreateManagerMember(leftover.Id, snapshot.DisplayName, subscription);
            leftover.Members.Add(managerMember);
        }
        else
        {
            ApplySubscriptionPlan(managerMember, subscription);
        }

        FamilyManagerProfileHydration.Apply(managerMember, snapshot.DisplayName, snapshot.AgeYears, snapshot.DefaultAddressId);
        managerMember.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(managerMember);

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result<FamilySizePackageSettings> ResolvePackage(
        FamilyPricingSettings settings,
        Guid? requestedPackageId,
        Guid? selectedPackageId)
    {
        if (requestedPackageId is Guid packageId && packageId != Guid.Empty)
        {
            var found = FamilyPackageResolver.FindActive(settings, packageId);
            return found is null
                ? Error.Validation("family.package_not_found", "Selected family package is not available.")
                : found;
        }

        return FamilyPackageResolver.ResolveForGroup(settings, selectedPackageId);
    }

    private static FamilyMember CreateManagerMember(Guid familyGroupId, string displayName, Subscription subscription)
    {
        var member = new FamilyMember
        {
            FamilyGroupId = familyGroupId,
            Role = FamilyMemberRole.Manager,
            DisplayName = displayName,
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 0,
            NutritionalGoalCode = string.Empty,
            ActivityLevel = FamilyActivityLevel.Sedentary,
        };
        ApplySubscriptionPlan(member, subscription);
        return member;
    }

    private static void ApplySubscriptionPlan(FamilyMember member, Subscription subscription)
    {
        member.ProgramId = subscription.ProgramId;
        member.BundleId = subscription.BundleId;
        member.SelectedTier = subscription.SelectedTier;
        member.DurationDays = subscription.DurationDays;
        member.IncludeFriday = subscription.IncludeFriday;
        member.MemberSubscriptionId = subscription.Id;
    }

    private async Task<(string DisplayName, int AgeYears, Guid? DefaultAddressId)> LoadManagerSnapshotAsync(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var account = await _users.FindByIdAsync(managerId, cancellationToken);
        var displayName = string.IsNullOrWhiteSpace(account?.FullName) ? "عضو" : account!.FullName!.Trim();
        var profile = await _db.CustomerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == managerId, cancellationToken);
        var age = FamilyManagerProfileHydration.AgeYearsFromDateOfBirth(profile?.DateOfBirth, _clock.TodayUtc);
        var defaultAddressId = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == managerId && x.RegionId != null && x.RegionId != Guid.Empty)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return (displayName, age, defaultAddressId);
    }

    private Result<Guid> RequireCustomerId()
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid userId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");
        return userId;
    }

    private async Task<Subscription?> FindConvertibleOrPendingAsync(Guid customerId, CancellationToken ct)
        => await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == customerId && x.FamilyGroupId == null
                        && (ConvertibleStatuses.Contains(x.Status) || x.Status == SubscriptionStatus.PendingPayment))
            .OrderByDescending(x => ConvertibleStatuses.Contains(x.Status))
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    private static FamilyGroupScreenDto MapScreen(FamilyGroup group, FamilyPricingSettings settings)
    {
        var package = FamilyPackageResolver.FindActive(settings, group.SelectedFamilyPackageId);
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
            group.Members
                .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(x => x.CreatedAtUtc)
                .Select(m => new FamilyMemberCardDto(
                    m.Id,
                    m.Role,
                    m.DisplayName,
                    m.RelationshipCode,
                    m.Gender,
                    m.AgeYears,
                    m.DeliveryAddressId,
                    m.SetupStatus,
                    m.ProgramId,
                    m.BundleId,
                    m.SelectedTier,
                    m.DurationDays,
                    m.IncludeFriday,
                    m.Role == FamilyMemberRole.Manager,
                    m.PhoneNumber,
                    m.Occupation,
                    m.StartDate))
                .ToList(),
            ResolveCurrentStep(group),
            PackageAvailable: package is not null);
    }

    private static int ResolveCurrentStep(FamilyGroup group)
    {
        if (group.Status is FamilyGroupStatus.CheckoutPending or FamilyGroupStatus.Active) return 4;
        if (group.Members.Count == 0) return 1;
        if (group.Members.Any(m => m.SetupStatus != FamilyMemberSetupStatus.Ready)) return 2;
        return 3;
    }
}
