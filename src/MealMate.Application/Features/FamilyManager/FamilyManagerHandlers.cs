using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Helpers;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
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
    ICommandHandler<DetachFamilyMemberCommand, FamilyMemberAccessDto>,
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
    private readonly IAuthSessionService? _sessions;
    private readonly IAuthLoginChallengeService? _loginChallenges;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly IFamilyActionPolicy _familyPolicy;

    public FamilyManagerHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IFamilyPricingSettingsReader pricing,
        IUserAccountService users, ILocalizedMessageProvider? messages = null, IFamilyCancellationSettlement? settlements = null,
        IAuthSessionService? sessions = null, IAuthLoginChallengeService? loginChallenges = null,
        IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null,
        IFamilyActionPolicy? familyPolicy = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _pricing = pricing;
        _users = users;
        _messages = messages ?? NullLocalizedMessageProvider.Instance;
        _settlements = settlements;
        _sessions = sessions;
        _loginChallenges = loginChallenges;
        _transactions = transactions;
        _locks = locks;
        _familyPolicy = familyPolicy ?? new FamilyActionPolicy(db);
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
                projection.UpcomingId, projection.UpcomingStartDate, m.SetupStatus,
                access?.HasSeparateAccount == true, access?.RequiresPasswordSetup == true,
                access?.DetachedFromFamilyManagement == true);
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
                memberAccess?.HasSeparateAccount == true, planSummary, todaySelection,
                memberAccess?.DetachedFromFamilyManagement == true,
                memberAccess?.RequiresPasswordSetup == true);
        }).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IReadOnlyList<FamilySwitcherItemDto>>> Handle(GetFamilySwitcherQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid actorId)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);
        var groupResult = await RequireActiveManagerGroupAsync(cancellationToken);
        var managedGroups = await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.ManagerCustomerUserId == actorId && !x.IsDeleted && x.Status != FamilyGroupStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var managedGroupIds = managedGroups.Select(x => x.Id).ToList();
        var ownAccesses = await _db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.FamilyMember).ThenInclude(x => x.FamilyGroup)
                .Where(x => x.LinkedCustomerUserId == actorId && x.RemovedAtUtc == null
                    && !x.IsDeleted && !x.FamilyMember.IsDeleted
                    && ((!x.FamilyMember.FamilyGroup.IsDeleted
                            && x.FamilyMember.FamilyGroup.Status != FamilyGroupStatus.Cancelled)
                        || x.DetachedFromFamilyManagement))
                .OrderBy(x => x.FamilyMember.FamilyGroupId).ThenBy(x => x.FamilyMemberId)
                .ToListAsync(cancellationToken);
        if (ownAccesses.Count == 0 && managedGroupIds.Count == 0)
            return groupResult.IsFailure ? groupResult.Error : Error.Forbidden("family.not_member");
        var memberGroupIds = ownAccesses.Select(x => x.FamilyMember.FamilyGroupId).ToHashSet();
        memberGroupIds.UnionWith(managedGroupIds);
        var today = BusinessToday;
        var members = await _db.FamilyMembers.IgnoreQueryFilters()
            .Where(x => memberGroupIds.Contains(x.FamilyGroupId) && !x.IsDeleted
                && !_db.FamilyMemberAccesses.IgnoreQueryFilters().Any(a => a.FamilyMemberId == x.Id
                    && a.RemovedAtUtc != null && !a.DetachedFromFamilyManagement))
            .Include(x => x.MemberSubscription).ThenInclude(s => s!.Program)
            .OrderBy(x => x.FamilyGroupId).ThenBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .ThenBy(x => x.DisplayName).ToListAsync(cancellationToken);
        {
            var visibleIds = await _db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.LinkedCustomerUserId == actorId && memberGroupIds.Contains(x.FamilyMember.FamilyGroupId)
                    && x.RemovedAtUtc == null && !x.IsDeleted && !x.FamilyMember.IsDeleted)
                .Select(x => x.FamilyMemberId).ToListAsync(cancellationToken);
            var managedMemberIds = await _db.FamilyMembers.IgnoreQueryFilters().AsNoTracking()
                .Where(x => managedGroupIds.Contains(x.FamilyGroupId) && !x.IsDeleted)
                .Select(x => x.Id).ToListAsync(cancellationToken);
            visibleIds.AddRange(managedMemberIds);
            var visibleSet = visibleIds.ToHashSet();
            foreach (var sibling in members.Where(m => !visibleSet.Contains(m.Id) && m.Role != FamilyMemberRole.Manager))
            {
                var calendar = await _familyPolicy.EvaluateAsync(actorId, sibling.Id, FamilyAction.ViewCalendar, cancellationToken);
                var plan = await _familyPolicy.EvaluateAsync(actorId, sibling.Id, FamilyAction.ViewPlan, cancellationToken);
                var invoices = await _familyPolicy.EvaluateAsync(actorId, sibling.Id, FamilyAction.ViewInvoices, cancellationToken);
                if (calendar.Allowed || plan.Allowed || invoices.Allowed) visibleSet.Add(sibling.Id);
            }
            members = members.Where(m => visibleSet.Contains(m.Id)).ToList();
        }
        var serviceSubscriptions = await ResolveServiceSubscriptionsAsync(members, today, cancellationToken);
        var access = await _db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
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

        var switcherItems = new List<FamilySwitcherItemDto>(members.Count);
        foreach (var m in members)
        {
            var isTargetManager = m.Role == FamilyMemberRole.Manager;
            access.TryGetValue(m.Id, out var memberAccess);
            programs.TryGetValue(m.ProgramId ?? Guid.Empty, out var program);
            var projection = serviceSubscriptions.GetValueOrDefault(m.Id, new FamilyServiceSubscriptionProjection(null, null, null, null));
            var service = projection.Current ?? projection.Upcoming;
            var programAr = service?.Program?.NameAr ?? program?.NameAr;
            var programEn = service?.Program?.NameEn ?? program?.NameEn;
            var ownerId = isTargetManager ? managedGroups.FirstOrDefault(x => x.Id == m.FamilyGroupId)?.ManagerCustomerUserId : memberAccess?.LinkedCustomerUserId;
            var imageUrl = ownerId.HasValue && profileImages.TryGetValue(ownerId.Value, out var imageKey)
                ? MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(imageKey) : null;
            var resolvedSubId = projection.CurrentId ?? projection.UpcomingId ?? m.MemberSubscriptionId;
            var isTargetGroupManager = managedGroupIds.Contains(m.FamilyGroupId);
            var ownRow = isTargetGroupManager || memberAccess?.LinkedCustomerUserId == actorId;
            var canViewCalendar = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.ViewCalendar, cancellationToken)).Allowed;
            var canViewProgram = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.ViewPlan, cancellationToken)).Allowed;
            var canViewInvoices = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.ViewInvoices, cancellationToken)).Allowed;
            var canEditDailyMeals = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.EditDailyMeals, cancellationToken)).Allowed;
            var canFillMealsAndDays = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.EditMealsAndDays, cancellationToken)).Allowed;
            var canExchangeMeals = (await _familyPolicy.EvaluateAsync(
                actorId, m.Id, FamilyAction.UnitExchange, cancellationToken)).Allowed;
            var canViewSiblingPlan = isTargetGroupManager || canViewProgram;
            var canViewSiblingCalendar = isTargetGroupManager || canViewCalendar;
            var canViewSiblingInvoices = isTargetGroupManager || canViewInvoices;
            var canViewAnySiblingData = canViewSiblingPlan || canViewSiblingCalendar || canViewSiblingInvoices;

            switcherItems.Add(new FamilySwitcherItemDto(
                m.Id,
                m.DisplayName,
                m.Role,
                isTargetManager,
                canViewAnySiblingData ? resolvedSubId : null,
                canViewAnySiblingData ? projection.UpcomingId : null,
                canViewAnySiblingData ? projection.UpcomingStartDate : null,
                canViewSiblingPlan ? programAr : null,
                canViewSiblingPlan ? programEn : null,
                ownRow ? imageUrl : null,
                ownRow && memberAccess?.IsPaused == true,
                ownRow && (canEditDailyMeals || canFillMealsAndDays || canExchangeMeals),
                ownRow ? m.SetupStatus : null,
                canViewSiblingCalendar,
                canViewSiblingPlan,
                canViewSiblingInvoices,
                ownRow && memberAccess?.HasSeparateAccount == true,
                ownRow && memberAccess?.RequiresPasswordSetup == true,
                ownRow && memberAccess?.DetachedFromFamilyManagement == true,
                m.FamilyGroupId));
        }
        IReadOnlyList<FamilySwitcherItemDto> list = switcherItems;

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
        if (access?.LinkedCustomerUserId is Guid linkedUserId)
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
        if (access.LinkedCustomerUserId is Guid linkedUserId)
        {
            var user = await _users.FindByIdAsync(linkedUserId, cancellationToken);
            linkedUserPhone = user?.PhoneNumber;
        }

        return Result.Success(MapAccess(access, loaded.Value.Member, existingLogin, linkedUserPhone: linkedUserPhone));
    }

    public Task<Result<FamilyMemberAccessDto>> Handle(UpdateFamilyMemberAccessCommand request, CancellationToken cancellationToken)
        => _transactions is null
            ? HandleUpdateFamilyMemberAccessCore(request, cancellationToken)
            : _transactions.ExecuteResultAsync(async ct =>
            {
                if (_locks is not null)
                {
                    var groupId = await _db.FamilyMembers.AsNoTracking()
                        .Where(member => member.Id == request.MemberId)
                        .Select(member => member.FamilyGroupId)
                        .FirstOrDefaultAsync(ct);
                    if (groupId != Guid.Empty) await _locks.LockFamilyGroupAsync(groupId, ct);
                }
                return await HandleUpdateFamilyMemberAccessCore(request, ct);
            }, cancellationToken);

    private async Task<Result<FamilyMemberAccessDto>> HandleUpdateFamilyMemberAccessCore(
        UpdateFamilyMemberAccessCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, existingAccess) = loaded.Value;
        if (existingAccess?.DetachedFromFamilyManagement == true)
            return Error.Conflict("family.member_detached");
        if (member.Role == FamilyMemberRole.Manager
            && (request.Request.HasSeparateAccount == true || request.Request.GenerateTemporaryPassword))
            return Error.Conflict("family.manager_account_immutable");
        string? credentialOperationKeyHash = null;
        if (request.Request.GenerateTemporaryPassword || request.Request.HasSeparateAccount == true)
        {
            var operationKey = request.Request.CredentialOperationKey?.Trim();
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length is < 16 or > 200)
                return Error.Validation("family.credential_operation_key_required");
            credentialOperationKeyHash = HashToken(operationKey);
            var priorOperation = await _db.FamilyCredentialOperations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id
                    && x.OperationKeyHash == credentialOperationKeyHash, cancellationToken);
            if (priorOperation is not null)
            {
                var priorAccess = existingAccess ?? await EnsureAccessAsync(member, cancellationToken);
                var priorPhone = priorAccess.LinkedCustomerUserId is Guid priorUserId
                    ? (await _users.FindByIdAsync(priorUserId, cancellationToken))?.PhoneNumber : null;
                return Result.Success(MapAccess(priorAccess, member,
                    await HasExistingLoginAsync(priorAccess, member, cancellationToken),
                    linkedUserPhone: priorPhone, passwordAlreadyIssued: true, requiresExplicitReset: true));
            }
        }
        if (request.Request.Remove == true)
        {
            if (member.Role == FamilyMemberRole.Manager) return Error.Conflict(MessageCodes.Family.CannotRemoveManager);
            if (_locks is not null && existingAccess?.LinkedCustomerUserId is Guid removalIdentity)
                await _locks.LockCustomerIdentityAsync(removalIdentity, cancellationToken);
            if (_settlements is null) return Error.Failure("family.settlement_dependency_missing");
            var removal = await _settlements.RemoveAsync(member.Id, _currentUser.UserId!.Value, cancellationToken);
            if (removal.IsFailure) return removal.Error;
            var updated = await _db.FamilyMemberAccesses.AsNoTracking().FirstAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
            if (updated.LinkedCustomerUserId is Guid removedUserId)
            {
                if (_sessions is not null) await _sessions.RevokeAllUserTokensAsync(removedUserId, cancellationToken);
                if (_loginChallenges is not null) await _loginChallenges.InvalidateAllForUserAsync(removedUserId, cancellationToken);
            }
            return MapAccess(updated, member, await HasExistingLoginAsync(updated, member, cancellationToken));
        }
        var access = await EnsureAccessAsync(member, cancellationToken);
        if (_locks is not null)
        {
            var identityId = access.LinkedCustomerUserId;
            if (identityId is null && !string.IsNullOrWhiteSpace(member.PhoneNumber))
                identityId = (await _users.FindByPhoneAsync(member.PhoneNumber, cancellationToken))?.Id;
            if (identityId is Guid userId)
            {
                await _locks.LockCustomerIdentityAsync(userId, cancellationToken);
                if (_db is DbContext context)
                    await context.Entry(access).ReloadAsync(cancellationToken);
            }
        }
        var hadSeparateAccount = access.HasSeparateAccount;

        if (request.Request.IsPaused is { } paused)
        {
            access.IsPaused = paused;
            access.PausedAtUtc = paused ? _clock.UtcNow : null;
        }

        string? generatedPassword = null;
        var existingLogin = false;

        if (request.Request.GenerateTemporaryPassword && request.Request.HasSeparateAccount == false)
            return Error.Validation("family.temporary_password_requires_separate_account");

        if (request.Request.HasSeparateAccount == true || request.Request.GenerateTemporaryPassword)
        {
            var provisioned = await ProvisionSeparateAccountAsync(member, access, hadSeparateAccount,
                request.Request.GenerateTemporaryPassword, cancellationToken);
            if (provisioned.IsFailure) return provisioned.Error;
            access.HasSeparateAccount = true;
            await EnsurePermissionsAsync(member, cancellationToken);
            generatedPassword = provisioned.Value.GeneratedPassword;
            existingLogin = provisioned.Value.HasExistingLogin;
            access.InviteTokenHash = null;
            access.InviteExpiresAtUtc = null;
            if (credentialOperationKeyHash is not null && generatedPassword is not null)
            {
                _db.FamilyCredentialOperations.Add(new FamilyCredentialOperation
                {
                    FamilyMemberId = member.Id,
                    ActorUserId = _currentUser.UserId!.Value,
                    OperationKeyHash = credentialOperationKeyHash,
                    CompletedAtUtc = _clock.UtcNow
                });
            }
        }
        else if (request.Request.HasSeparateAccount == false)
        {
            access.HasSeparateAccount = false;
            access.InviteTokenHash = null;
            access.InviteExpiresAtUtc = null;
            if (access.LinkedCustomerUserId is Guid disabledUserId)
            {
                if (_sessions is not null) await _sessions.RevokeAllUserTokensAsync(disabledUserId, cancellationToken);
                if (_loginChallenges is not null) await _loginChallenges.InvalidateAllForUserAsync(disabledUserId, cancellationToken);
            }
        }

        var linkedUserPhone = (string?)null;
        if (access.LinkedCustomerUserId is Guid linkedUserId)
        {
            var user = await _users.FindByIdAsync(linkedUserId, cancellationToken);
            linkedUserPhone = user?.PhoneNumber;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access, member, existingLogin, generatedPassword: generatedPassword, linkedUserPhone: linkedUserPhone));
    }

    public Task<Result<FamilyMemberAccessDto>> Handle(DetachFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        if ((_transactions is null || _locks is null)
            && _db is DbContext context
            && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            throw new InvalidOperationException("Family detachment requires transactional family-group locks.");

        return _transactions is null
            ? HandleDetachFamilyMemberCore(request, cancellationToken)
            : _transactions.ExecuteResultAsync(async ct =>
            {
                var groupId = await _db.FamilyMembers.AsNoTracking()
                    .Where(member => member.Id == request.MemberId)
                    .Select(member => member.FamilyGroupId)
                    .FirstOrDefaultAsync(ct);
                if (groupId != Guid.Empty) await _locks!.LockFamilyGroupAsync(groupId, ct);
                return await HandleDetachFamilyMemberCore(request, ct);
            }, cancellationToken);
    }

    private async Task<Result<FamilyMemberAccessDto>> HandleDetachFamilyMemberCore(DetachFamilyMemberCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var (member, existingAccess) = loaded.Value;
        if (member.Role == FamilyMemberRole.Manager)
            return Error.Conflict(MessageCodes.Family.CannotRemoveManager);
        if (existingAccess?.DetachedFromFamilyManagement == true)
            return Result.Success(MapAccess(existingAccess, member,
                await HasExistingLoginAsync(existingAccess, member, cancellationToken)));
        if (await _db.FamilyCheckoutBatches.AsNoTracking().AnyAsync(batch => batch.FamilyGroupId == member.FamilyGroupId
                && (batch.Status == FamilyCheckoutBatchStatus.PaymentPending
                    || batch.Status == FamilyCheckoutBatchStatus.ReviewRequired
                    || (batch.Status == FamilyCheckoutBatchStatus.Draft
                        && batch.Lines.Any(line => line.FamilyMemberId == member.Id && line.CheckoutSelected))), cancellationToken))
            return Error.Conflict("family.checkout_pending");
        if (await _db.SubscriptionTierUpgrades.AsNoTracking().AnyAsync(upgrade =>
                upgrade.Status == VerificationStatus.Submitted
                && _db.Subscriptions.Any(subscription => subscription.Id == upgrade.SubscriptionId
                    && subscription.FamilyGroupId == member.FamilyGroupId
                    && subscription.FamilyMemberId == member.Id), cancellationToken)
            || await _db.SubscriptionRenewals.AsNoTracking().AnyAsync(renewal => renewal.FamilyMemberId == member.Id
                && (renewal.Status == "PaymentPending" || renewal.Status == "ReviewRequired"),
                cancellationToken))
            return Error.Conflict("family.checkout_pending");
        if (await (from freeze in _db.SubscriptionFreezeRequests.AsNoTracking()
                   join subscription in _db.Subscriptions.AsNoTracking() on freeze.SubscriptionId equals subscription.Id
                   where subscription.FamilyGroupId == member.FamilyGroupId
                       && subscription.FamilyMemberId == member.Id
                       && freeze.Status == VerificationStatus.Submitted
                   select freeze.Id).AnyAsync(cancellationToken))
            return Error.Conflict("family.freeze_request_pending");
        var access = existingAccess ?? await EnsureAccessAsync(member, cancellationToken);
        if (_locks is not null && access.LinkedCustomerUserId is Guid detachIdentity)
        {
            await _locks.LockCustomerIdentityAsync(detachIdentity, cancellationToken);
            if (_db is DbContext context) await context.Entry(access).ReloadAsync(cancellationToken);
            if (access.DetachedFromFamilyManagement)
                return Result.Success(MapAccess(access, member, await HasExistingLoginAsync(access, member, cancellationToken)));
        }
        if (!access.HasSeparateAccount || access.IsPaused || access.RequiresPasswordSetup
            || access.LinkedCustomerUserId is not Guid linkedUserId)
            return Error.Validation(MessageCodes.Family.DetachRequiresActiveLogin);
        var linkedUser = await _users.FindByIdAsync(linkedUserId, cancellationToken);
        if (linkedUser is null || linkedUser.UserType != UserType.Customer
            || linkedUser.AccountStatus != AccountStatus.Active || !linkedUser.HasSetPassword)
            return Error.Validation(MessageCodes.Family.DetachRequiresActiveLogin);
        var permissions = await _db.FamilyMemberPermissions
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        if (permissions is null)
        {
            permissions = new FamilyMemberPermissions { FamilyMemberId = member.Id };
            _db.FamilyMemberPermissions.Add(permissions);
        }
        permissions.ViewCalendar = true;
        permissions.ViewProgram = true;
        permissions.ViewInvoices = true;
        permissions.SelectRestaurant = true;
        permissions.EditDailyMeals = true;
        permissions.ConfirmMeals = true;
        permissions.EditAddress = true;
        permissions.ChangePassword = true;
        permissions.RequestFreeze = true;
        permissions.RequestRenewal = true;
        permissions.RequestUpgrade = true;
        permissions.CancelSubscription = true;
        permissions.ViewFamilyCalendars = false;
        permissions.ViewFamilyPlans = false;
        permissions.ViewFamilyInvoices = false;
        permissions.EditMealsAndDays = false;
        permissions.ChangeProgram = false;
        permissions.ChangeDeliveryTime = false;
        permissions.UnitExchange = false;
        permissions.Version++;
        access.DetachedFromFamilyManagement = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(MapAccess(access, member,
            await HasExistingLoginAsync(access, member, cancellationToken)));
    }

    public async Task<Result<FamilyMemberPermissionsDto>> Handle(GetFamilyMemberPermissionsQuery request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        var perms = await _db.FamilyMemberPermissions.AsNoTracking().FirstOrDefaultAsync(x => x.FamilyMemberId == loaded.Value.Member.Id, cancellationToken)
            ?? new FamilyMemberPermissions { FamilyMemberId = loaded.Value.Member.Id };
        return Result.Success(MapPermissions(perms));
    }

    public Task<Result<FamilyMemberPermissionsDto>> Handle(UpdateFamilyMemberPermissionsCommand request, CancellationToken cancellationToken)
        => _transactions is null
            ? HandleUpdateFamilyMemberPermissionsCore(request, cancellationToken)
            : _transactions.ExecuteResultAsync(async ct =>
            {
                if (_locks is not null)
                {
                    var groupId = await _db.FamilyMembers.AsNoTracking()
                        .Where(member => member.Id == request.MemberId)
                        .Select(member => member.FamilyGroupId)
                        .FirstOrDefaultAsync(ct);
                    if (groupId != Guid.Empty) await _locks.LockFamilyGroupAsync(groupId, ct);
                }
                return await HandleUpdateFamilyMemberPermissionsCore(request, ct);
            }, cancellationToken);

    private async Task<Result<FamilyMemberPermissionsDto>> HandleUpdateFamilyMemberPermissionsCore(UpdateFamilyMemberPermissionsCommand request, CancellationToken cancellationToken)
    {
        var loaded = await LoadManagedMemberAsync(request.MemberId, cancellationToken);
        if (loaded.IsFailure) return loaded.Error;
        if (loaded.Value.Access?.DetachedFromFamilyManagement == true)
            return Error.Conflict("family.member_detached");
        if (loaded.Value.Member.Role == FamilyMemberRole.Manager)
            return Error.Conflict(MessageCodes.Family.ManagerPermissionsImmutable);

        if (request.Request.ExpectedVersion is null)
            return Error.Validation("family.permissions_version_required");
        var perms = await EnsurePermissionsAsync(loaded.Value.Member, cancellationToken);
        if (perms.Version != request.Request.ExpectedVersion)
            return Error.Conflict("family.permissions_changed");
        if (request.Request.ChangeDeliveryTime == true || request.Request.ChangeProgram == true)
            return Error.Validation("family.permission_not_supported");
        if (request.Request.ChangeDeliveryTime is false) perms.ChangeDeliveryTime = false;
        if (request.Request.ChangeProgram is false) perms.ChangeProgram = false;
        if (request.Request.EditMealsAndDays is { } editMealsAndDays) perms.EditMealsAndDays = editMealsAndDays;
        if (request.Request.UnitExchange is { } unitExchange) perms.UnitExchange = unitExchange;
        if (request.Request.EditDailyMeals is { } editDailyMeals) perms.EditDailyMeals = editDailyMeals;
        // Own-plan visibility is a baseline member capability, not a manager grant.
        perms.ViewProgram = true;
        if (request.Request.EditAddress is { } editAddress) perms.EditAddress = editAddress;
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
        // A granted service action must remain discoverable through its read surface.
        if (perms.SelectRestaurant || perms.EditDailyMeals || perms.EditMealsAndDays || perms.UnitExchange
            || perms.ConfirmMeals || perms.EditAddress)
            perms.ViewCalendar = true;
        if (perms.ViewCalendar || perms.SelectRestaurant || perms.EditDailyMeals || perms.EditMealsAndDays || perms.UnitExchange
            || perms.ConfirmMeals || perms.EditAddress || perms.RequestFreeze || perms.RequestRenewal
            || perms.RequestUpgrade || perms.CancelSubscription)
            perms.ViewProgram = true;
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

    public Task<Result<FamilyMemberAccessDto>> Handle(AcceptFamilyInviteCommand request, CancellationToken cancellationToken)
        => _transactions is null
            ? AcceptFamilyInviteCoreAsync(request, cancellationToken)
            : _transactions.ExecuteResultAsync(async ct =>
            {
                var hash = HashToken(request.Token.Trim());
                var groupId = await _db.FamilyMemberAccesses.AsNoTracking()
                    .Where(x => x.InviteTokenHash == hash)
                    .Select(x => (Guid?)x.FamilyMember.FamilyGroupId).FirstOrDefaultAsync(ct);
                if (_locks is not null)
                {
                    if (groupId is Guid id) await _locks.LockFamilyGroupAsync(id, ct);
                    if (_currentUser.UserId is Guid actorId) await _locks.LockCustomerIdentityAsync(actorId, ct);
                }
                return await AcceptFamilyInviteCoreAsync(request, ct);
            }, cancellationToken);

    private async Task<Result<FamilyMemberAccessDto>> AcceptFamilyInviteCoreAsync(AcceptFamilyInviteCommand request, CancellationToken cancellationToken)
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
            .AnyAsync(x => x.LinkedCustomerUserId == _currentUser.UserId && x.Id != access.Id
                && x.RemovedAtUtc == null && !x.DetachedFromFamilyManagement, cancellationToken);
        var managesAnotherFamily = await _db.FamilyGroups.AsNoTracking().AnyAsync(x =>
            x.ManagerCustomerUserId == _currentUser.UserId && x.Id != access.FamilyMember.FamilyGroupId
            && x.Status != FamilyGroupStatus.Cancelled, cancellationToken);
        if (alreadyLinked || managesAnotherFamily)
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
        perms = new FamilyMemberPermissions
        {
            FamilyMemberId = member.Id,
            ViewCalendar = true,
            ViewProgram = true
        };
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
        bool hadSeparateAccount,
        bool forcePasswordReset,
        CancellationToken ct)
    {
        var account = access.LinkedCustomerUserId is { } linkedId
            ? await _users.FindByIdAsync(linkedId, ct)
            : null;
        if (account is null && string.IsNullOrWhiteSpace(member.PhoneNumber))
            return Error.Conflict("family.member_phone_required");
        if (account is null && !string.IsNullOrWhiteSpace(member.PhoneNumber))
            account = await _users.FindByPhoneAsync(member.PhoneNumber, ct);

        if (account is not null && await _db.FamilyGroups.AsNoTracking()
            .AnyAsync(group => group.ManagerCustomerUserId == account.Id, ct))
            return Error.Conflict("family.manager_account_immutable");

        if (account is null)
        {
            var newAccountPassword = GeneratePassword();
            var create = await _users.CreateAsync(new CreateUserRequest(
                Email: null,
                PhoneNumber: member.PhoneNumber,
                Password: newAccountPassword,
                FullName: string.IsNullOrWhiteSpace(member.DisplayName) ? null : member.DisplayName,
                UserType: UserType.Customer,
                AccountStatus: AccountStatus.Active), ct);
            if (create.IsFailure) return create.Error;
            account = create.Value;
            if (_locks is not null)
                await _locks.LockCustomerIdentityAsync(account.Id, ct);
            var role = await _users.AddToRoleAsync(account.Id, AppRoles.Customer, ct);
            if (role.IsFailure) return role.Error;
            await _users.SetHasPasswordAsync(account.Id, false, ct);
            if (!await _db.CustomerProfiles.AnyAsync(x => x.UserId == account.Id, ct))
                _db.CustomerProfiles.Add(new CustomerProfile { UserId = account.Id, PreferredLanguage = "ar" });
            access.LinkedCustomerUserId = account.Id;
            access.RequiresPasswordSetup = true;
            return Result.Success<(bool, string?)>((false, newAccountPassword));
        }

        if (account.UserType != UserType.Customer)
            return Error.Conflict("family.member_account_type_invalid");
        if (account.AccountStatus != AccountStatus.Active)
            return Error.Forbidden(account.AccountStatus switch
            {
                AccountStatus.Suspended => MessageCodes.Auth.AccountSuspended,
                AccountStatus.Closed => MessageCodes.Auth.AccountClosed,
                _ => MessageCodes.Auth.InvalidState
            });

        var duplicateLink = await _db.FamilyMemberAccesses.AsNoTracking().AnyAsync(x => x.LinkedCustomerUserId == account.Id
            && x.FamilyMemberId != member.Id && x.RemovedAtUtc == null && !x.DetachedFromFamilyManagement, ct);
        if (duplicateLink)
            return Error.Conflict("family.account_already_linked");

        access.LinkedCustomerUserId = account.Id;
        var shouldGeneratePassword = forcePasswordReset
            || (!hadSeparateAccount && account.LastLoginAtUtc is null && !account.HasSetPassword);
        if (!shouldGeneratePassword)
            return Result.Success<(bool, string?)>((account.LastLoginAtUtc is not null, null));

        var temporaryPassword = GeneratePassword();
        var token = await _users.GeneratePasswordResetTokenAsync(account.Id, ct);
        var reset = await _users.ResetPasswordAsync(account.Id, token, temporaryPassword, ct);
        if (reset.IsFailure) return reset.Error;
        await _users.SetHasPasswordAsync(account.Id, false, ct);
        access.RequiresPasswordSetup = true;
        if (_sessions is not null) await _sessions.RevokeAllUserTokensAsync(account.Id, ct);
        if (_loginChallenges is not null) await _loginChallenges.InvalidateAllForUserAsync(account.Id, ct);
        return Result.Success<(bool, string?)>((false, temporaryPassword));
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
        var canRenew = access?.DetachedFromFamilyManagement != true
            && serviceSubscriptionId is not null && subscription is not null && CanRenew(subscription.Status);
        var renewalPath = subscription is not null && canRenew
            ? $"/api/v1/customer/subscriptions/{subscription.Id}/renewal-requests"
            : null;

        var rawPhone = !string.IsNullOrWhiteSpace(linkedUserPhone) ? linkedUserPhone : m.PhoneNumber;
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
            countryCode, nationalNumber, lifecycle, upcomingSubscriptionId, upcomingStartDate,
            access?.DetachedFromFamilyManagement == true,
            access?.RequiresPasswordSetup == true);
    }

    private static FamilyMemberAccessDto MapAccess(
        FamilyMemberAccess access,
        FamilyMember? member,
        bool hasExistingLogin,
        string? inviteRawToken = null,
        string? generatedPassword = null,
        string? linkedUserPhone = null,
        bool passwordAlreadyIssued = false,
        bool requiresExplicitReset = false)
    {
        var rawPhone = !string.IsNullOrWhiteSpace(linkedUserPhone) ? linkedUserPhone : member?.PhoneNumber;
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
            nationalNumber,
            access.RequiresPasswordSetup,
            access.DetachedFromFamilyManagement,
            passwordAlreadyIssued,
            requiresExplicitReset);
    }

    private FamilyMemberPermissionsDto MapPermissions(FamilyMemberPermissions p)
        => new(
            p.FamilyMemberId, p.EditDailyMeals, p.ChangeDeliveryTime, true, p.ChangeProgram,
            p.EditMealsAndDays, p.EditAddress, p.UnitExchange, true,
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
        var account = access?.DetachedFromFamilyManagement == true ? "Detached"
            : access?.RemovedAtUtc.HasValue == true ? "Removed"
            : access?.RequiresPasswordSetup == true ? "SetupRequired"
            : access?.IsPaused == true ? "Paused"
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
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string alphabet = letters + digits;
        var chars = new char[12];
        chars[0] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
        chars[1] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        for (var i = 2; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
