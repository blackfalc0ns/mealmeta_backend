using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class AdminFamilyGroupHandlers :
    IQueryHandler<GetAdminFamilyGroupsListQuery, PagedResult<AdminFamilyGroupRowDto>>,
    IQueryHandler<GetAdminFamilyGroupByIdQuery, AdminFamilyGroupRowDto>,
    IQueryHandler<GetAdminFamilyGroupStatsQuery, AdminFamilyGroupStatsDto>
{
    private const string MixedProgramAr = "متعدد";
    private const string MixedProgramEn = "Mixed";

    private static readonly SubscriptionDayStatus[] ActiveOrderStatuses =
    [
        SubscriptionDayStatus.Locked72h,
        SubscriptionDayStatus.SentToRestaurant,
        SubscriptionDayStatus.ConfirmedByRestaurant,
        SubscriptionDayStatus.ReplacementRequired,
        SubscriptionDayStatus.ReplacementWindowOpen,
        SubscriptionDayStatus.Preparing,
        SubscriptionDayStatus.OutForDelivery,
    ];

    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly IDateTimeProvider _clock;

    public AdminFamilyGroupHandlers(IAppDbContext db, IUserAccountService users, IDateTimeProvider clock)
    {
        _db = db;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<PagedResult<AdminFamilyGroupRowDto>>> Handle(
        GetAdminFamilyGroupsListQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.FamilyGroups.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(x => x.Name.Contains(q));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var groups = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .Include(x => x.Members)
            .ToListAsync(cancellationToken);

        var rows = await MapRowsAsync(groups, cancellationToken);
        return new PagedResult<AdminFamilyGroupRowDto>(
            rows,
            totalCount,
            pageReq.NormalizedPage,
            pageReq.NormalizedPageSize);
    }

    public async Task<Result<AdminFamilyGroupRowDto>> Handle(
        GetAdminFamilyGroupByIdQuery request,
        CancellationToken cancellationToken)
    {
        var group = await _db.FamilyGroups.AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (group is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Family group '{request.Id}' was not found.");
        }

        var rows = await MapRowsAsync([group], cancellationToken);
        return rows[0];
    }

    public async Task<Result<AdminFamilyGroupStatsDto>> Handle(
        GetAdminFamilyGroupStatsQuery _,
        CancellationToken cancellationToken)
    {
        var statusCounts = await _db.FamilyGroups.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(FamilyGroupStatus status)
            => statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var draft = Count(FamilyGroupStatus.Draft);
        var checkoutPending = Count(FamilyGroupStatus.CheckoutPending);
        var active = Count(FamilyGroupStatus.Active);
        var frozen = Count(FamilyGroupStatus.Frozen);
        var dispute = Count(FamilyGroupStatus.Dispute);
        var pendingDetach = Count(FamilyGroupStatus.PendingDetach);
        var cancelled = Count(FamilyGroupStatus.Cancelled);

        var familyMembers = await _db.FamilyMembers.AsNoTracking().CountAsync(cancellationToken);

        return new AdminFamilyGroupStatsDto(
            FamilyGroups: active,
            FamilyMembers: familyMembers,
            FamilyDisputes: dispute,
            FamilyPendingDetach: pendingDetach,
            Draft: draft,
            CheckoutPending: checkoutPending,
            Active: active,
            Frozen: frozen,
            Dispute: dispute,
            PendingDetach: pendingDetach,
            Cancelled: cancelled);
    }

    private async Task<List<AdminFamilyGroupRowDto>> MapRowsAsync(
        List<FamilyGroup> groups,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return [];
        }

        var managerIds = groups.Select(g => g.ManagerCustomerUserId).Distinct().ToList();
        var identities = await _users.GetUserIdentitiesAsync(managerIds, cancellationToken);
        var managerNameById = identities
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);

        var programIds = groups.SelectMany(g => g.Members)
            .Where(m => m.ProgramId.HasValue)
            .Select(m => m.ProgramId!.Value)
            .Distinct()
            .ToList();
        var bundleIds = groups.SelectMany(g => g.Members)
            .Where(m => m.BundleId.HasValue)
            .Select(m => m.BundleId!.Value)
            .Distinct()
            .ToList();

        var programs = programIds.Count == 0
            ? new Dictionary<Guid, (string NameAr, string NameEn)>()
            : await _db.Programs.AsNoTracking()
                .Where(p => programIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.NameAr, p.NameEn), cancellationToken);

        var bundles = bundleIds.Count == 0
            ? new Dictionary<Guid, (string NameAr, string NameEn)>()
            : await _db.Bundles.AsNoTracking()
                .Where(b => bundleIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => (b.NameAr, b.NameEn), cancellationToken);

        var subscriptionIds = groups.SelectMany(g => g.Members)
            .Where(m => m.MemberSubscriptionId.HasValue)
            .Select(m => m.MemberSubscriptionId!.Value)
            .Distinct()
            .ToList();

        var deliveredById = new Dictionary<Guid, int>();
        var activeOrdersById = new Dictionary<Guid, int>();
        var subscriptionMetaById = new Dictionary<Guid, (DateOnly StartDate, int DurationDays, bool IncludeFriday)>();

        if (subscriptionIds.Count > 0)
        {
            var deliveredCounts = await _db.SubscriptionDays.AsNoTracking()
                .Where(d => subscriptionIds.Contains(d.SubscriptionId) && d.Status == SubscriptionDayStatus.Delivered)
                .GroupBy(d => d.SubscriptionId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            deliveredById = deliveredCounts.ToDictionary(x => x.Id, x => x.Count);

            var activeOrderCounts = await _db.SubscriptionDays.AsNoTracking()
                .Where(d => subscriptionIds.Contains(d.SubscriptionId) && ActiveOrderStatuses.Contains(d.Status))
                .GroupBy(d => d.SubscriptionId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            activeOrdersById = activeOrderCounts.ToDictionary(x => x.Id, x => x.Count);

            var subs = await _db.Subscriptions.AsNoTracking()
                .Where(s => subscriptionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.StartDate, s.DurationDays, s.IncludeFriday })
                .ToListAsync(cancellationToken);
            subscriptionMetaById = subs.ToDictionary(
                s => s.Id,
                s => (s.StartDate, s.DurationDays, s.IncludeFriday));
        }

        var tierLabels = await LoadTierLabelsAsync(cancellationToken);
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var schedule = new SubscriptionScheduleService(_clock);

        return groups.Select(group =>
        {
            managerNameById.TryGetValue(group.ManagerCustomerUserId, out var managerName);
            var members = group.Members
                .OrderBy(m => m.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(m => m.CreatedAtUtc)
                .Select(m => MapMember(
                    m,
                    programs,
                    bundles,
                    deliveredById,
                    activeOrdersById,
                    subscriptionMetaById,
                    schedule,
                    today))
                .ToList();

            return MapGroup(group, managerName ?? string.Empty, members, tierLabels);
        }).ToList();
    }

    private static AdminFamilyMemberRowDto MapMember(
        FamilyMember member,
        IReadOnlyDictionary<Guid, (string NameAr, string NameEn)> programs,
        IReadOnlyDictionary<Guid, (string NameAr, string NameEn)> bundles,
        IReadOnlyDictionary<Guid, int> deliveredById,
        IReadOnlyDictionary<Guid, int> activeOrdersById,
        IReadOnlyDictionary<Guid, (DateOnly StartDate, int DurationDays, bool IncludeFriday)> subscriptionMetaById,
        SubscriptionScheduleService schedule,
        DateOnly today)
    {
        string? programAr = null;
        string? programEn = null;
        if (member.ProgramId is Guid programId && programs.TryGetValue(programId, out var program))
        {
            programAr = program.NameAr;
            programEn = program.NameEn;
        }

        string? bundleAr = null;
        string? bundleEn = null;
        if (member.BundleId is Guid bundleId && bundles.TryGetValue(bundleId, out var bundle))
        {
            bundleAr = bundle.NameAr;
            bundleEn = bundle.NameEn;
        }

        var usedDays = 0;
        var activeOrders = 0;
        if (member.MemberSubscriptionId is Guid subId)
        {
            if (deliveredById.TryGetValue(subId, out var delivered))
            {
                usedDays = delivered;
            }
            else if (subscriptionMetaById.TryGetValue(subId, out var meta))
            {
                var serviceDates = schedule.GetServiceDates(meta.StartDate, meta.DurationDays, meta.IncludeFriday);
                usedDays = serviceDates.Count(d => d < today);
            }

            activeOrdersById.TryGetValue(subId, out activeOrders);
        }

        return new AdminFamilyMemberRowDto(
            member.Id,
            member.MemberSubscriptionId,
            member.DisplayName,
            member.Role,
            member.SetupStatus,
            programAr,
            programEn,
            bundleAr,
            bundleEn,
            member.DurationDays,
            usedDays,
            activeOrders);
    }

    private static AdminFamilyGroupRowDto MapGroup(
        FamilyGroup group,
        string managerName,
        IReadOnlyList<AdminFamilyMemberRowDto> members,
        TierLabelsSettings tierLabels)
    {
        var (programAr, programEn) = ResolveSharedOrMixed(
            members.Select(m => (m.ProgramNameAr, m.ProgramNameEn)));
        var (bundleAr, bundleEn) = ResolveSharedOrMixed(
            members.Select(m => (m.BundleNameAr, m.BundleNameEn)));

        var tiers = members
            .Select(m => group.Members.First(x => x.Id == m.MemberId).SelectedTier)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .Distinct()
            .ToList();

        string tierAr;
        string tierEn;
        if (tiers.Count == 0)
        {
            tierAr = string.Empty;
            tierEn = string.Empty;
        }
        else if (tiers.Count == 1)
        {
            (tierAr, tierEn) = TierLabel(tiers[0], tierLabels);
        }
        else
        {
            tierAr = MixedProgramAr;
            tierEn = MixedProgramEn;
        }

        var subscriptionDays = members.Count == 0
            ? 0
            : members.Max(m => m.DurationDays ?? 0);
        var usedDays = members.Count == 0 ? 0 : members.Max(m => m.UsedDays);
        var activeOrders = members.Sum(m => m.ActiveOrders);

        return new AdminFamilyGroupRowDto(
            group.Id,
            group.Name,
            group.ManagerCustomerUserId,
            managerName,
            group.PaymentAnchorSubscriptionId,
            programAr,
            programEn,
            bundleAr,
            bundleEn,
            tierAr,
            tierEn,
            members.Count,
            subscriptionDays,
            usedDays,
            activeOrders,
            group.Status,
            members);
    }

    private static (string Ar, string En) ResolveSharedOrMixed(
        IEnumerable<(string? Ar, string? En)> values)
    {
        var named = values
            .Where(v => !string.IsNullOrWhiteSpace(v.Ar) || !string.IsNullOrWhiteSpace(v.En))
            .Select(v => (Ar: v.Ar ?? string.Empty, En: v.En ?? string.Empty))
            .ToList();

        if (named.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var distinctAr = named.Select(v => v.Ar).Distinct(StringComparer.Ordinal).ToList();
        var distinctEn = named.Select(v => v.En).Distinct(StringComparer.Ordinal).ToList();
        if (distinctAr.Count == 1 && distinctEn.Count == 1)
        {
            return (distinctAr[0], distinctEn[0]);
        }

        return (MixedProgramAr, MixedProgramEn);
    }

    private static (string Ar, string En) TierLabel(RestaurantTier tier, TierLabelsSettings labels)
        => tier switch
        {
            RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn),
            RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn),
            RestaurantTier.Elite => (labels.EliteAr, labels.EliteEn),
            _ => (tier.ToString(), tier.ToString()),
        };

    private async Task<TierLabelsSettings> LoadTierLabelsAsync(CancellationToken cancellationToken)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive,
                cancellationToken);
        return SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson).TierLabels;
    }
}
