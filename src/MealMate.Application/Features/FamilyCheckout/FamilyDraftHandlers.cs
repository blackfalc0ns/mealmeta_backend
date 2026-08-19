using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class FamilyDraftHandlers :
    IQueryHandler<GetFamilyDraftMetadataQuery, FamilyDraftMetadataDto>,
    IQueryHandler<GetCurrentFamilyGroupQuery, FamilyGroupScreenDto>,
    ICommandHandler<UpsertFamilyGroupCommand, FamilyGroupScreenDto>,
    ICommandHandler<AddFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<UpdateFamilyMemberCommand, FamilyGroupScreenDto>,
    ICommandHandler<DeleteFamilyMemberCommand, FamilyGroupScreenDto>
{
    private static readonly FamilyGroupStatus[] EditableStatuses =
    [
        FamilyGroupStatus.Draft,
        FamilyGroupStatus.CheckoutPending,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyPricingSettingsReader _settings;

    public FamilyDraftHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyPricingSettingsReader settings)
        => (_db, _currentUser, _users, _settings) = (db, currentUser, users, settings);

    public async Task<Result<FamilyDraftMetadataDto>> Handle(GetFamilyDraftMetadataQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var settings = await _settings.GetAsync(cancellationToken);
        return new FamilyDraftMetadataDto(
            FamilyCatalog.Relationships(),
            FamilyCatalog.Goals(),
            FamilyCatalog.ActivityLevels(),
            settings.MaxMembers,
            settings.DiscountPercent,
            settings.MinMembersForDiscount);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(GetCurrentFamilyGroupQuery request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");

        var settings = await _settings.GetAsync(cancellationToken);
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

        if (group is not null)
        {
            group.Name = name;
            return MapScreen(group, settings);
        }

        var account = await _users.FindByIdAsync(managerId.Value, cancellationToken);
        var displayName = string.IsNullOrWhiteSpace(account?.FullName) ? "عضو" : account!.FullName!.Trim();

        group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId.Value,
            Name = name,
            Status = FamilyGroupStatus.Draft,
            DiscountPercentSnapshot = settings.DiscountPercent,
        };

        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = displayName,
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 0,
            NutritionalGoalCode = string.Empty,
            ActivityLevel = FamilyActivityLevel.Sedentary,
        };
        managerMember.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(managerMember);
        group.Members.Add(managerMember);
        _db.FamilyGroups.Add(group);

        return MapScreen(group, settings);
    }

    public async Task<Result<FamilyGroupScreenDto>> Handle(AddFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = RequireCustomerId();
        if (managerId.IsFailure) return managerId.Error;

        var group = await LoadEditableGroupTrackedAsync(managerId.Value, cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No draft family group was found.");
        if (group.Status != FamilyGroupStatus.Draft)
            return Error.Conflict("family.not_draft", "Family members can only be changed while the group is a draft.");

        var settings = await _settings.GetAsync(cancellationToken);
        if (group.Members.Count >= settings.MaxMembers)
            return Error.Conflict("family.max_members", "Family member limit reached.");

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

        var validated = await ValidateMemberWriteAsync(managerId.Value, request.Member, cancellationToken);
        if (validated.IsFailure) return validated.Error;

        ApplyWrite(member, request.Member);
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);

        var settings = await _settings.GetAsync(cancellationToken);
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
            return Error.Conflict("family.not_draft", "The manager member cannot be deleted.");
        if (group.Members.Count <= 1)
            return Error.Conflict("family.not_draft", "At least one family member is required.");

        group.Members.Remove(member);
        _db.FamilyMembers.Remove(member);

        var settings = await _settings.GetAsync(cancellationToken);
        return MapScreen(group, settings);
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
        if (FamilyCatalog.Goals().All(x => x.Code != write.NutritionalGoalCode))
            return Error.Validation("family.goal_invalid", "Nutritional goal code is invalid.");

        var addressOwned = await _db.CustomerAddresses.AsNoTracking()
            .AnyAsync(x => x.Id == write.DeliveryAddressId && x.UserId == managerId, cancellationToken);
        if (!addressOwned)
            return Error.Validation("family.address_invalid", "Delivery address must belong to the manager.");

        return Result.Success();
    }

    private static void ApplyWrite(FamilyMember member, FamilyMemberWriteDto write)
    {
        member.DisplayName = write.DisplayName.Trim();
        member.RelationshipCode = write.RelationshipCode;
        member.Gender = write.Gender;
        member.AgeYears = write.AgeYears;
        member.NutritionalGoalCode = write.NutritionalGoalCode;
        member.ActivityLevel = write.ActivityLevel;
        member.DeliveryAddressId = write.DeliveryAddressId;
    }

    private static FamilyGroupScreenDto MapScreen(FamilyGroup group, FamilyPricingSettings settings)
        => new(
            group.Id,
            group.Name,
            group.Status,
            settings.MaxMembers,
            settings.DiscountPercent,
            group.Members
                .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(x => x.CreatedAtUtc)
                .Select(MapMember)
                .ToList(),
            ResolveCurrentStep(group));

    private static FamilyMemberCardDto MapMember(FamilyMember member)
        => new(
            member.Id,
            member.Role,
            member.DisplayName,
            member.RelationshipCode,
            member.Gender,
            member.AgeYears,
            member.NutritionalGoalCode,
            member.ActivityLevel,
            member.DeliveryAddressId,
            member.SetupStatus,
            member.ProgramId,
            member.BundleId,
            member.SelectedTier,
            member.DurationDays,
            member.IncludeFriday);

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
