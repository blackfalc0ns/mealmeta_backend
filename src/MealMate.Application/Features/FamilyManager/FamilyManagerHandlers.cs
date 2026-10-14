using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Helpers;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed class FamilyManagerHandlers :
    IQueryHandler<GetFamilyHomeQuery, FamilyHomeDto>,
    IQueryHandler<GetFamilyMembersQuery, IReadOnlyList<FamilyMemberListItemDto>>,
    IQueryHandler<GetFamilySwitcherQuery, IReadOnlyList<FamilySwitcherItemDto>>,
    IQueryHandler<GetFamilyMemberQuery, FamilyMemberDetailDto>,
    ICommandHandler<CreateFamilyMemberCommand, FamilyMemberDetailDto>,
    IQueryHandler<GetFamilyMemberAccessQuery, FamilyMemberAccessDto>,
    ICommandHandler<UpdateFamilyMemberAccessCommand, FamilyMemberAccessDto>,
    IQueryHandler<GetFamilyMemberPermissionsQuery, FamilyMemberPermissionsDto>,
    ICommandHandler<UpdateFamilyMemberPermissionsCommand, FamilyMemberPermissionsDto>,
    ICommandHandler<AcceptFamilyInviteCommand, FamilyMemberAccessDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IFamilyPricingSettingsReader _pricing;
    private readonly IUserAccountService _users;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IFamilyCancellationSettlement? _settlements;

    public FamilyManagerHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IFamilyPricingSettingsReader pricing,
        IUserAccountService users, ILocalizedMessageProvider? messages = null, IFamilyCancellationSettlement? settlements = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _pricing = pricing;
        _users = users;
        _messages = messages ?? NullLocalizedMessageProvider.Instance;
        _settlements = settlements;
    }

    public async Task<Result<FamilyHomeDto>> Handle(GetFamilyHomeQuery request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var group = groupResult.Value;

        var allMembers = await ActiveMembersQuery(group.Id).Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var members = allMembers.Where(x => x.Role != FamilyMemberRole.Manager).ToList();

        var accessByMember = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);

        var today = BusinessToday;
        var serviceSubscriptions = await ResolveServiceSubscriptionsAsync(members, today, cancellationToken);
        var subIds = serviceSubscriptions.Values.Select(x => x.CurrentId ?? x.UpcomingId).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var todayDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => subIds.Contains(d.SubscriptionId) && d.ServiceDate == today)
            .ToListAsync(cancellationToken);
        var dayIds = todayDays.Select(d => d.Id).ToList();
        var filledByDay = await _db.DailyMealSelections.AsNoTracking()
            .Where(s => dayIds.Contains(s.SubscriptionDayId))
            .GroupBy(s => s.SubscriptionDayId)
            .Select(g => new { DayId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DayId, x => x.Count, cancellationToken);

        var remaining = await LoadRemainingDaysAsync(subIds, today, cancellationToken);
        var lines = await LoadLatestLinesAsync(members, serviceSubscriptions.ToDictionary(x => x.Key, x => x.Value.CurrentId ?? x.Value.UpcomingId), cancellationToken);
        var package = await ResolvePackageAsync(group, cancellationToken);
        var bundleIds = members.Where(m => m.BundleId != null).Select(m => m.BundleId!.Value).Distinct().ToList();
        var bundles = await _db.Bundles.AsNoTracking()
            .Where(b => bundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var programIds = members.Where(m => m.ProgramId.HasValue).Select(m => m.ProgramId!.Value).Distinct().ToList();
        var programs = await _db.Programs.AsNoTracking().Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var homeMembers = members.Select(m =>
        {
            accessByMember.TryGetValue(m.Id, out var access);
            var projection = serviceSubscriptions.GetValueOrDefault(m.Id, new FamilyServiceSubscriptionProjection(null, null, null, null));
            var service = projection.Current ?? projection.Upcoming;
            programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var memberProgram);
            var day = projection.CurrentId is { } sid
                ? todayDays.FirstOrDefault(d => d.SubscriptionId == sid)
                : null;
            int? filled = day is not null && filledByDay.TryGetValue(day.Id, out var c) ? c : null;
            int? required = null;
            if (m.BundleId is { } bid && bundles.TryGetValue(bid, out var bundle))
                required = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            return new FamilyHomeMemberDto(
                m.Id, m.DisplayName, m.Role, access?.IsPaused == true,
                projection.CurrentId,
                service?.Program?.NameAr ?? memberProgram?.NameAr,
                service?.Program?.NameEn ?? memberProgram?.NameEn,
                day?.Status.ToString(), filled, required, remaining.GetValueOrDefault((projection.CurrentId ?? projection.UpcomingId) ?? Guid.Empty),
                Lifecycle(m, access, lines.GetValueOrDefault(m.Id), service, projectionResolved: true),
                projection.UpcomingId, projection.UpcomingStartDate);
        }).ToList();

        var shortcuts = new List<FamilyShortcutDto>
        {
            new("calendar", true, null),
            new("meals", true, null),
            new("subscriptions", true, null),
            new("wallet", false, "wallet_not_implemented")
        };

        return Result.Success(new FamilyHomeDto(
            group.Id,
            group.Name,
            group.Status,
            package?.NameAr,
            package?.NameEn,
            group.QuotedFinalPrice,
            group.CurrencyCode,
            homeMembers,
            homeMembers.Where(x => x.TodayUiStatus is not null && x.TodayUiStatus != nameof(SubscriptionDayStatus.Cancelled)
                && x.TodayUiStatus != nameof(SubscriptionDayStatus.Frozen)).ToList(),
            shortcuts,
            new FamilyWalletDto(false, "wallet_not_implemented")));
    }

    public async Task<Result<IReadOnlyList<FamilyMemberListItemDto>>> Handle(GetFamilyMembersQuery request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var today = BusinessToday;
        var allMembers = await ActiveMembersQuery(groupResult.Value.Id)
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var members = allMembers.Where(x => x.Role != FamilyMemberRole.Manager).ToList();
        var serviceSubscriptions = await ResolveServiceSubscriptionsAsync(members, today, cancellationToken);
        var access = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);
        var programIds = members.Where(m => m.ProgramId != null).Select(m => m.ProgramId!.Value).Distinct().ToList();
        var programs = await _db.Programs.AsNoTracking()
            .Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var bundleIds = members.SelectMany(m => new[] { m.BundleId,
                serviceSubscriptions.GetValueOrDefault(m.Id)?.Current?.BundleId,
                serviceSubscriptions.GetValueOrDefault(m.Id)?.Upcoming?.BundleId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var bundles = await _db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var profileOwners = members.Select(m => m.Role == FamilyMemberRole.Manager
                ? _currentUser.UserId
                : access.GetValueOrDefault(m.Id)?.LinkedCustomerUserId)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var profileImages = await _db.CustomerProfiles.AsNoTracking()
            .Where(p => profileOwners.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.ProfileImageStorageKey, cancellationToken);

        var currentSubscriptionIds = serviceSubscriptions.Values.Where(x => x.CurrentId.HasValue)
            .Select(x => x.CurrentId!.Value).Distinct().ToList();
        var todayDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => d.ServiceDate == today && currentSubscriptionIds.Contains(d.SubscriptionId))
            .ToListAsync(cancellationToken);
        var todayDayIds = todayDays.Select(d => d.Id).ToList();
        var todaySelections = await _db.DailyMealSelections.AsNoTracking()
            .Where(s => todayDayIds.Contains(s.SubscriptionDayId))
            .Select(s => new { s.SubscriptionDayId, s.MealId }).ToListAsync(cancellationToken);
        var filledByDay = todaySelections.GroupBy(x => x.SubscriptionDayId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.MealId).Distinct().Count());

        var serviceIds = serviceSubscriptions.Values.SelectMany(x => new[] { x.CurrentId, x.UpcomingId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var calendarDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => serviceIds.Contains(d.SubscriptionId))
            .Select(d => new { d.SubscriptionId, d.Status }).ToListAsync(cancellationToken);
        var calendarUsage = calendarDays.GroupBy(x => x.SubscriptionId).ToDictionary(g => g.Key, g => new
        {
            Total = g.Count(x => x.Status != SubscriptionDayStatus.Cancelled),
            Used = g.Count(x => x.Status == SubscriptionDayStatus.Delivered)
        });

        var remaining = await LoadRemainingDaysAsync(serviceSubscriptions.Values.Select(x => x.CurrentId ?? x.UpcomingId)
            .Where(x => x.HasValue).Select(x => x!.Value).ToList(), today, cancellationToken);
        var lines = await LoadLatestLinesAsync(members, serviceSubscriptions.ToDictionary(x => x.Key, x => x.Value.CurrentId ?? x.Value.UpcomingId), cancellationToken);
        IReadOnlyList<FamilyMemberListItemDto> list = members.Select(m =>
        {
            access.TryGetValue(m.Id, out var memberAccess);
            programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var program);
            var projection = serviceSubscriptions.GetValueOrDefault(m.Id, new FamilyServiceSubscriptionProjection(null, null, null, null));
            var service = projection.Current ?? projection.Upcoming;
            var programAr = service?.Program?.NameAr ?? program?.NameAr;
            var programEn = service?.Program?.NameEn ?? program?.NameEn;
            bundles.TryGetValue(service?.BundleId ?? m.BundleId ?? Guid.Empty, out var bundle);
            var fundedId = projection.CurrentId ?? projection.UpcomingId;
            var fundedService = projection.Current ?? projection.Upcoming;
            var hasFundedService = fundedId.HasValue && fundedService is not null;
            var planMeals = bundle is null ? (int?)null : bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            var hasSavedPlan = m.ProgramId.HasValue || m.BundleId.HasValue;
            FamilyPlanSummaryDto? planSummary = null;
            if (hasFundedService && fundedService is not null)
            {
                var usage = calendarUsage.GetValueOrDefault(fundedService.Id);
                var total = usage?.Total;
                var used = usage?.Used;
                decimal? percentage = usage is null ? null : usage.Total == 0 ? 0m : Math.Round((decimal)usage.Used * 100m / usage.Total, 2);
                planSummary = new FamilyPlanSummaryDto(planMeals, fundedService.DurationDays,
                    fundedService.StartDate, fundedService.EndDate, fundedService.EndDate.AddDays(1), total, used, percentage);
            }
            else if (hasSavedPlan)
            {
                planSummary = new FamilyPlanSummaryDto(planMeals, m.DurationDays,
                    m.StartDate.HasValue && m.DurationDays is > 0 ? m.StartDate : null,
                    null, null, null, null, null);
            }
            var currentDay = projection.CurrentId is { } currentId
                ? todayDays.FirstOrDefault(d => d.SubscriptionId == currentId)
                : null;
            FamilyTodayMealSelectionDto? todaySelection = null;
            if (currentDay is not null && planMeals.HasValue)
                todaySelection = new FamilyTodayMealSelectionDto(today, currentDay.Status.ToString(),
                    filledByDay.GetValueOrDefault(currentDay.Id), planMeals.Value, currentDay.IsAutoSelected);
            var ownerId = m.Role == FamilyMemberRole.Manager ? _currentUser.UserId : memberAccess?.LinkedCustomerUserId;
            var imageUrl = ownerId.HasValue && profileImages.TryGetValue(ownerId.Value, out var imageKey)
                ? MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(imageKey) : null;
            return new FamilyMemberListItemDto(
                m.Id, m.DisplayName, m.Role, m.AgeYears, memberAccess?.IsPaused == true,
                projection.CurrentId, programAr, programEn, remaining.GetValueOrDefault((projection.CurrentId ?? projection.UpcomingId) ?? Guid.Empty), m.SetupStatus,
                Lifecycle(m, memberAccess, lines.GetValueOrDefault(m.Id), service, projectionResolved: true),
                projection.UpcomingId, projection.UpcomingStartDate, imageUrl,
                memberAccess?.HasSeparateAccount == true, planSummary, todaySelection);
        }).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IReadOnlyList<FamilySwitcherItemDto>>> Handle(GetFamilySwitcherQuery request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;
        var today = BusinessToday;
        var members = await ActiveMembersQuery(groupResult.Value.Id)
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var serviceSubscriptions = await ResolveServiceSubscriptionsAsync(members, today, cancellationToken);
        var access = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);
        var permissions = await _db.FamilyMemberPermissions.AsNoTracking()
            .Where(x => members.Select(m => m.Id).Contains(x.FamilyMemberId))
            .ToDictionaryAsync(x => x.FamilyMemberId, cancellationToken);
        var programIds = members.Where(m => m.ProgramId != null).Select(m => m.ProgramId!.Value).Distinct().ToList();
        var programs = await _db.Programs.AsNoTracking()
            .Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var profileOwners = members.Select(m => m.Role == FamilyMemberRole.Manager
                ? _currentUser.UserId
                : access.GetValueOrDefault(m.Id)?.LinkedCustomerUserId)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var profileImages = await _db.CustomerProfiles.AsNoTracking()
            .Where(p => profileOwners.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.ProfileImageStorageKey, cancellationToken);

        IReadOnlyList<FamilySwitcherItemDto> list = members.Select(m =>
        {
            var isManager = m.Role == FamilyMemberRole.Manager;
            access.TryGetValue(m.Id, out var memberAccess);
            permissions.TryGetValue(m.Id, out var memberPerms);
            programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var program);
            var projection = serviceSubscriptions.GetValueOrDefault(m.Id, new FamilyServiceSubscriptionProjection(null, null, null, null));
            var service = projection.Current ?? projection.Upcoming;
            var programAr = service?.Program?.NameAr ?? program?.NameAr;
            var programEn = service?.Program?.NameEn ?? program?.NameEn;
            var ownerId = isManager ? _currentUser.UserId : memberAccess?.LinkedCustomerUserId;
            var imageUrl = ownerId.HasValue && profileImages.TryGetValue(ownerId.Value, out var imageKey)
                ? MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(imageKey) : null;
            var canEditDailyMeals = isManager || (memberAccess?.IsPaused != true && memberPerms?.EditDailyMeals == true);
            var resolvedSubId = projection.CurrentId ?? projection.UpcomingId ?? m.MemberSubscriptionId;

            return new FamilySwitcherItemDto(
                m.Id,
                m.DisplayName,
                m.Role,
                isManager,
                resolvedSubId,
                projection.UpcomingId,
                projection.UpcomingStartDate,
                programAr,
                programEn,
                imageUrl,
                memberAccess?.IsPaused == true,
                canEditDailyMeals,
                m.SetupStatus);
        }).ToList();

        return Result.Success(list);
    }

    public async Task<Result<FamilyMemberDetailDto>> Handle(GetFamilyMemberQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, access) = loaded.Value;
        var serviceProjection = (await ResolveServiceSubscriptionsAsync([member], BusinessToday, cancellationToken))
            .GetValueOrDefault(member.Id, new FamilyServiceSubscriptionProjection(null, null, null, null));
        var serviceSubscription = serviceProjection.Current ?? serviceProjection.Upcoming;
        var programId = serviceSubscription?.ProgramId ?? member.ProgramId;
        var program = programId is { } pid
            ? await _db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, cancellationToken)
            : null;
        var programs = program is null ? new Dictionary<Guid, Program>() : new Dictionary<Guid, Program> { [program.Id] = program };
        var nutrition = await LoadTodayNutritionAsync(serviceProjection.CurrentId, cancellationToken);

        var linkedUserPhone = (string?)null;
        if (string.IsNullOrWhiteSpace(member.PhoneNumber) && access?.LinkedCustomerUserId is Guid linkedUserId)
        {
            var user = await _users.FindByIdAsync(linkedUserId, cancellationToken);
            linkedUserPhone = user?.PhoneNumber;
        }

        var serviceSubscriptionId = serviceProjection.CurrentId ?? serviceProjection.UpcomingId ?? member.MemberSubscriptionId;
        var remaining = await LoadRemainingDaysAsync(serviceSubscriptionId.HasValue ? [serviceSubscriptionId.Value] : [], BusinessToday, cancellationToken);
        var lines = await LoadLatestLinesAsync([member], new Dictionary<Guid, Guid?> { [member.Id] = serviceSubscriptionId }, cancellationToken);
        return Result.Success(MapDetail(member, access, programs, remaining.GetValueOrDefault(serviceSubscriptionId ?? Guid.Empty), nutrition,
            linkedUserPhone, Lifecycle(member, access, lines.GetValueOrDefault(member.Id), serviceSubscription, projectionResolved: true), serviceProjection.UpcomingId,
            serviceProjection.UpcomingStartDate, serviceSubscription, serviceProjection.CurrentId, projectionResolved: true));
    }

    public async Task<Result<FamilyMemberDetailDto>> Handle(CreateFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        if (groupResult.IsFailure) return groupResult.Error;

        return Error.Conflict(
            MessageCodes.Family.UseCheckoutFlow,
            "Add and pay for a new member via /api/v1/customer/family-subscription-flow");
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(GetFamilyMemberAccessQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var access = await EnsureAccessAsync(loaded.Value.Member, cancellationToken);
        var existingLogin = await HasExistingLoginAsync(access, loaded.Value.Member, cancellationToken);

        var linkedUserPhone = (string?)null;
        if (string.IsNullOrWhiteSpace(loaded.Value.Member.PhoneNumber) && access.LinkedCustomerUserId is Guid linkedUserId)
        {
            var user = await _users.FindByIdAsync(linkedUserId, cancellationToken);
            linkedUserPhone = user?.PhoneNumber;
        }

        return Result.Success(MapAccess(access, loaded.Value.Member, existingLogin, linkedUserPhone: linkedUserPhone));
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(UpdateFamilyMemberAccessCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, _) = loaded.Value;
        if (request.Request.Remove == true)
        {
            if (member.Role == FamilyMemberRole.Manager) return Error.Conflict(MessageCodes.Family.CannotRemoveManager);
            if (_settlements is null) return Error.Failure("family.settlement_dependency_missing");
            var removal = await _settlements.RemoveAsync(member.Id, _currentUser.UserId!.Value, cancellationToken);
            if (removal.IsFailure) return removal.Error;
            var updated = await _db.FamilyMemberAccesses.AsNoTracking().FirstAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
            return MapAccess(updated, member, await HasExistingLoginAsync(updated, member, cancellationToken));
        }
        var access = await EnsureAccessAsync(member, cancellationToken);

        if (request.Request.IsPaused is { } paused)
        {
            access.IsPaused = paused;
            access.PausedAtUtc = paused ? _clock.UtcNow : null;
        }

        string? generatedPassword = null;
        string? inviteRawToken = null;
        var existingLogin = false;

        if (request.Request.HasSeparateAccount is { } separate)
        {
            access.HasSeparateAccount = separate;
            if (separate)
            {
                var provisioned = await ProvisionSeparateAccountAsync(member, access, cancellationToken);
                if (provisioned.IsFailure) return provisioned.Error;
                generatedPassword = provisioned.Value.GeneratedPassword;
                existingLogin = provisioned.Value.HasExistingLogin;

                if (access.LinkedCustomerUserId is null)
                {
                    inviteRawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    access.InviteTokenHash = HashToken(inviteRawToken);
                    access.InviteExpiresAtUtc = _clock.UtcNow.AddDays(7);
                }
            }

            if (!separate)
            {
                access.InviteTokenHash = null;
                access.InviteExpiresAtUtc = null;
            }
        }

        var linkedUserPhone = (string?)null;
        if (string.IsNullOrWhiteSpace(member.PhoneNumber) && access.LinkedCustomerUserId is Guid linkedUserId)
        {
            var user = await _users.FindByIdAsync(linkedUserId, cancellationToken);
            linkedUserPhone = user?.PhoneNumber;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access, member, existingLogin, inviteRawToken, generatedPassword, linkedUserPhone));
    }

    public async Task<Result<FamilyMemberPermissionsDto>> Handle(GetFamilyMemberPermissionsQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var perms = await _db.FamilyMemberPermissions.AsNoTracking().FirstOrDefaultAsync(x => x.FamilyMemberId == loaded.Value.Member.Id, cancellationToken)
            ?? new FamilyMemberPermissions { FamilyMemberId = loaded.Value.Member.Id };
        return Result.Success(MapPermissions(perms));
    }

    public async Task<Result<FamilyMemberPermissionsDto>> Handle(UpdateFamilyMemberPermissionsCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        if (loaded.Value.Member.Role == FamilyMemberRole.Manager)
            return Error.Conflict(MessageCodes.Family.ManagerPermissionsImmutable);

        if (request.Request.ExpectedVersion is null)
            return Error.Validation("family.permissions_version_required");
        var perms = await EnsurePermissionsAsync(loaded.Value.Member, cancellationToken);
        if (perms.Version != request.Request.ExpectedVersion)
            return Error.Conflict("family.permissions_changed");
        perms.EditDailyMeals = request.Request.EditDailyMeals;
        perms.ChangeDeliveryTime = request.Request.ChangeDeliveryTime;
        perms.ViewProgram = request.Request.ViewProgram;
        perms.ChangeProgram = request.Request.ChangeProgram;
        perms.EditMealsAndDays = request.Request.EditMealsAndDays;
        perms.EditAddress = request.Request.EditAddress;
        perms.UnitExchange = request.Request.UnitExchange;
        perms.ChangePassword = request.Request.ChangePassword;
        if (request.Request.ViewCalendar is { } viewCalendar) perms.ViewCalendar = viewCalendar;
        if (request.Request.ViewInvoices is { } viewInvoices) perms.ViewInvoices = viewInvoices;
        if (request.Request.SelectRestaurant is { } selectRestaurant) perms.SelectRestaurant = selectRestaurant;
        if (request.Request.ConfirmMeals is { } confirmMeals) perms.ConfirmMeals = confirmMeals;
        if (request.Request.RequestFreeze is { } requestFreeze) perms.RequestFreeze = requestFreeze;
        if (request.Request.RequestRenewal is { } requestRenewal) perms.RequestRenewal = requestRenewal;
        if (request.Request.RequestUpgrade is { } requestUpgrade) perms.RequestUpgrade = requestUpgrade;
        if (request.Request.CancelSubscription is { } cancelSubscription) perms.CancelSubscription = cancelSubscription;
        if (request.Request.ViewFamilyCalendars is { } viewFamilyCalendars) perms.ViewFamilyCalendars = viewFamilyCalendars;
        if (request.Request.ViewFamilyPlans is { } viewFamilyPlans) perms.ViewFamilyPlans = viewFamilyPlans;
        if (request.Request.ViewFamilyInvoices is { } viewFamilyInvoices) perms.ViewFamilyInvoices = viewFamilyInvoices;
        var inserting = _db is DbContext tracked && tracked.Entry(perms).State == EntityState.Added;
        perms.Version++;
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext context) context.ChangeTracker.Clear();
            return Error.Conflict("family.permissions_changed");
        }
        catch (DbUpdateException exception) when (inserting && IsPermissionInsertCollision(exception))
        {
            if (_db is DbContext context) context.ChangeTracker.Clear();
            if (!await _db.FamilyMemberPermissions.AsNoTracking().AnyAsync(x => x.FamilyMemberId == request.MemberId, cancellationToken)) throw;
            return Error.Conflict("family.permissions_changed");
        }
        return Result.Success(MapPermissions(perms));
    }

    public async Task<Result<FamilyMemberAccessDto>> Handle(AcceptFamilyInviteCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var hash = HashToken(request.Token.Trim());
        var access = await _db.FamilyMemberAccesses
            .Include(x => x.FamilyMember)
            .FirstOrDefaultAsync(x => x.InviteTokenHash == hash, cancellationToken);
        if (access is null || access.InviteExpiresAtUtc is null || access.InviteExpiresAtUtc < _clock.UtcNow)
            return Error.Validation(MessageCodes.Family.InviteInvalid);
        if (access.RemovedAtUtc is not null)
            return Error.Conflict(MessageCodes.Family.InviteConflict);
        if (access.LinkedCustomerUserId is not null && access.LinkedCustomerUserId != _currentUser.UserId)
            return Error.Conflict(MessageCodes.Family.InviteConflict);

        var alreadyLinked = await _db.FamilyMemberAccesses.AsNoTracking()
            .AnyAsync(x => x.LinkedCustomerUserId == _currentUser.UserId && x.Id != access.Id && x.RemovedAtUtc == null, cancellationToken);
        if (alreadyLinked)
            return Error.Conflict(MessageCodes.Family.InviteConflict);

        access.LinkedCustomerUserId = _currentUser.UserId;
        access.HasSeparateAccount = true;
        access.InviteTokenHash = null;
        access.InviteExpiresAtUtc = null;

        await FamilyMemberAddressHydration.PreferLinkedCustomerAddressAsync(
            _db,
            access.FamilyMember,
            _currentUser.UserId.Value,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access, access.FamilyMember, hasExistingLogin: true));
    }

    private async Task<Result<FamilyGroup>> RequireActiveManagerGroupAsync(CancellationToken ct)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var group = await _db.FamilyGroups
            .FirstOrDefaultAsync(x => x.ManagerCustomerUserId == _currentUser.UserId &&
                (x.Status == FamilyGroupStatus.Active || x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending), ct);
        if (group is null)
            return Error.Forbidden(MessageCodes.Family.NotManager);
        return Result.Success(group);
    }

    private IQueryable<FamilyMember> ActiveMembersQuery(Guid groupId)
        => _db.FamilyMembers
            .Where(m => m.FamilyGroupId == groupId)
            .Where(m => !_db.FamilyMemberAccesses.Any(a => a.FamilyMemberId == m.Id && a.RemovedAtUtc != null));

    private async Task<Result<(FamilyMember Member, FamilyMemberAccess? Access)>> LoadManagedMemberAsync(Guid memberId, CancellationToken ct)
    {
        var groupResult = await RequireActiveManagerGroupAsync(ct);
        if (groupResult.IsFailure) return groupResult.Error;

        var member = await _db.FamilyMembers
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.FamilyGroupId == groupResult.Value.Id, ct);
        if (member is null) return Error.NotFound(MessageCodes.Family.MemberNotFound);

        var access = await _db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (access?.RemovedAtUtc is not null) return Error.NotFound(MessageCodes.Family.MemberNotFound);
        return Result.Success((member, access));
    }

    private Task<Dictionary<Guid, FamilyServiceSubscriptionProjection>> ResolveServiceSubscriptionsAsync(
        IReadOnlyCollection<FamilyMember> members, DateOnly today, CancellationToken ct)
    {
        return new FamilyServiceSubscriptionResolver(_db).ResolveAsync(members.Select(x => x.Id).ToArray(),
            members.ToDictionary(x => x.Id, x => x.MemberSubscriptionId), today, ct);
    }

    private async Task<FamilyMemberAccess> EnsureAccessAsync(FamilyMember member, CancellationToken ct)
    {
        var access = await _db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (access is not null) return access;
        access = new FamilyMemberAccess { FamilyMemberId = member.Id };
        _db.FamilyMemberAccesses.Add(access);
        await _db.SaveChangesAsync(ct);
        return access;
    }

    private async Task<FamilyMemberPermissions> EnsurePermissionsAsync(FamilyMember member, CancellationToken ct)
    {
        var perms = await _db.FamilyMemberPermissions.FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, ct);
        if (perms is not null) return perms;
        perms = new FamilyMemberPermissions { FamilyMemberId = member.Id };
        _db.FamilyMemberPermissions.Add(perms);
        return perms;
    }

    private static bool IsPermissionInsertCollision(DbUpdateException exception)
    {
        var provider = exception.InnerException;
        return provider?.GetType().GetProperty("SqlState")?.GetValue(provider)?.ToString() == "23505"
            && provider.GetType().GetProperty("ConstraintName")?.GetValue(provider)?.ToString()
                == "IX_FamilyMemberPermissions_FamilyMemberId";
    }

    private async Task<ProgramsSubscriptions.FamilySizePackageSettings?> ResolvePackageAsync(FamilyGroup group, CancellationToken ct)
    {
        var settings = await _pricing.GetAsync(ct);
        var resolved = FamilyPackageResolver.ResolveForGroup(settings, group.SelectedFamilyPackageId);
        return resolved.IsSuccess ? resolved.Value : null;
    }

    private async Task<FamilyTodayNutritionDto?> LoadTodayNutritionAsync(Guid? subscriptionId, CancellationToken ct)
    {
        if (subscriptionId is null) return null;
        var today = BusinessToday;
        var meals = await (
            from selection in _db.DailyMealSelections.AsNoTracking()
            join day in _db.SubscriptionDays.AsNoTracking() on selection.SubscriptionDayId equals day.Id
            join meal in _db.Meals.AsNoTracking() on selection.MealId equals meal.Id
            where day.SubscriptionId == subscriptionId.Value && day.ServiceDate == today
            select meal).ToListAsync(ct);
        if (meals.Count == 0) return null;
        return new FamilyTodayNutritionDto(
            meals.Sum(m => m.Calories ?? 0),
            meals.Sum(m => m.ProteinG ?? 0),
            meals.Sum(m => m.CarbsG ?? 0),
            meals.Sum(m => m.FatG ?? 0));
    }

    private async Task<Result<(bool HasExistingLogin, string? GeneratedPassword)>> ProvisionSeparateAccountAsync(
        FamilyMember member,
        FamilyMemberAccess access,
        CancellationToken ct)
    {
        var account = access.LinkedCustomerUserId is { } linkedId
            ? await _users.FindByIdAsync(linkedId, ct)
            : null;
        if (account is null && !string.IsNullOrWhiteSpace(member.PhoneNumber))
            account = await _users.FindByPhoneAsync(member.PhoneNumber, ct);

        if (account is null)
            return Result.Success<(bool, string?)>((false, null));

        var alreadyLinked = access.LinkedCustomerUserId == account.Id;
        access.LinkedCustomerUserId = account.Id;
        if (alreadyLinked || account.LastLoginAtUtc is not null)
            return Result.Success<(bool, string?)>((true, null));

        var password = GeneratePassword();
        var token = await _users.GeneratePasswordResetTokenAsync(account.Id, ct);
        var reset = await _users.ResetPasswordAsync(account.Id, token, password, ct);
        if (reset.IsFailure) return reset.Error;
        return Result.Success<(bool, string?)>((false, password));
    }

    private async Task<bool> HasExistingLoginAsync(FamilyMemberAccess access, FamilyMember member, CancellationToken ct)
    {
        if (access.LinkedCustomerUserId is { } linkedId)
        {
            var linked = await _users.FindByIdAsync(linkedId, ct);
            return linked?.LastLoginAtUtc is not null;
        }

        if (string.IsNullOrWhiteSpace(member.PhoneNumber)) return false;
        var byPhone = await _users.FindByPhoneAsync(member.PhoneNumber, ct);
        return byPhone?.LastLoginAtUtc is not null;
    }

    private static FamilyMemberDetailDto MapDetail(
        FamilyMember m,
        FamilyMemberAccess? access,
        IReadOnlyDictionary<Guid, Program> programs,
        int remainingDays,
        FamilyTodayNutritionDto? nutrition,
        string? linkedUserPhone = null, FamilyMemberLifecycleDto? lifecycle = null,
        Guid? upcomingSubscriptionId = null, DateOnly? upcomingStartDate = null,
        Subscription? serviceSubscription = null, Guid? serviceSubscriptionId = null, bool projectionResolved = false)
    {
        programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var program);
        var subscription = projectionResolved ? serviceSubscription : serviceSubscription ?? m.MemberSubscription;
        var canRenew = serviceSubscriptionId is not null && subscription is not null && CanRenew(subscription.Status);
        var renewalPath = subscription is not null && canRenew
            ? $"/api/v1/customer/subscriptions/{subscription.Id}/renewal-requests"
            : null;

        var rawPhone = !string.IsNullOrWhiteSpace(m.PhoneNumber) ? m.PhoneNumber : linkedUserPhone;
        var formattedPhone = PhoneNumberNormalizer.ToInternationalFormat(rawPhone) ?? PhoneNumberNormalizer.Normalize(rawPhone) ?? rawPhone;
        var (countryCode, nationalNumber) = PhoneNumberNormalizer.ExtractCountryCodeAndNationalNumber(rawPhone);

        return new FamilyMemberDetailDto(
            m.Id, m.DisplayName, m.Role, m.RelationshipCode, m.Gender, m.AgeYears,
            m.NutritionalGoalCode, m.ActivityLevel, m.SetupStatus,
            m.ProgramId, m.BundleId, m.SelectedTier, m.DurationDays, m.IncludeFriday,
            serviceSubscriptionId ?? (projectionResolved ? null : subscription?.Id), subscription?.Program?.NameAr ?? program?.NameAr,
            subscription?.Program?.NameEn ?? program?.NameEn,
            access?.IsPaused == true, access?.HasSeparateAccount == true, access?.LinkedCustomerUserId,
            formattedPhone, remainingDays,
            subscription?.StartDate, subscription?.EndDate, canRenew, renewalPath, nutrition,
            countryCode, nationalNumber, lifecycle, upcomingSubscriptionId, upcomingStartDate);
    }

    private static FamilyMemberAccessDto MapAccess(
        FamilyMemberAccess access,
        FamilyMember? member,
        bool hasExistingLogin,
        string? inviteRawToken = null,
        string? generatedPassword = null,
        string? linkedUserPhone = null)
    {
        var rawPhone = !string.IsNullOrWhiteSpace(member?.PhoneNumber) ? member.PhoneNumber : linkedUserPhone;
        var formattedPhone = PhoneNumberNormalizer.ToInternationalFormat(rawPhone) ?? PhoneNumberNormalizer.Normalize(rawPhone) ?? rawPhone;
        var (countryCode, nationalNumber) = PhoneNumberNormalizer.ExtractCountryCodeAndNationalNumber(rawPhone);

        return new FamilyMemberAccessDto(
            access.FamilyMemberId,
            access.HasSeparateAccount,
            access.LinkedCustomerUserId,
            access.IsPaused,
            access.PausedAtUtc,
            access.RemovedAtUtc,
            inviteRawToken is null ? null : $"mealmate://family/invite/{inviteRawToken}",
            access.InviteExpiresAtUtc,
            formattedPhone,
            hasExistingLogin,
            generatedPassword,
            countryCode,
            nationalNumber);
    }

    private FamilyMemberPermissionsDto MapPermissions(FamilyMemberPermissions p)
        => new(
            p.FamilyMemberId, p.EditDailyMeals, p.ChangeDeliveryTime, p.ViewProgram, p.ChangeProgram,
            p.EditMealsAndDays, p.EditAddress, p.UnitExchange, p.ChangePassword,
            FamilyPermissionCatalog.Groups(p, _messages),
            p.Version, p.ViewCalendar, p.ViewInvoices, p.SelectRestaurant, p.ConfirmMeals,
            p.RequestFreeze, p.RequestRenewal, p.RequestUpgrade, p.CancelSubscription,
            p.ViewFamilyCalendars, p.ViewFamilyPlans, p.ViewFamilyInvoices);

    private DateOnly BusinessToday => BusinessCalendarDate.KuwaitToday(_clock.UtcNow);

    private async Task<Dictionary<Guid, int>> LoadRemainingDaysAsync(IReadOnlyList<Guid> subscriptionIds, DateOnly today, CancellationToken ct)
        => await _db.SubscriptionDays.AsNoTracking().Where(x => subscriptionIds.Contains(x.SubscriptionId)
            && x.ServiceDate >= today && x.Status != SubscriptionDayStatus.Cancelled
            && x.Status != SubscriptionDayStatus.Delivered && x.Status != SubscriptionDayStatus.Frozen)
            .GroupBy(x => x.SubscriptionId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

    private async Task<Dictionary<Guid, FamilyCheckoutBatchLine>> LoadLatestLinesAsync(IReadOnlyList<FamilyMember> members,
        IReadOnlyDictionary<Guid, Guid?>? currentSubscriptionIds, CancellationToken ct)
    {
        var ids = members.Select(x => x.Id).ToList();
        var lines = await _db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
            .Where(x => ids.Contains(x.FamilyMemberId)).OrderByDescending(x => x.Batch.CreatedAtUtc).ThenByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        return members.Where(x => lines.Any(l => l.FamilyMemberId == x.Id))
            .ToDictionary(x => x.Id, x => lines.FirstOrDefault(l => l.FamilyMemberId == x.Id && l.SubscriptionId ==
                    (currentSubscriptionIds is not null && currentSubscriptionIds.TryGetValue(x.Id, out var currentId) ? currentId : x.MemberSubscriptionId))
                ?? lines.First(l => l.FamilyMemberId == x.Id));
    }

    private static FamilyMemberLifecycleDto Lifecycle(FamilyMember member, FamilyMemberAccess? access,
        FamilyCheckoutBatchLine? line, Subscription? projectedSubscription = null, bool projectionResolved = false)
    {
        var subscription = projectionResolved ? projectedSubscription : member.MemberSubscription;
        var selected = line?.CheckoutSelected == true;
        var payment = line?.Batch.Status.ToString();
        var fulfillment = subscription is null ? "NotStarted" : subscription.FulfillmentCompletedAtUtc.HasValue ? "Completed" : "Pending";
        var account = access?.RemovedAtUtc.HasValue == true ? "Removed" : access?.IsPaused == true ? "Paused"
            : access?.HasSeparateAccount == true ? "Enabled" : "Managed";
        var display = subscription is null
            ? payment == nameof(FamilyCheckoutBatchStatus.PaymentPending) && selected ? "PaymentPending"
                : selected ? member.SetupStatus == FamilyMemberSetupStatus.Ready ? "ReadyToPay" : "SetupIncomplete" : "Unsubscribed"
            : subscription.Status is SubscriptionStatus.PendingPayment or SubscriptionStatus.Draft ? "PaymentPending"
                : subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled
                    && !subscription.FulfillmentCompletedAtUtc.HasValue ? "PaidPreparingCalendar" : subscription.Status.ToString();
        if (payment == nameof(FamilyCheckoutBatchStatus.ReviewRequired)) display = "ReviewRequired";
        return new(member.SetupStatus, selected, payment, fulfillment, subscription?.Status.ToString(), account, display);
    }

    private static bool CanRenew(SubscriptionStatus status)
        => status is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment
            or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled);

    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
