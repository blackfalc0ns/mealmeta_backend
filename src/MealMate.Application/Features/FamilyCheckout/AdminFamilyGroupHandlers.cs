using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class AdminFamilyGroupHandlers :
    IQueryHandler<GetAdminFamilyGroupsListQuery, PagedResult<AdminFamilyGroupRowDto>>,
    IQueryHandler<GetAdminFamilyGroupByIdQuery, AdminFamilyGroupRowDto>,
    IQueryHandler<GetAdminFamilyGroupStatsQuery, AdminFamilyGroupStatsDto>,
    IQueryHandler<GetAdminFamilyGroupHistoryQuery, PagedResult<AdminFamilyGroupHistoryRowDto>>,
    IQueryHandler<GetAdminFamilyCancellationHistoryQuery, PagedResult<AdminFamilyCancellationRowDto>>
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
            .Include(x => x.Members)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
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

        var activeGroupIds = await _db.FamilyGroups.AsNoTracking().Where(x => x.Status == FamilyGroupStatus.Active)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var activeGroupMembers = await _db.FamilyMembers.AsNoTracking()
            .Where(m => activeGroupIds.Contains(m.FamilyGroupId) && !m.IsDeleted).ToListAsync(cancellationToken);
        var memberIds = activeGroupMembers.Select(x => x.Id).ToArray();
        var removedIds = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => memberIds.Contains(x.FamilyMemberId) && x.RemovedAtUtc != null)
            .Select(x => x.FamilyMemberId).ToListAsync(cancellationToken);
        var roster = activeGroupMembers.Where(m => !removedIds.Contains(m.Id)).ToList();
        var pointers = roster.ToDictionary(x => x.Id, x => x.MemberSubscriptionId);
        var projections = await new FamilyServiceSubscriptionResolver(_db).ResolveAsync(roster.Select(x => x.Id).ToArray(),
            pointers, BusinessCalendarDate.KuwaitToday(_clock.UtcNow), cancellationToken);
        var familyMembers = roster.Count;
        var activeServiceMembers = projections.Values.Count(x => x.CurrentId.HasValue);

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
            Cancelled: cancelled,
            ActiveServiceMembers: activeServiceMembers);
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

        var memberIds = groups.SelectMany(g => g.Members).Where(m => !m.IsDeleted).Select(m => m.Id).Distinct().ToArray();
        var accessRows = memberIds.Length == 0 ? [] : await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => memberIds.Contains(x.FamilyMemberId)).ToListAsync(cancellationToken);
        var accessByMember = accessRows.ToDictionary(x => x.FamilyMemberId);
        var visibleMembers = groups.SelectMany(g => g.Members).Where(m => !m.IsDeleted
            && (!accessByMember.TryGetValue(m.Id, out var access) || access.RemovedAtUtc is null)).ToList();

        var programIds = visibleMembers
            .Where(m => m.ProgramId.HasValue)
            .Select(m => m.ProgramId!.Value)
            .Distinct()
            .ToList();
        var bundleIds = visibleMembers
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

        var pointers = visibleMembers.ToDictionary(x => x.Id, x => x.MemberSubscriptionId);
        var serviceProjection = await new FamilyServiceSubscriptionResolver(_db).ResolveAsync(
            visibleMembers.Select(x => x.Id).ToArray(), pointers, BusinessCalendarDate.KuwaitToday(_clock.UtcNow), cancellationToken);
        var latestLineIds = memberIds.Length == 0 ? [] : await _db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => memberIds.Contains(x.FamilyMemberId)).GroupBy(x => x.FamilyMemberId)
            .Select(g => g.OrderByDescending(x => x.Batch.CreatedAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id).Select(x => x.Id).First())
            .ToListAsync(cancellationToken);
        var batchLines = latestLineIds.Count == 0 ? [] : await _db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
            .Where(x => latestLineIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var latestLineByMember = batchLines.ToDictionary(x => x.FamilyMemberId);
        var subscriptionIds = visibleMembers
            .Where(m => m.MemberSubscriptionId.HasValue)
            .Select(m => m.MemberSubscriptionId!.Value)
            .Concat(serviceProjection.Values.SelectMany(x => new[] { x.CurrentId, x.UpcomingId })
                .Where(x => x.HasValue).Select(x => x!.Value))
            .Distinct()
            .ToList();

        var deliveredById = new Dictionary<Guid, int>();
        var activeOrdersById = new Dictionary<Guid, int>();
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

        }

        var currentAndUpcomingIds = serviceProjection.Values.SelectMany(x => new[] { x.CurrentId, x.UpcomingId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var serviceSubscriptions = currentAndUpcomingIds.Length == 0 ? [] : await _db.Subscriptions.AsNoTracking()
            .Where(x => currentAndUpcomingIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var serviceDayCounts = currentAndUpcomingIds.Length == 0 ? [] : await _db.SubscriptionDays.AsNoTracking()
            .Where(x => currentAndUpcomingIds.Contains(x.SubscriptionId))
            .GroupBy(x => x.SubscriptionId).Select(x => new { SubscriptionId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.SubscriptionId, x => x.Count, cancellationToken);

        var tierLabels = await LoadTierLabelsAsync(cancellationToken);
        return groups.Select(group =>
        {
            managerNameById.TryGetValue(group.ManagerCustomerUserId, out var managerName);
            var members = group.Members.Where(m => !m.IsDeleted
                    && (!accessByMember.TryGetValue(m.Id, out var access) || access.RemovedAtUtc is null))
                .OrderBy(m => m.Role == FamilyMemberRole.Manager ? 0 : 1)
                .ThenBy(m => m.CreatedAtUtc)
                .Select(m => MapMember(
                    m,
                    group.ManagerCustomerUserId,
                    accessByMember.GetValueOrDefault(m.Id),
                    serviceProjection.GetValueOrDefault(m.Id),
                    serviceSubscriptions,
                    serviceDayCounts,
                    latestLineByMember.GetValueOrDefault(m.Id),
                    programs,
                    bundles,
                    deliveredById,
                    activeOrdersById))
                .ToList();

            var latestBatchId = batchLines.Where(x => x.Batch.FamilyGroupId == group.Id)
                .OrderByDescending(x => x.Batch.CreatedAtUtc).Select(x => (Guid?)x.BatchId).FirstOrDefault();
            return MapGroup(group, latestBatchId, managerName ?? string.Empty, members, tierLabels);
        }).ToList();
    }

    private static AdminFamilyMemberRowDto MapMember(
        FamilyMember member,
        Guid groupManagerId,
        FamilyMemberAccess? access,
        FamilyServiceSubscriptionProjection? service,
        IReadOnlyDictionary<Guid, Subscription> serviceSubscriptions,
        IReadOnlyDictionary<Guid, int> serviceDayCounts,
        FamilyCheckoutBatchLine? latestLine,
        IReadOnlyDictionary<Guid, (string NameAr, string NameEn)> programs,
        IReadOnlyDictionary<Guid, (string NameAr, string NameEn)> bundles,
        IReadOnlyDictionary<Guid, int> deliveredById,
        IReadOnlyDictionary<Guid, int> activeOrdersById)
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
        var currentId = service?.CurrentId;
        var upcomingId = service?.UpcomingId;
        if (currentId is Guid subId)
        {
            deliveredById.TryGetValue(subId, out usedDays);
            activeOrdersById.TryGetValue(subId, out activeOrders);
        }

        var customerUserId = member.Role == FamilyMemberRole.Manager ? groupManagerId : access?.LinkedCustomerUserId;
        var selected = latestLine?.CheckoutSelected == true;
        var paymentStatus = latestLine?.Batch.Status.ToString();
        var fulfillmentStatus = currentId is Guid current && serviceSubscriptions.TryGetValue(current, out var currentSub)
            ? currentSub.Status.ToString()
            : upcomingId is Guid upcoming && serviceSubscriptions.TryGetValue(upcoming, out var upcomingSub)
                ? upcomingSub.Status.ToString() : "NotStarted";
        var serviceStatus = currentId.HasValue ? (serviceDayCounts.GetValueOrDefault(currentId.Value) > 0 ? "CurrentService" : "PreparingCalendar")
            : upcomingId.HasValue ? "UpcomingService" : "NoFundedService";
        var accountStatus = member.Role == FamilyMemberRole.Manager ? "Linked"
            : access?.RemovedAtUtc is not null ? "Removed"
            : access?.IsPaused == true ? "Paused"
            : customerUserId.HasValue ? "Linked" : "Unlinked";
        var readinessBlocker = member.Role != FamilyMemberRole.Manager && selected && customerUserId is null
            ? "family.member_account_required" : null;

        return new AdminFamilyMemberRowDto(
            member.FamilyGroupId,
            member.Id,
            customerUserId,
            member.MemberSubscriptionId,
            currentId,
            upcomingId,
            latestLine?.BatchId,
            member.DisplayName,
            member.Role,
            member.SetupStatus,
            latestLine is null ? "NotSelected" : selected ? "Selected" : "Deferred",
            paymentStatus,
            fulfillmentStatus,
            serviceStatus,
            accountStatus,
            readinessBlocker,
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
        Guid? batchId,
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
            batchId,
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

    public async Task<Result<PagedResult<AdminFamilyGroupHistoryRowDto>>> Handle(
        GetAdminFamilyGroupHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await _db.FamilyGroups.AsNoTracking().AnyAsync(x => x.Id == request.GroupId, cancellationToken))
            return Error.NotFound(MessageCodes.Common.NotFound, "Family group was not found.");

        var query = _db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => x.Batch.FamilyGroupId == request.GroupId);
        var totalCount = await query.CountAsync(cancellationToken);
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var lines = await query.Include(x => x.Batch)
            .OrderByDescending(x => x.Batch.CreatedAtUtc).ThenByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id).Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return new PagedResult<AdminFamilyGroupHistoryRowDto>([], totalCount, page.NormalizedPage, page.NormalizedPageSize);

        var batchIds = lines.Select(x => x.BatchId).Distinct().ToArray();
        var subscriptionIds = lines.Where(x => x.SubscriptionId.HasValue).Select(x => x.SubscriptionId!.Value).Distinct().ToArray();
        var evidencePage = new PageRequest { Page = request.EvidencePage, PageSize = request.EvidencePageSize };
        var attemptsByBatch = new Dictionary<Guid, List<FamilyCheckoutPaymentAttempt>>();
        var attemptCounts = new Dictionary<Guid, int>();
        var canonicalAttemptByBatch = new Dictionary<Guid, FamilyCheckoutPaymentAttempt?>();
        var receiptsByBatch = new Dictionary<Guid, List<PaymentWebhookReceipt>>();
        var canonicalReceiptByBatch = new Dictionary<Guid, PaymentWebhookReceipt?>();
        var receiptCounts = new Dictionary<Guid, int>();
        foreach (var batchId in batchIds)
        {
            var batchAttemptsQuery = _db.FamilyCheckoutPaymentAttempts.AsNoTracking().Where(x => x.BatchId == batchId);
            attemptCounts[batchId] = await batchAttemptsQuery.CountAsync(cancellationToken);
            attemptsByBatch[batchId] = await batchAttemptsQuery.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id)
                .Skip(evidencePage.Skip).Take(evidencePage.NormalizedPageSize).ToListAsync(cancellationToken);
            canonicalAttemptByBatch[batchId] = await _db.FamilyCheckoutPaymentAttempts.AsNoTracking()
                .Where(x => x.BatchId == batchId && x.ResultCode == "Paid" && x.VerifiedAtUtc.HasValue)
                .OrderByDescending(x => x.VerifiedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            var matchingReceipts = _db.PaymentWebhookReceipts.AsNoTracking().Where(r => r.VerifiedAtUtc.HasValue
                && _db.FamilyCheckoutPaymentAttempts.Any(a => a.BatchId == batchId
                    && ((a.GatewayPaymentId != null && a.GatewayPaymentId == r.PaymentId)
                        || (a.GatewayInvoiceId != null && a.GatewayInvoiceId == r.InvoiceId))));
            receiptCounts[batchId] = await matchingReceipts.CountAsync(cancellationToken);
            receiptsByBatch[batchId] = await matchingReceipts.OrderByDescending(x => x.ProcessedAtUtc).ThenBy(x => x.Id)
                .Skip(evidencePage.Skip).Take(evidencePage.NormalizedPageSize).ToListAsync(cancellationToken);
            var canonical = canonicalAttemptByBatch[batchId];
            canonicalReceiptByBatch[batchId] = canonical is null ? null
                : await _db.PaymentWebhookReceipts.AsNoTracking().Where(receipt =>
                    canonical.GatewayPaymentId != null && canonical.GatewayInvoiceId != null
                    && receipt.PaymentId == canonical.GatewayPaymentId && receipt.InvoiceId == canonical.GatewayInvoiceId
                    && receipt.VerifiedAtUtc.HasValue
                    && (receipt.Status == "PaymentVerified" || receipt.Status == "FulfillmentCompleted"
                        || receipt.Status == "FulfillmentRetryPending"))
                    .OrderByDescending(x => x.ProcessedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        }
        var upgradesBySubscription = new Dictionary<Guid, List<SubscriptionTierUpgrade>>();
        var upgradeCounts = new Dictionary<Guid, int>();
        var upgradeReceiptsBySubscription = new Dictionary<Guid, List<PaymentWebhookReceipt>>();
        foreach (var subscriptionId in subscriptionIds)
        {
            var upgradeQuery = _db.SubscriptionTierUpgrades.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId
                && x.VerifiedPaidAmount.HasValue && x.VerifiedCurrencyCode != null
                && x.PayerCustomerUserId.HasValue && x.VerifiedPaidAtUtc.HasValue);
            upgradeCounts[subscriptionId] = await upgradeQuery.CountAsync(cancellationToken);
            upgradesBySubscription[subscriptionId] = await upgradeQuery.OrderByDescending(x => x.VerifiedPaidAtUtc)
                .ThenBy(x => x.Id).Skip(evidencePage.Skip).Take(evidencePage.NormalizedPageSize).ToListAsync(cancellationToken);
            var upgradePayments = upgradesBySubscription[subscriptionId].Where(x => !string.IsNullOrWhiteSpace(x.PaymentId))
                .Select(x => x.PaymentId!).Distinct().ToArray();
            upgradeReceiptsBySubscription[subscriptionId] = upgradePayments.Length == 0 ? []
                : await _db.PaymentWebhookReceipts.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId
                    && x.PaymentId != null && upgradePayments.Contains(x.PaymentId) && x.VerifiedAtUtc.HasValue
                    && x.Status != "PaymentFailureVerified" && x.Status != "VerificationRejected")
                    .OrderByDescending(x => x.ProcessedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        }
        var batchLines = await _db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => batchIds.Contains(x.BatchId)).ToListAsync(cancellationToken);

        var rows = lines.Select(line =>
        {
            var batchAttempts = attemptsByBatch[line.BatchId];
            var batch = line.Batch;
            var baseReceipts = receiptsByBatch[line.BatchId].ToList();
            if (canonicalReceiptByBatch[line.BatchId] is { } proofReceipt && baseReceipts.All(x => x.Id != proofReceipt.Id))
                baseReceipts.Add(proofReceipt);
            var canonicalAttempt = canonicalAttemptByBatch[line.BatchId];
            var baseReceipt = canonicalAttempt is null ? null : canonicalReceiptByBatch[line.BatchId] is { } canonicalReceipt
                && canonicalReceipt.Amount == batch.FinalPrice
                && string.Equals(canonicalReceipt.CurrencyCode, batch.CurrencyCode, StringComparison.OrdinalIgnoreCase)
                && batch.GatewayPaymentId == canonicalReceipt.PaymentId && batch.GatewayInvoiceId == canonicalReceipt.InvoiceId
                    ? canonicalReceipt : null;
            var allocationLines = batchLines.Where(x => x.BatchId == line.BatchId).ToList();
            var allocationIsComplete = allocationLines.Count > 0
                && allocationLines.All(x => x.NetPaidAmount >= 0m
                    && x.NetPaidAmount == x.PriceBeforeFamilyDiscount - x.FamilyDiscountAllocated
                    && string.Equals(x.CurrencyCode, batch.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                && allocationLines.Sum(x => x.NetPaidAmount) == batch.FinalPrice;
            var lineUpgrades = line.SubscriptionId is Guid lineSubscriptionId
                ? upgradesBySubscription.GetValueOrDefault(lineSubscriptionId) ?? [] : [];
            var upgradeDtos = lineUpgrades.Select(upgrade =>
            {
                var receipt = upgradeReceiptsBySubscription.GetValueOrDefault(upgrade.SubscriptionId)?.FirstOrDefault(x =>
                    x.PaymentId == upgrade.PaymentId && x.VerifiedAtUtc.HasValue
                    && x.Amount == upgrade.VerifiedPaidAmount
                    && string.Equals(x.CurrencyCode, upgrade.VerifiedCurrencyCode, StringComparison.OrdinalIgnoreCase));
                return receipt is null ? null : new AdminFamilyUpgradeDto(upgrade.Id, receipt.Id, upgrade.PayerCustomerUserId,
                    upgrade.PaymentId, upgrade.VerifiedInvoiceId, upgrade.VerifiedPaidAmount, upgrade.VerifiedCurrencyCode,
                    receipt.Status == "FinancialReviewRequired" ? "PaidReviewRequired" : upgrade.Status.ToString(),
                    upgrade.VerifiedPaidAtUtc, upgrade.Status == VerificationStatus.Approved);
            }).Where(x => x is not null).Cast<AdminFamilyUpgradeDto>().ToList();
            var zeroDue = batch.Status == FamilyCheckoutBatchStatus.Paid && batch.PaidAtUtc.HasValue
                && batch.FinalPrice == 0m && allocationIsComplete && line.NetPaidAmount == 0m;
            var verified = batch.Status == FamilyCheckoutBatchStatus.Paid && batch.PaidAtUtc.HasValue
                && baseReceipt is not null && allocationIsComplete;
            var evidenceStatus = verified ? "VerifiedPaid" : zeroDue ? "RecordedZeroDue"
                : batch.Status == FamilyCheckoutBatchStatus.Paid ? "NeedsReview" : "NotPaid";
            var lineAttempts = batchAttempts.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).ToList();
            var lineReceipts = baseReceipts.OrderByDescending(x => x.ProcessedAtUtc).ThenBy(x => x.Id).ToList();
            var upgradeCount = line.SubscriptionId is Guid countSubscriptionId
                ? upgradeCounts.GetValueOrDefault(countSubscriptionId) : 0;
            return new AdminFamilyGroupHistoryRowDto(line.BatchId, line.Id, line.Batch.FamilyGroupId,
                line.FamilyMemberId, line.SubscriptionId, line.Batch.PayerCustomerUserId, line.CheckoutSelected,
                batch.Status.ToString(), evidenceStatus, batch.Purpose, verified || zeroDue ? line.NetPaidAmount : null,
                line.CurrencyCode, line.Batch.Version, line.Batch.CreatedAtUtc, line.CreatedAtUtc,
                lineAttempts.Select(x => new AdminFamilyPaymentAttemptDto(x.Id, x.AttemptKey, x.GatewayPaymentId,
                    x.GatewayInvoiceId, x.RequestedAtUtc, x.VerifiedAtUtc, x.Version, x.ResultCode)).ToList(),
                lineReceipts
                    .Select(x => new AdminFamilyReceiptDto(x.Id, x.PaymentId, x.InvoiceId, x.Amount, x.CurrencyCode,
                        x.Status, x.VerifiedAtUtc, x.ProcessedAtUtc)).ToList(), upgradeDtos,
                attemptCounts[line.BatchId], receiptCounts[line.BatchId], upgradeCount,
                attemptCounts[line.BatchId] > evidencePage.NormalizedPageSize
                    || receiptCounts[line.BatchId] > evidencePage.NormalizedPageSize
                    || upgradeCount > evidencePage.NormalizedPageSize);
        }).ToList();
        return new PagedResult<AdminFamilyGroupHistoryRowDto>(rows, totalCount, page.NormalizedPage, page.NormalizedPageSize);
    }

    public async Task<Result<PagedResult<AdminFamilyCancellationRowDto>>> Handle(
        GetAdminFamilyCancellationHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await _db.FamilyGroups.AsNoTracking().AnyAsync(x => x.Id == request.GroupId, cancellationToken))
            return Error.NotFound(MessageCodes.Common.NotFound, "Family group was not found.");
        var query = _db.SubscriptionCancellations.AsNoTracking()
            .Where(x => x.FamilyGroupId == request.GroupId && x.SettlementKind == "FamilyCancellation");
        var totalCount = await query.CountAsync(cancellationToken);
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var rows = await query.Include(x => x.RefundCalculations)
            .OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(cancellationToken);
        var subscriptionIds = rows.Select(x => x.SubscriptionId).Distinct().ToArray();
        var memberBySubscription = subscriptionIds.Length == 0 ? new Dictionary<Guid, Guid?>()
            : await _db.Subscriptions.AsNoTracking().Where(x => subscriptionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FamilyMemberId, cancellationToken);
        return new PagedResult<AdminFamilyCancellationRowDto>(rows.Select(x =>
        {
            var refund = x.RefundCalculations.OrderByDescending(y => y.CalculatedAtUtc).ThenBy(y => y.Id).FirstOrDefault();
            return new AdminFamilyCancellationRowDto(x.Id, x.SubscriptionId, memberBySubscription.GetValueOrDefault(x.SubscriptionId),
                x.BatchId, x.BatchLineId, x.ActorUserId, x.PayerCustomerUserId, x.Status, x.RequestedAtUtc,
                x.CompletedAtUtc, x.CountedDays, x.UnusedDays, refund?.FinalCashRefund.Amount,
                refund?.FinalCashRefund.CurrencyCode, refund?.Id);
        }).ToList(), totalCount, page.NormalizedPage, page.NormalizedPageSize);
    }
}
