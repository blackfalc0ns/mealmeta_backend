using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;

public sealed class CustomerSubscriptionQueryService : ICustomerSubscriptionQueryService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICapacityHoldExpiryService? _holdExpiry;
    private readonly IFamilySubscriptionAccess _familyAccess;
    private readonly ILocalizedMessageProvider _messages;

    public CustomerSubscriptionQueryService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICapacityHoldExpiryService? holdExpiry = null,
        IFamilySubscriptionAccess? familyAccess = null,
        ILocalizedMessageProvider? messages = null)
    {
        _db = db;
        _clock = clock;
        _holdExpiry = holdExpiry;
        _familyAccess = familyAccess ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
        _messages = messages ?? NullLocalizedMessageProvider.Instance;
    }

    public async Task<Result<CustomerSubscriptionManagementScreenDto>> GetManagementScreenAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default)
    {
        var subscriptions = await VisibleCustomerSubscriptions(customerUserId, includeHistory, ct);
        var cards = new List<CustomerManagedSubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSubscription(subscription, includeInvoices: true, ct, customerUserId));

        var ownMemberIds = await GetOwnFamilyMemberIdsAsync(customerUserId, ct);
        var currentSubscriptionId = SelectCurrentSubscription(subscriptions, customerUserId, ownMemberIds)?.Id;
        var current = cards.FirstOrDefault(x => x.SubscriptionId == currentSubscriptionId);

        return new CustomerSubscriptionManagementScreenDto(
            current,
            cards,
            current?.Invoices ?? [],
            current is null ? MessageCodes.Subscriptions.NoCurrentSubscription : null);
    }

    public async Task<Result<CustomerCurrentSubscriptionScreenDto>> GetCurrentSubscriptionAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var subscriptions = await VisibleCustomerSubscriptions(customerUserId, includeHistory: true, ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct, actorUserId: customerUserId));

        var ownMemberIds = await GetOwnFamilyMemberIdsAsync(customerUserId, ct);
        var currentSubscriptionId = SelectCurrentSubscription(subscriptions, customerUserId, ownMemberIds)?.Id;
        var current = cards.FirstOrDefault(x => x.SubscriptionId == currentSubscriptionId);

        return new CustomerCurrentSubscriptionScreenDto(
            current,
            current is null ? MessageCodes.Subscriptions.NoCurrentSubscription : null);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionSlimDto>>> ListSubscriptionsAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default)
    {
        var subscriptions = await VisibleCustomerSubscriptions(customerUserId, includeHistory, ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct, actorUserId: customerUserId));

        return cards;
    }

    public async Task<Result<IReadOnlyList<CustomerUncompletedSubscriptionDto>>> GetUncompletedSubscriptionsAsync(Guid customerUserId, CancellationToken ct = default)
    {
        // A managed family member may view their funded subscription through the
        // normal customer screens, but cannot start or resume any subscription
        // checkout. Family subscription setup belongs to the group manager.
        var isManagedFamilyMember = await (from access in _db.FamilyMemberAccesses.AsNoTracking()
                                           join member in _db.FamilyMembers.AsNoTracking() on access.FamilyMemberId equals member.Id
                                           join familyGroup in _db.FamilyGroups.AsNoTracking() on member.FamilyGroupId equals familyGroup.Id
                                           where access.LinkedCustomerUserId == customerUserId
                                               && access.RemovedAtUtc == null
                                               && !access.DetachedFromFamilyManagement
                                               && !access.IsPaused
                                               && !member.IsDeleted
                                               && !familyGroup.IsDeleted
                                               && familyGroup.ManagerCustomerUserId != customerUserId
                                           select access.FamilyMemberId).AnyAsync(ct);
        if (isManagedFamilyMember)
            return Result.Success<IReadOnlyList<CustomerUncompletedSubscriptionDto>>([]);

        var uncompletedStatuses = new[]
        {
            SubscriptionStatus.Draft,
            SubscriptionStatus.PendingPayment,
            SubscriptionStatus.PaymentFailed
        };

        if (_holdExpiry is not null)
            await _holdExpiry.ExpireDueAsync(ct);
        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, ct, customerUserId);

        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == customerUserId
                        && x.FamilyGroupId == null
                        && uncompletedStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var pricingRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);

        var result = new List<CustomerUncompletedSubscriptionDto>(subscriptions.Count);
        foreach (var sub in subscriptions)
        {
            var holds = await _db.CapacityHolds.AsNoTracking()
                .Where(x => x.SubscriptionId == sub.Id && x.Status == CapacityHoldStatus.PendingPayment)
                .ToListAsync(ct);

            var isHoldActive = holds.Any(x => x.ExpiresAtUtc > _clock.UtcNow);
            var holdExpiresAtUtc = holds.Where(x => x.ExpiresAtUtc > _clock.UtcNow).Max(x => (DateTime?)x.ExpiresAtUtc);
            var holdRemainingSeconds = isHoldActive ? Math.Max(0, (int)(holdExpiresAtUtc!.Value - _clock.UtcNow).TotalSeconds) : (int?)null;

            var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(sub.SelectedTier, pricing.TierLabels);
            var heroImage = await GetHeroImage(sub.Id, ct);

            result.Add(new CustomerUncompletedSubscriptionDto(
                sub.Id,
                sub.Status.ToString(),
                SubscriptionLocalization.StatusNameAr(sub.Status),
                sub.ProgramId,
                sub.Program?.NameAr ?? string.Empty,
                sub.Program?.NameEn ?? string.Empty,
                sub.BundleId,
                sub.Bundle?.NameAr ?? string.Empty,
                sub.Bundle?.NameEn ?? string.Empty,
                sub.SelectedTier,
                tierNameAr,
                tierNameEn,
                tierLogoUrl,
                sub.Bundle?.MealsPerDay ?? 0,
                heroImage,
                sub.StartDate,
                sub.EndDate,
                sub.DurationDays,
                sub.IncludeFriday,
                sub.PaidAmount.Amount,
                sub.PaidAmount.CurrencyCode,
                sub.DeliveryAddress,
                sub.CreatedAtUtc,
                isHoldActive,
                holdRemainingSeconds,
                holdExpiresAtUtc,
                new CustomerUncompletedActionsDto(
                    CanDelete: true,
                    CanResume: true,
                    DeleteEndpoint: $"/api/v1/customer/subscriptions/{sub.Id}",
                    ResumeEndpoint: $"/api/v1/customer/subscriptions/{sub.Id}/resume",
                    NextScreen: "checkout",
                    PayEndpoint: $"/api/v1/customer/subscriptions/{sub.Id}/resume")));
        }

        var familyDrafts = await _db.FamilyGroups.AsNoTracking()
            .Include(x => x.Members)
            .Where(x => x.ManagerCustomerUserId == customerUserId
                && (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending))
            // A stale draft must not be presented as a new subscription when the
            // same family already has a funded/live member subscription.
            .Where(x => !_db.Subscriptions.Any(subscription =>
                subscription.FamilyGroupId == x.Id
                && (subscription.Status == SubscriptionStatus.Paid
                    || subscription.Status == SubscriptionStatus.Scheduled
                    || subscription.Status == SubscriptionStatus.Active
                    || subscription.Status == SubscriptionStatus.Frozen)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        foreach (var group in familyDrafts)
            result.Add(await MapIncompleteFamilyGroup(group, pricing.TierLabels, ct));

        return result.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionSlimDto>>> GetArchivedSubscriptionsAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var terminalStatuses = new[]
        {
            SubscriptionStatus.Completed,
            SubscriptionStatus.Expired,
            SubscriptionStatus.Cancelled,
            SubscriptionStatus.Upgraded
        };

        var candidates = await QueryCustomerSubscriptions(customerUserId, true)
            .Where(x => terminalStatuses.Contains(x.Status)).ToListAsync(ct);
        var subscriptions = new List<Subscription>();
        foreach (var candidate in candidates)
            if ((await _familyAccess.AuthorizeAsync(candidate, customerUserId, FamilyAction.ViewPlan, ct)).IsSuccess)
                subscriptions.Add(candidate);
        var collapsed = await CollapseArchivedSubscriptionsAsync(subscriptions, ct, customerUserId);
        var cards = new List<CustomerSubscriptionSlimDto>(collapsed.Count);
        foreach (var sub in collapsed)
            cards.Add(await MapSlimSubscription(sub, ct, actorUserId: customerUserId));

        return cards;
    }

    public async Task<Result<CustomerManagedSubscriptionDto>> GetManagementDetailsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await OwnedSubscription(subscriptionId, customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        return await MapSubscription(subscription, includeInvoices: true, ct, customerUserId);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceListDto>>> GetInvoicesAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var subscriptions = await QueryCustomerSubscriptions(customerUserId, includeHistory: true).ToListAsync(ct);
        var invoices = new List<CustomerSubscriptionInvoiceDto>();
        foreach (var subscription in subscriptions)
            if ((await _familyAccess.AuthorizeFinancialHistoryAsync(subscription, customerUserId, ct)).IsSuccess)
                invoices.AddRange(await GetInvoices(subscription, ct, customerUserId));
        return invoices.GroupBy(x => new { x.ReceiptId, x.FamilyMemberId })
            .Select(x => x.First()).OrderByDescending(x => x.IssuedAtUtc)
            .Select(x => new CustomerSubscriptionInvoiceListDto(x.ReceiptId, x.InvoiceNumber, x.PaymentId,
                x.IssuedAtUtc, x.Amount, x.CurrencyCode, x.Status, InvoiceStatusNameAr(x.Status),
                x.Gateway, x.MaskedCardNumber, x.CanDownload, x.DownloadUnavailableReason,
                x.BatchId, x.FamilyMemberId, x.PayerCustomerUserId, x.IsMemberAllocation,
                x.UpgradeId, x.IsPaidReviewRequired, InvoiceStatusNameEn(x.Status))).ToList();
    }

    public async Task<Result<CustomerSubscriptionInvoiceDto>> GetInvoiceDetailAsync(Guid receiptId, Guid customerUserId, CancellationToken ct = default)
    {
        var visible = await GetInvoicesAsync(customerUserId, ct);
        if (visible.IsFailure) return visible.Error;
        var row = visible.Value.FirstOrDefault(x => x.ReceiptId == receiptId);
        if (row is null)
            return Error.NotFound(MessageCodes.Subscriptions.InvoiceNotFound, "Invoice not found.");
        return new CustomerSubscriptionInvoiceDto(row.ReceiptId, row.InvoiceNumber, row.PaymentId, row.IssuedAtUtc,
            row.Amount, row.CurrencyCode, row.Status, row.Gateway, row.MaskedCardNumber, row.CanDownload,
            row.DownloadUnavailableReason, row.BatchId, row.FamilyMemberId, row.PayerCustomerUserId, row.IsMemberAllocation,
            row.UpgradeId, row.IsPaidReviewRequired);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceDto>>> GetSubscriptionInvoicesAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await OwnedSubscription(subscriptionId, customerUserId, ct, FamilyAction.ViewInvoices);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        return Result.Success(await GetInvoices(subscription, ct, customerUserId));
    }

    public async Task<Result<CustomerDefaultPaymentMethodDto>> GetDefaultPaymentMethodAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var invoices = await GetInvoicesAsync(customerUserId, ct);
        var latestGateway = invoices.Value.OrderByDescending(x => x.IssuedAtUtc).FirstOrDefault()?.Gateway;
        return new CustomerDefaultPaymentMethodDto(false, null, null, null, latestGateway, MessageCodes.Subscriptions.SavedCardNotAvailable);
    }

    private IQueryable<Subscription> QueryCustomerSubscriptions(Guid customerUserId, bool includeHistory)
    {
        var managerGroupIds = _db.FamilyGroups.IgnoreQueryFilters()
            .Where(g => g.ManagerCustomerUserId == customerUserId)
            .Select(g => g.Id);

        var linkedGroupIds = from a in _db.FamilyMemberAccesses
                             join m in _db.FamilyMembers on a.FamilyMemberId equals m.Id
                             where a.LinkedCustomerUserId == customerUserId && a.RemovedAtUtc == null
                                 && a.HasSeparateAccount && !a.IsPaused
                             select m.FamilyGroupId;
        var terminalStatuses = new[] { SubscriptionStatus.Completed, SubscriptionStatus.Expired,
            SubscriptionStatus.Cancelled, SubscriptionStatus.Upgraded };
        var query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == customerUserId || (x.FamilyGroupId != null
                && (managerGroupIds.Contains(x.FamilyGroupId.Value) || linkedGroupIds.Contains(x.FamilyGroupId.Value)))
                || _db.FamilyCheckoutBatchLines.Any(l => l.SubscriptionId == x.Id && l.FamilyMemberId == x.FamilyMemberId
                    && l.Batch.FamilyGroupId == x.FamilyGroupId && l.Batch.Status == FamilyCheckoutBatchStatus.Paid
                    && l.Batch.PayerCustomerUserId == customerUserId)
                || (x.FamilyGroupId != null && terminalStatuses.Contains(x.Status)
                    && _db.SubscriptionTierUpgrades.Any(upgrade => upgrade.SubscriptionId == x.Id
                        && upgrade.PayerCustomerUserId == customerUserId && upgrade.VerifiedPaidAmount.HasValue
                        && upgrade.VerifiedCurrencyCode != null && upgrade.VerifiedPaidAtUtc.HasValue
                        && _db.PaymentWebhookReceipts.Any(receipt => receipt.SubscriptionId == x.Id
                            && receipt.PaymentId == upgrade.PaymentId && receipt.VerifiedAtUtc.HasValue
                            && receipt.Amount == upgrade.VerifiedPaidAmount && receipt.CurrencyCode == upgrade.VerifiedCurrencyCode
                            && receipt.Status != "PaymentFailureVerified" && receipt.Status != "VerificationRejected"))));

        if (!includeHistory)
            query = query.Where(x => x.Status != SubscriptionStatus.Completed && x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired);

        return query.OrderByDescending(x => x.CreatedAtUtc);
    }

    private async Task<HashSet<Guid>> GetOwnFamilyMemberIdsAsync(Guid customerUserId, CancellationToken ct) =>
        (await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => x.LinkedCustomerUserId == customerUserId && x.RemovedAtUtc == null)
            .Select(x => x.FamilyMemberId).ToListAsync(ct)).ToHashSet();

    private static Subscription? SelectCurrentSubscription(
        IReadOnlyList<Subscription> subscriptions, Guid actorUserId, HashSet<Guid> ownFamilyMemberIds) =>
        subscriptions.Where(x => IsCurrentStatus(CustomerFacingStatus(x.Status)))
            .OrderBy(x => x.FamilyGroupId.HasValue
                ? (x.FamilyMemberId.HasValue && ownFamilyMemberIds.Contains(x.FamilyMemberId.Value) ? 0 : 1)
                : (x.CustomerUserId == actorUserId ? 0 : 1))
            .ThenBy(x => CurrentStatusPriority(CustomerFacingStatus(x.Status)))
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefault();

    private async Task<IReadOnlyList<Subscription>> VisibleCustomerSubscriptions(Guid customerUserId, bool includeHistory, CancellationToken ct)
    {
        var subscriptions = await QueryCustomerSubscriptions(customerUserId, includeHistory).ToListAsync(ct);
        var authorized = new List<Subscription>();
        foreach (var subscription in subscriptions)
            if ((await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.ViewPlan, ct)).IsSuccess)
                authorized.Add(subscription);
        return await CollapseFamilyMemberSubscriptionsAsync(authorized, ct, customerUserId);
    }

    private async Task<IReadOnlyList<Subscription>> CollapseFamilyMemberSubscriptionsAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct, Guid actorUserId)
    {
        var familyIds = subscriptions
            .Where(x => x.FamilyGroupId is Guid)
            .Select(x => x.FamilyGroupId!.Value)
            .Distinct()
            .ToList();
        if (familyIds.Count == 0)
            return subscriptions;

        var anchors = await _db.FamilyGroups.AsNoTracking()
            .Where(x => familyIds.Contains(x.Id) && x.ManagerCustomerUserId == actorUserId)
            .Select(x => new { x.Id, x.PaymentAnchorSubscriptionId })
            .ToDictionaryAsync(x => x.Id, x => x.PaymentAnchorSubscriptionId, ct);
        var familyMembers = await _db.FamilyMembers.AsNoTracking().Where(x => familyIds.Contains(x.FamilyGroupId)
            && !x.IsDeleted).ToListAsync(ct);
        var projections = await new FamilyServiceSubscriptionResolver(_db).ResolveAsync(
            familyMembers.Select(x => x.Id).ToArray(), familyMembers.ToDictionary(x => x.Id, x => x.MemberSubscriptionId),
            BusinessCalendarDate.KuwaitToday(_clock.UtcNow), ct);

        var result = new List<Subscription>();
        var seenFamilies = new HashSet<Guid>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.FamilyGroupId is not Guid familyId || !anchors.ContainsKey(familyId))
            {
                result.Add(subscription);
                continue;
            }

            if (!seenFamilies.Add(familyId))
                continue;

            var familyRows = subscriptions.Where(x => x.FamilyGroupId == familyId).ToList();
            var memberIds = familyMembers.Where(x => x.FamilyGroupId == familyId).Select(x => x.Id).ToHashSet();
            var serviceIds = projections.Where(x => memberIds.Contains(x.Key))
                .Select(x => x.Value.CurrentId ?? x.Value.UpcomingId).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
            // The manager's primary calendar is personal. When the actor is the
            // family manager, never replace it with a child's service merely
            // because that child has the next service day.
            var managerMemberId = familyMembers.FirstOrDefault(x => x.FamilyGroupId == familyId
                && x.Role == FamilyMemberRole.Manager)?.Id;
            var managerServiceIds = managerMemberId is Guid managerId && projections.TryGetValue(managerId, out var managerProjection)
                ? new[] { managerProjection.CurrentId, managerProjection.UpcomingId }
                    .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet()
                : [];
            var serviceSubscription = familyRows.Where(x => managerServiceIds.Contains(x.Id))
                .OrderBy(x => CurrentStatusPriority(CustomerFacingStatus(x.Status)))
                .ThenByDescending(x => x.StartDate).FirstOrDefault()
                ?? familyRows.Where(x => serviceIds.Contains(x.Id))
                .OrderBy(x => CurrentStatusPriority(CustomerFacingStatus(x.Status)))
                .ThenByDescending(x => x.StartDate).FirstOrDefault();
            var anchorId = anchors.GetValueOrDefault(familyId);
            result.Add(serviceSubscription ?? familyRows.FirstOrDefault(x => x.Id == anchorId) ?? familyRows[0]);
        }

        return result.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    private async Task<IReadOnlyList<Subscription>> CollapseArchivedSubscriptionsAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct, Guid actorUserId)
    {
        var familyIds = subscriptions
            .Where(x => x.FamilyGroupId is Guid)
            .Select(x => x.FamilyGroupId!.Value)
            .Distinct()
            .ToList();
        if (familyIds.Count == 0)
            return subscriptions;

        var anchors = await _db.FamilyGroups.AsNoTracking()
            .Where(x => familyIds.Contains(x.Id) && x.ManagerCustomerUserId == actorUserId)
            .Select(x => new { x.Id, x.PaymentAnchorSubscriptionId })
            .ToDictionaryAsync(x => x.Id, x => x.PaymentAnchorSubscriptionId, ct);

        var ids = subscriptions.Select(x => x.Id).ToList();
        var paidLines = await _db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
            .Where(x => x.SubscriptionId.HasValue && ids.Contains(x.SubscriptionId.Value)
                && x.Batch.Status == FamilyCheckoutBatchStatus.Paid).ToListAsync(ct);
        var batchBySubscription = paidLines.ToDictionary(x => x.SubscriptionId!.Value, x => x.BatchId);
        var result = new List<Subscription>();
        var seenBatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subscription in subscriptions)
        {
            if (subscription.FamilyGroupId is not Guid familyId || !anchors.ContainsKey(familyId))
            {
                result.Add(subscription);
                continue;
            }

            // A payment invoice is assigned to every subscription created by the same family checkout.
            // Historical rows without an invoice cannot be safely batched, so retain each one separately.
            var invoiceId = subscription.PaymentInvoiceId?.Trim();
            var batchKey = batchBySubscription.TryGetValue(subscription.Id, out var paidBatchId)
                ? $"{familyId:N}:batch:{paidBatchId:N}"
                : string.IsNullOrWhiteSpace(invoiceId)
                ? $"{familyId:N}:subscription:{subscription.Id:N}"
                : $"{familyId:N}:invoice:{invoiceId}";
            if (!seenBatches.Add(batchKey))
                continue;

            var familyBatchRows = batchBySubscription.ContainsKey(subscription.Id)
                ? subscriptions.Where(x => batchBySubscription.GetValueOrDefault(x.Id) == paidBatchId).ToList()
                : string.IsNullOrWhiteSpace(invoiceId)
                ? [subscription]
                : subscriptions.Where(x => x.FamilyGroupId == familyId
                    && string.Equals(x.PaymentInvoiceId?.Trim(), invoiceId, StringComparison.OrdinalIgnoreCase)).ToList();
            result.Add(familyBatchRows
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PaymentId))
                ?? familyBatchRows.FirstOrDefault(x => x.Id == anchors.GetValueOrDefault(familyId))
                ?? familyBatchRows[0]);
        }

        return result.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, FamilySubscriptionDisplay>> BuildArchivedFamilyDisplaysAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        var familySubscriptions = subscriptions.Where(x => x.FamilyGroupId.HasValue).ToList();
        if (familySubscriptions.Count == 0)
            return new Dictionary<Guid, FamilySubscriptionDisplay>();

        var familyIds = familySubscriptions.Select(x => x.FamilyGroupId!.Value).Distinct().ToList();
        var familyMemberIds = familySubscriptions.Where(x => x.FamilyMemberId.HasValue)
            .Select(x => x.FamilyMemberId!.Value).Distinct().ToList();
        var memberNames = familyMemberIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.FamilyMembers.AsNoTracking().Where(x => familyMemberIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var groups = await _db.FamilyGroups.AsNoTracking()
            .Where(x => familyIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.QuotedFinalPrice, MemberCount = x.Members.Count })
            .ToDictionaryAsync(x => x.Id, ct);

        var invoiceIds = familySubscriptions.Select(x => x.PaymentInvoiceId?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var batchRows = invoiceIds.Count == 0
            ? []
            : await _db.Subscriptions.AsNoTracking()
                .Where(x => x.FamilyGroupId.HasValue
                    && familyIds.Contains(x.FamilyGroupId.Value)
                    && x.PaymentInvoiceId != null
                    && invoiceIds.Contains(x.PaymentInvoiceId))
                .ToListAsync(ct);

        var subscriptionIds = familySubscriptions.Select(x => x.Id).ToList();
        var receipts = subscriptionIds.Count == 0
            ? []
            : await _db.PaymentWebhookReceipts.AsNoTracking()
                .Where(x => x.SubscriptionId.HasValue && subscriptionIds.Contains(x.SubscriptionId.Value)
                    && x.VerifiedAtUtc.HasValue
                    && x.Status != "PaymentFailureVerified"
                    && x.Status != "VerificationRejected"
                    && x.Amount.HasValue)
                .OrderByDescending(x => x.ProcessedAtUtc)
                .ToListAsync(ct);
        var familyDiscounts = subscriptionIds.Count == 0
            ? []
            : await _db.DiscountsAndReferralLogs.AsNoTracking()
                .Where(x => subscriptionIds.Contains(x.SubscriptionId) && x.DiscountType == "FamilyDiscount")
                .ToListAsync(ct);

        var displays = new Dictionary<Guid, FamilySubscriptionDisplay>();
        foreach (var subscription in familySubscriptions)
        {
            var familyId = subscription.FamilyGroupId!.Value;
            groups.TryGetValue(familyId, out var group);
            var invoiceId = subscription.PaymentInvoiceId?.Trim();
            var rowsForBatch = string.IsNullOrWhiteSpace(invoiceId)
                ? [subscription]
                : batchRows.Where(x => x.FamilyGroupId == familyId
                    && string.Equals(x.PaymentInvoiceId?.Trim(), invoiceId, StringComparison.OrdinalIgnoreCase)).ToList();
            var receipt = receipts.FirstOrDefault(x => x.SubscriptionId == subscription.Id
                && string.Equals(x.InvoiceId?.Trim(), invoiceId, StringComparison.OrdinalIgnoreCase));

            var amount = receipt?.Amount;
            if (amount is null && !string.IsNullOrWhiteSpace(invoiceId))
            {
                amount = rowsForBatch.Sum(x => x.PaidAmount.Amount)
                    - familyDiscounts.Where(x => x.SubscriptionId == subscription.Id).Sum(x => x.Amount.Amount);
            }
            amount ??= subscription.PaidAmount.Amount;
            var memberCount = string.IsNullOrWhiteSpace(invoiceId)
                ? 1
                : rowsForBatch.Where(x => x.FamilyMemberId.HasValue)
                    .Select(x => x.FamilyMemberId!.Value).Distinct().Count();

            displays[subscription.Id] = new FamilySubscriptionDisplay(
                "FAMILY", familyId, Math.Max(memberCount, 1), group?.Name, Math.Max(amount.Value, 0m),
                SubscriptionLocalization.FamilyStatusNameAr(CustomerDisplayStatus(subscription.Status)), subscription.FamilyMemberId,
                subscription.FamilyMemberId is Guid memberId && memberNames.TryGetValue(memberId, out var name) ? name : null,
                SubscriptionLocalization.FamilyStatusNameEn(CustomerDisplayStatus(subscription.Status)));
        }

        return displays;
    }

    private async Task<FamilySubscriptionDisplay> FamilyDisplayAsync(Subscription subscription, CancellationToken ct, Guid? actorUserId)
    {
        if (subscription.FamilyGroupId is not Guid familyId)
            return new("INDIVIDUAL", null, 1, null, subscription.PaidAmount.Amount,
                SubscriptionLocalization.StatusNameAr(CustomerDisplayStatus(subscription.Status)), StatusNameEn: SubscriptionLocalization.StatusNameEn(CustomerDisplayStatus(subscription.Status)));
        var group = await _db.FamilyGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == familyId, ct);
        var member = await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x =>
            x.FamilyGroupId == familyId && (subscription.FamilyMemberId.HasValue
                ? x.Id == subscription.FamilyMemberId.Value
                : x.MemberSubscriptionId == subscription.Id), ct);
        var line = await _db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.FamilyMemberId == subscription.FamilyMemberId
                && x.Batch.FamilyGroupId == familyId && x.Batch.Status == FamilyCheckoutBatchStatus.Paid, ct);
        var isManager = group?.ManagerCustomerUserId == actorUserId;
        decimal? amount = null;
        var memberCount = 1;
        if (line is not null)
        {
            amount = isManager ? line.Batch.FinalPrice : line.NetPaidAmount;
            if (isManager) memberCount = await _db.FamilyCheckoutBatchLines.CountAsync(x => x.BatchId == line.BatchId && x.SubscriptionId.HasValue, ct);
        }
        else if (isManager)
        {
            // Legacy financial display is based on verified receipt or persisted paid provenance,
            // never the mutable family draft quote.
            var legacy = (await BuildArchivedFamilyDisplaysAsync([subscription], ct)).GetValueOrDefault(subscription.Id);
            amount = legacy?.PaidAmount;
            memberCount = legacy?.MemberCount ?? 1;
        }
        return new("FAMILY", familyId, memberCount, group?.Name, amount,
            SubscriptionLocalization.FamilyStatusNameAr(CustomerDisplayStatus(subscription.Status)), member?.Id, member?.DisplayName,
            SubscriptionLocalization.FamilyStatusNameEn(CustomerDisplayStatus(subscription.Status)));
    }

    private IQueryable<InvoiceReceiptRow> InvoiceReceiptQuery(Guid customerUserId)
    {
        var managerGroupIds = _db.FamilyGroups
            .Where(g => g.ManagerCustomerUserId == customerUserId)
            .Select(g => g.Id);

        return from r in _db.PaymentWebhookReceipts.AsNoTracking()
               join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
               where (s.CustomerUserId == customerUserId || (s.FamilyGroupId != null && managerGroupIds.Contains(s.FamilyGroupId.Value)))
                     && r.VerifiedAtUtc.HasValue
                     && r.Status != "PaymentFailureVerified"
                     && r.Status != "VerificationRejected"
               orderby r.ProcessedAtUtc descending
               select new InvoiceReceiptRow(r, s.PaidAmount.Amount, s.PaidAmount.CurrencyCode);
    }

    private async Task<Subscription?> OwnedSubscription(Guid id, Guid customerUserId, CancellationToken ct,
        FamilyAction action = FamilyAction.ViewPlan)
    {
        var subscription = await _db.Subscriptions.AsNoTracking().Include(x => x.Program).Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return null;
        return (await (action == FamilyAction.ViewInvoices
            ? _familyAccess.AuthorizeFinancialHistoryAsync(subscription, customerUserId, ct)
            : _familyAccess.AuthorizeAsync(subscription, customerUserId, action, ct))).IsSuccess ? subscription : null;
    }

    private async Task<CustomerManagedSubscriptionDto> MapSubscription(Subscription subscription, bool includeInvoices, CancellationToken ct, Guid actorUserId)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var family = await FamilyDisplayAsync(subscription, ct, actorUserId);
        var canViewInvoices = (await _familyAccess.AuthorizeAsync(subscription, actorUserId, FamilyAction.ViewInvoices, ct)).IsSuccess;
        var invoices = includeInvoices && canViewInvoices ? await GetInvoices(subscription, ct, actorUserId) : Array.Empty<CustomerSubscriptionInvoiceDto>();
        return new CustomerManagedSubscriptionDto(subscription.Id, CustomerFacingStatus(CustomerDisplayStatus(subscription.Status)), family.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, canViewInvoices ? family.PaidAmount : null, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, canViewInvoices ? common.PendingUpgrade : null, await AuthorizeActionsAsync(subscription, actorUserId, common.Actions, ct), invoices, common.ActiveFreeze,
            family.Kind, family.FamilyGroupId, family.MemberCount, family.FamilyName,
            family.FamilyMemberId, family.FamilyMemberName, family.StatusNameEn);
    }

    private async Task<CustomerSubscriptionSlimDto> MapSlimSubscription(
        Subscription subscription, CancellationToken ct, FamilySubscriptionDisplay? preloadedFamily = null, Guid? actorUserId = null)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var family = preloadedFamily ?? await FamilyDisplayAsync(subscription, ct, actorUserId);
        var canViewInvoices = actorUserId.HasValue && (await _familyAccess.AuthorizeAsync(subscription, actorUserId.Value, FamilyAction.ViewInvoices, ct)).IsSuccess;
        return new CustomerSubscriptionSlimDto(subscription.Id, CustomerFacingStatus(CustomerDisplayStatus(subscription.Status)), family.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, canViewInvoices ? family.PaidAmount : null, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, canViewInvoices ? common.PendingUpgrade : null, await AuthorizeActionsAsync(subscription, actorUserId!.Value, common.Actions, ct), common.ActiveFreeze,
            family.Kind, family.FamilyGroupId, family.MemberCount, family.FamilyName,
            family.FamilyMemberId, family.FamilyMemberName, family.StatusNameEn);
    }

    private async Task<CustomerSubscriptionActionsDto> AuthorizeActionsAsync(Subscription subscription, Guid actor,
        CustomerSubscriptionActionsDto actions, CancellationToken ct)
    {
        if (subscription.FamilyGroupId is null) return actions;
        var renew = await _familyAccess.AuthorizeAsync(subscription, actor, FamilyAction.RequestRenewal, ct);
        var freeze = await _familyAccess.AuthorizeAsync(subscription, actor, FamilyAction.RequestFreeze, ct);
        var cancel = await _familyAccess.AuthorizeAsync(subscription, actor, FamilyAction.CancelSubscription, ct);
        var upgrade = await _familyAccess.AuthorizeAsync(subscription, actor, FamilyAction.RequestUpgrade, ct);
        return actions with {
            CanRenew = actions.CanRenew && renew.IsSuccess, RenewUnavailableReason = renew.IsFailure ? renew.Error.Code : actions.RenewUnavailableReason,
            CanFreeze = actions.CanFreeze && freeze.IsSuccess, FreezeUnavailableReason = freeze.IsFailure ? freeze.Error.Code : actions.FreezeUnavailableReason,
            CanCancel = actions.CanCancel && cancel.IsSuccess, CancelUnavailableReason = cancel.IsFailure ? cancel.Error.Code : actions.CancelUnavailableReason,
            CanUpgrade = actions.CanUpgrade && upgrade.IsSuccess, UpgradeUnavailableReason = upgrade.IsFailure ? upgrade.Error.Code : actions.UpgradeUnavailableReason
        };
    }

    private async Task<SubscriptionCommonData> BuildSubscriptionCommon(Subscription subscription, CancellationToken ct)
    {
        var businessToday = subscription.FamilyGroupId.HasValue ? BusinessCalendarDate.KuwaitToday(_clock.UtcNow) : _clock.TodayUtc;
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var totalDays = days.Count > 0 ? days.Count : subscription.DurationDays;
        var completedDays = days.Count(x => x.Status == SubscriptionDayStatus.Delivered);
        var remainingDays = Math.Max(0, totalDays - completedDays);
        var progress = totalDays == 0 ? 0m : decimal.Round(completedDays * 100m / totalDays, 1);

        var restaurant = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.IsInitiallySelected)
            .Select(x => new { x.RestaurantId, x.NameAr, x.NameEn }).FirstOrDefaultAsync(ct);

        string? logoUrl = null;
        if (restaurant is not null)
        {
            var path = await _db.Documents.AsNoTracking()
                .Where(x => x.OwnerType == DocumentOwnerType.Restaurant && x.OwnerId == restaurant.RestaurantId && x.DocumentType == DocumentType.ProfileImage)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.StoragePath).FirstOrDefaultAsync(ct);
            logoUrl = CatalogImageStorage.ToContentUrl(path);
        }

        var restaurantDto = restaurant is null ? null : new CustomerFrozenRestaurantDto(restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, logoUrl);

        var heroImage = await GetHeroImage(subscription.Id, ct);
        var policy = await SubscriptionFreezePolicyDefaults.ReadAsync(_db, ct);
        var pendingFreeze = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new CustomerPendingFreezeDto(x.Id, x.FreezeStartDate, x.FreezeEndDate, x.FreezeDays, x.Status))
            .FirstOrDefaultAsync(ct);

        var approvedFreeze = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Approved && x.FreezeEndDate >= businessToday)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var cancellationPending = subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.CancellationPending;
        var pendingUpgradeRow = await _db.SubscriptionTierUpgrades.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var pendingUpgrade = pendingUpgradeRow is not null;
        var pendingUpgradeDto = pendingUpgradeRow is null ? null : new CustomerPendingUpgradeDto(
            pendingUpgradeRow.Id,
            pendingUpgradeRow.FromTier,
            pendingUpgradeRow.ToTier,
            pendingUpgradeRow.PriceDifference.Amount,
            pendingUpgradeRow.Status);

        var (canUpgrade, upgradeUnavailableReason) = ComputeUpgradeAvailability(subscription, pendingUpgrade);

        var hasApprovedFreeze = approvedFreeze is not null || subscription.Status == SubscriptionStatus.Frozen;
        var canFreeze = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled && pendingFreeze is null && !hasApprovedFreeze && !cancellationPending;
        var canCancel = (subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen) && !cancellationPending;
        var canRenew = subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active or SubscriptionStatus.Frozen or SubscriptionStatus.Completed or SubscriptionStatus.Expired;
        var operations = await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, ct);
        canRenew = canRenew && MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.IsRenewalAvailable(operations, subscription.EndDate, businessToday);
        var freezeUsage = await _db.SubscriptionFreezeRequests.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id &&
            (x.Status == VerificationStatus.Approved || x.Status == VerificationStatus.Submitted)).ToListAsync(ct);
        canFreeze = canFreeze && policy.Enabled && freezeUsage.Count < policy.MaxOccurrences &&
            (policy.MaxTotalDays is null || freezeUsage.Sum(x => x.FreezeDays) + policy.MinDays <= policy.MaxTotalDays);

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);

        var statusNameAr = SubscriptionLocalization.StatusNameAr(CustomerDisplayStatus(subscription.Status));

        var earliestFreezeStartDate = businessToday.AddDays(policy.StartDelayDays);
        var latestLockedServiceDate = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id &&
                        x.ServiceDate >= businessToday &&
                        (x.Status == SubscriptionDayStatus.Locked72h ||
                         x.Status == SubscriptionDayStatus.SentToRestaurant ||
                         x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         x.Status == SubscriptionDayStatus.Preparing))
            .MaxAsync(x => (DateOnly?)x.ServiceDate, ct);

        if (latestLockedServiceDate.HasValue && latestLockedServiceDate.Value >= earliestFreezeStartDate)
        {
            earliestFreezeStartDate = latestLockedServiceDate.Value.AddDays(1);
        }

        var freezeNoticeAr = SubscriptionLocalization.FreezeNoticeAr(policy.StartDelayDays);
        var freezeNoticeEn = SubscriptionLocalization.FreezeNoticeEn(policy.StartDelayDays);

        CustomerActiveFreezeDto? activeFreezeDto = null;
        if (approvedFreeze is not null)
        {
            var frozenDates = days
                .Where(x => x.Status == SubscriptionDayStatus.Frozen && x.ServiceDate >= approvedFreeze.FreezeStartDate && x.ServiceDate <= approvedFreeze.FreezeEndDate)
                .Select(x => x.ServiceDate).ToList();

            var compensationDates = days
                .Where(x => x.Status != SubscriptionDayStatus.Frozen && x.ServiceDate > approvedFreeze.FreezeEndDate)
                .TakeLast(frozenDates.Count)
                .Select(x => x.ServiceDate).ToList();

            var isCurrentlyActive = businessToday >= approvedFreeze.FreezeStartDate && businessToday <= approvedFreeze.FreezeEndDate;
            var isUpcoming = businessToday < approvedFreeze.FreezeStartDate;

            var summaryAr = SubscriptionLocalization.FreezeSummaryAr(frozenDates.Count, approvedFreeze.FreezeStartDate, approvedFreeze.FreezeEndDate, compensationDates.Count, subscription.EndDate);
            var summaryEn = SubscriptionLocalization.FreezeSummaryEn(frozenDates.Count, approvedFreeze.FreezeStartDate, approvedFreeze.FreezeEndDate, compensationDates.Count, subscription.EndDate);

            activeFreezeDto = new CustomerActiveFreezeDto(
                approvedFreeze.Id,
                approvedFreeze.FreezeStartDate,
                approvedFreeze.FreezeEndDate,
                approvedFreeze.FreezeDays,
                frozenDates.Count,
                "Approved",
                SubscriptionLocalization.VerificationStatusNameAr(VerificationStatus.Approved),
                isCurrentlyActive,
                isUpcoming,
                approvedFreeze.Reason,
                approvedFreeze.CreatedAtUtc,
                approvedFreeze.ProcessedAtUtc,
                frozenDates,
                compensationDates,
                subscription.EndDate,
                summaryAr,
                summaryEn);
        }

        var actions = new CustomerSubscriptionActionsDto(canRenew, canFreeze, canCancel, canUpgrade,
            canRenew ? null : "renewal_not_available",
            canFreeze ? null : pendingFreeze is not null ? "freeze_request_pending" : subscription.Status == SubscriptionStatus.Frozen ? "subscription_already_frozen" : approvedFreeze is not null ? "freeze_already_scheduled" : "freeze_not_available_for_status",
            canCancel ? null : cancellationPending ? "subscription_already_cancelled_or_pending" : "cancellation_not_available_for_status",
            canUpgrade ? null : upgradeUnavailableReason,
            policy.StartDelayDays, policy.DefaultDurationDays, policy.ExtendSubscriptionEnd,
            earliestFreezeStartDate,
            $"/api/v1/customer/subscriptions/{subscription.Id}/renewal-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/freeze-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/cancellation-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/upgrade-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/cancellation-quote",
            freezeNoticeAr,
            freezeNoticeEn,
            freezeNoticeAr,
            $"/api/v1/customer/subscriptions/{subscription.Id}/freeze-details");

        return new SubscriptionCommonData(statusNameAr, tierNameAr, tierNameEn, tierLogoUrl, heroImage, totalDays, completedDays, remainingDays, progress, restaurantDto, pendingFreeze, pendingUpgradeDto, actions, activeFreezeDto);
    }

    private async Task<IReadOnlyList<CustomerSubscriptionInvoiceDto>> GetInvoices(Subscription subscription, CancellationToken ct, Guid actorUserId)
    {
        FamilyCheckoutBatchLine? line = null;
        FamilyCheckoutBatch? batch = null;
        var memberAllocation = false;
        var canViewBatchInvoice = true;
        var canViewAnyUpgradeHistory = true;
        var canViewOwnUpgradeHistory = true;
        var subIds = new List<Guid> { subscription.Id };
        if (subscription.FamilyGroupId is Guid familyId)
        {
            line = await _db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
                .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id
                    && x.Batch.FamilyGroupId == familyId && x.Batch.Status == FamilyCheckoutBatchStatus.Paid, ct);
            batch = line?.Batch;
            if (batch?.PaymentAnchorSubscriptionId is Guid anchor) subIds.Add(anchor);
            var isManager = await _db.FamilyGroups.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.Id == familyId && x.ManagerCustomerUserId == actorUserId, ct);
            var familyInvoiceGrant = await (from access in _db.FamilyMemberAccesses.AsNoTracking()
                join member in _db.FamilyMembers.AsNoTracking() on access.FamilyMemberId equals member.Id
                join permissions in _db.FamilyMemberPermissions.AsNoTracking() on member.Id equals permissions.FamilyMemberId
                where member.FamilyGroupId == familyId && access.LinkedCustomerUserId == actorUserId
                    && access.RemovedAtUtc == null && access.HasSeparateAccount && !access.IsPaused && permissions.ViewFamilyInvoices
                select permissions.Id).AnyAsync(ct);
            var historicalPayer = batch?.PayerCustomerUserId == actorUserId && subscription.Status is SubscriptionStatus.Completed or SubscriptionStatus.Expired or SubscriptionStatus.Cancelled or SubscriptionStatus.Upgraded;
            var ownInvoiceGrant = (await _familyAccess.AuthorizeAsync(subscription, actorUserId, FamilyAction.ViewInvoices, ct)).IsSuccess;
            var hasVerifiedUpgradePayment = await (from upgrade in _db.SubscriptionTierUpgrades.AsNoTracking()
                join receipt in _db.PaymentWebhookReceipts.AsNoTracking() on new { upgrade.SubscriptionId, PaymentId = upgrade.PaymentId }
                    equals new { SubscriptionId = receipt.SubscriptionId ?? Guid.Empty, PaymentId = receipt.PaymentId }
                where upgrade.SubscriptionId == subscription.Id && upgrade.PayerCustomerUserId == actorUserId
                    && upgrade.VerifiedPaidAmount.HasValue && upgrade.VerifiedCurrencyCode != null
                    && upgrade.VerifiedPaidAtUtc.HasValue && receipt.VerifiedAtUtc.HasValue
                    && receipt.Amount == upgrade.VerifiedPaidAmount && receipt.CurrencyCode == upgrade.VerifiedCurrencyCode
                    && receipt.Status != "PaymentFailureVerified" && receipt.Status != "VerificationRejected"
                select upgrade.Id).AnyAsync(ct);
            canViewBatchInvoice = isManager || familyInvoiceGrant || historicalPayer || ownInvoiceGrant;
            canViewAnyUpgradeHistory = isManager || familyInvoiceGrant || ownInvoiceGrant;
            canViewOwnUpgradeHistory = canViewAnyUpgradeHistory || historicalPayer || hasVerifiedUpgradePayment;
            memberAllocation = !isManager && !familyInvoiceGrant && !historicalPayer;
        }
        var canViewMemberBaseInvoice = canViewBatchInvoice && (!memberAllocation || line is not null);
        var receipts = canViewMemberBaseInvoice ? await _db.PaymentWebhookReceipts.AsNoTracking()
            .Where(x => x.SubscriptionId.HasValue && subIds.Contains(x.SubscriptionId.Value)
                && x.VerifiedAtUtc.HasValue && x.Status != "PaymentFailureVerified" && x.Status != "VerificationRejected"
                && x.Status != "FinancialReviewRequired" && x.Amount.HasValue && x.CurrencyCode != null && x.CurrencyCode != "")
            .OrderByDescending(x => x.ProcessedAtUtc).ToListAsync(ct) : [];
        if (batch is not null)
        {
            var batchEvidenceIsCoherent = batch.Status == FamilyCheckoutBatchStatus.Paid && batch.PaidAtUtc.HasValue
                && !string.IsNullOrWhiteSpace(batch.GatewayPaymentId) && !string.IsNullOrWhiteSpace(batch.GatewayInvoiceId);
            receipts = batchEvidenceIsCoherent ? receipts.Where(x => x.PaymentId == batch.GatewayPaymentId
                && x.InvoiceId == batch.GatewayInvoiceId && x.Amount == batch.FinalPrice
                && string.Equals(x.CurrencyCode, batch.CurrencyCode, StringComparison.OrdinalIgnoreCase)).ToList() : [];
            if (memberAllocation)
            {
                var allocations = await _db.FamilyCheckoutBatchLines.AsNoTracking()
                    .Where(x => x.BatchId == batch.Id).ToListAsync(ct);
                var allocationIsComplete = allocations.Count > 0 && allocations.All(x => x.NetPaidAmount >= 0m
                    && x.NetPaidAmount == x.PriceBeforeFamilyDiscount - x.FamilyDiscountAllocated
                    && string.Equals(x.CurrencyCode, batch.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                    && allocations.Sum(x => x.NetPaidAmount) == batch.FinalPrice;
                var memberLineIsCoherent = line is not null && line.BatchId == batch.Id
                    && line.SubscriptionId == subscription.Id && line.NetPaidAmount >= 0m
                    && string.Equals(line.CurrencyCode, batch.CurrencyCode, StringComparison.OrdinalIgnoreCase);
                if (!allocationIsComplete || !memberLineIsCoherent) receipts = [];
            }
        }
        var result = receipts.GroupBy(x => x.InvoiceId ?? x.PaymentId ?? x.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()).Select(x => new CustomerSubscriptionInvoiceDto(x.Id, x.InvoiceId, x.PaymentId, x.ProcessedAtUtc,
                memberAllocation ? line!.NetPaidAmount : x.Amount!.Value,
                batch?.CurrencyCode ?? x.CurrencyCode!,
                NormalizeInvoiceStatus(x.Status), x.Gateway, null, false, MessageCodes.Subscriptions.InvoiceDocumentNotAvailable,
                batch?.Id, memberAllocation ? line!.FamilyMemberId : null, batch?.PayerCustomerUserId, memberAllocation,
                IsPaidReviewRequired: x.Status == "FinancialReviewRequired")).ToList();

        if (subscription.FamilyGroupId is not Guid familyGroupId)
            return result;

        var terminal = subscription.Status is SubscriptionStatus.Completed or SubscriptionStatus.Expired
            or SubscriptionStatus.Cancelled or SubscriptionStatus.Upgraded;
        var upgrades = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.PayerCustomerUserId.HasValue
                && x.VerifiedPaidAmount.HasValue && x.VerifiedCurrencyCode != null && x.VerifiedPaidAtUtc.HasValue)
            .OrderByDescending(x => x.VerifiedPaidAtUtc).ToListAsync(ct);
        foreach (var upgrade in upgrades)
        {
            var isActorPayer = upgrade.PayerCustomerUserId == actorUserId;
            if (!canViewAnyUpgradeHistory && !(canViewOwnUpgradeHistory && isActorPayer && terminal)) continue;
            var receipt = await _db.PaymentWebhookReceipts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.SubscriptionId == subscription.Id && x.PaymentId == upgrade.PaymentId
                && x.VerifiedAtUtc.HasValue && x.Amount == upgrade.VerifiedPaidAmount
                && x.CurrencyCode == upgrade.VerifiedCurrencyCode
                && x.Status != "PaymentFailureVerified" && x.Status != "VerificationRejected", ct);
            if (receipt is null) continue;
            if (!canViewAnyUpgradeHistory && !(canViewOwnUpgradeHistory && isActorPayer && terminal)) continue;
            var paidReview = receipt.Status == "FinancialReviewRequired";
            result.Add(new CustomerSubscriptionInvoiceDto(receipt.Id, upgrade.VerifiedInvoiceId, receipt.PaymentId,
                upgrade.VerifiedPaidAtUtc.Value, upgrade.VerifiedPaidAmount.Value, upgrade.VerifiedCurrencyCode!,
                paidReview ? "PAID_REVIEW_REQUIRED" : NormalizeInvoiceStatus(receipt.Status), receipt.Gateway,
                null, false, MessageCodes.Subscriptions.InvoiceDocumentNotAvailable, null, null,
                upgrade.PayerCustomerUserId, false, upgrade.Id, paidReview));
        }
        return result.OrderByDescending(x => x.IssuedAtUtc).ToList();
    }

    private string InvoiceStatusNameAr(string status) => status == "PAID_REVIEW_REQUIRED"
        ? _messages.GetArabicMessage(MessageCodes.Subscriptions.InvoicePaidReviewRequired)
        : SubscriptionLocalization.InvoiceStatusNameAr(status);

    private string InvoiceStatusNameEn(string status) => status == "PAID_REVIEW_REQUIRED"
        ? _messages.GetEnglishMessage(MessageCodes.Subscriptions.InvoicePaidReviewRequired)
        : SubscriptionLocalization.InvoiceStatusNameEn(status);

    private CustomerSubscriptionInvoiceListDto MapInvoice(PaymentWebhookReceipt x, decimal fallbackAmount, string fallbackCurrency)
    {
        var status = NormalizeInvoiceStatus(x.Status);
        return new CustomerSubscriptionInvoiceListDto(
            x.Id,
            x.InvoiceId,
            x.PaymentId,
            x.ProcessedAtUtc,
            x.Amount ?? fallbackAmount,
            x.CurrencyCode ?? fallbackCurrency,
            status,
            SubscriptionLocalization.InvoiceStatusNameAr(status),
            x.Gateway,
            null,
            false,
            MessageCodes.Subscriptions.InvoiceDocumentNotAvailable);
    }

    private static CustomerSubscriptionInvoiceDto MapSingleInvoice(PaymentWebhookReceipt x, decimal fallbackAmount, string fallbackCurrency)
    {
        return new CustomerSubscriptionInvoiceDto(
            x.Id,
            x.InvoiceId,
            x.PaymentId,
            x.ProcessedAtUtc,
            x.Amount ?? fallbackAmount,
            x.CurrencyCode ?? fallbackCurrency,
            NormalizeInvoiceStatus(x.Status),
            x.Gateway,
            null,
            false,
            MessageCodes.Subscriptions.InvoiceDocumentNotAvailable);
    }

    private async Task<CustomerUncompletedSubscriptionDto> MapIncompleteFamilyGroup(
        FamilyGroup group,
        TierLabelsSettings labels,
        CancellationToken ct)
    {
        var planned = group.Members
            .OrderBy(x => x.Role == FamilyMemberRole.Manager ? 0 : 1)
            .FirstOrDefault(x => x.ProgramId.HasValue);
        var programId = planned?.ProgramId ?? Guid.Empty;
        var bundleId = planned?.BundleId ?? Guid.Empty;
        var tier = planned?.SelectedTier ?? RestaurantTier.Basic;
        var durationDays = planned?.DurationDays ?? 0;
        var startDate = planned?.StartDate ?? DateOnly.FromDateTime(_clock.UtcNow);
        var endDate = durationDays > 0 ? startDate.AddDays(durationDays - 1) : startDate;
        var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(tier, labels);

        var programNameAr = string.Empty;
        var programNameEn = string.Empty;
        var bundleNameAr = string.Empty;
        var bundleNameEn = string.Empty;
        var mealsPerDay = 0;
        if (programId != Guid.Empty)
        {
            var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId, ct);
            programNameAr = program?.NameAr ?? string.Empty;
            programNameEn = program?.NameEn ?? string.Empty;
        }
        if (bundleId != Guid.Empty)
        {
            var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bundleId, ct);
            bundleNameAr = bundle?.NameAr ?? string.Empty;
            bundleNameEn = bundle?.NameEn ?? string.Empty;
            mealsPerDay = bundle?.MealsPerDay ?? 0;
        }

        var isDraft = group.Status == FamilyGroupStatus.Draft;
        var holdExpiresAtUtc = FamilyDraftExpiry.ExpiresAtUtc(group);
        var isHoldActive = holdExpiresAtUtc > _clock.UtcNow;
        var holdRemainingSeconds = isHoldActive
            ? Math.Max(0, (int)(holdExpiresAtUtc - _clock.UtcNow).TotalSeconds)
            : (int?)null;
        return new CustomerUncompletedSubscriptionDto(
            group.Id,
            group.Status.ToString(),
            group.Status == FamilyGroupStatus.CheckoutPending ? SubscriptionLocalization.FamilyStatusNameAr(SubscriptionStatus.PendingPayment) : "اشتراك عائلي غير مكتمل",
            programId,
            programNameAr,
            programNameEn,
            bundleId,
            bundleNameAr,
            bundleNameEn,
            tier,
            tierNameAr,
            tierNameEn,
            tierLogoUrl,
            mealsPerDay,
            CatalogImageStorage.ToContentUrl(null),
            startDate,
            endDate,
            durationDays,
            planned?.IncludeFriday ?? false,
            group.QuotedFinalPrice,
            string.IsNullOrWhiteSpace(group.CurrencyCode) ? "KWD" : group.CurrencyCode,
            group.Name,
            group.CreatedAtUtc,
            isHoldActive,
            holdRemainingSeconds,
            holdExpiresAtUtc,
            new CustomerUncompletedActionsDto(
                CanDelete: isDraft,
                CanResume: true,
                DeleteEndpoint: "/api/v1/customer/family-subscription-flow/abandon-draft",
                ResumeEndpoint: "/api/v1/customer/family-subscription-flow/current",
                NextScreen: group.Status == FamilyGroupStatus.CheckoutPending ? "family_payment" : "family_setup",
                PayEndpoint: "/api/v1/customer/family-subscription-flow/checkout",
                RetryPaymentEndpoint: await FamilyCheckoutResumeReader.CanRetryAsync(_db, group.Id, ct)
                    ? FamilyCheckoutResumeReader.RetryEndpoint : null,
                PendingCheckout: await FamilyCheckoutResumeReader.GetAsync(_db, group.Id, _clock.UtcNow, ct)),
            Kind: "FAMILY",
            FamilyGroupId: group.Id);
    }

    private async Task<string?> GetHeroImage(Guid subscriptionId, CancellationToken ct)
    {
        var json = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.ServiceDate).Select(x => x.MealsJson).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<MealSnapshotItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.FirstOrDefault()?.ImageUrl; }
        catch (JsonException) { return null; }
    }

    private static (bool CanUpgrade, string? UnavailableReason) ComputeUpgradeAvailability(Subscription subscription, bool pendingUpgrade)
    {
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return (false, "upgrade_not_available_for_status");
        if (subscription.SelectedTier == RestaurantTier.Elite)
            return (false, "already_elite");
        if (pendingUpgrade)
            return (false, "upgrade_request_pending");
        return (true, null);
    }

    private static (string NameAr, string NameEn, string? LogoUrl) ResolveTierInfo(RestaurantTier tier, TierLabelsSettings labels) => tier switch
    {
        RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn, CatalogImageStorage.ToContentUrl(labels.BasicLogoStorageKey)),
        RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn, CatalogImageStorage.ToContentUrl(labels.PlatinumLogoStorageKey)),
        RestaurantTier.Elite => (labels.EliteAr, labels.EliteEn, CatalogImageStorage.ToContentUrl(labels.EliteLogoStorageKey)),
        _ => (tier.ToString(), tier.ToString(), null),
    };

    // A funded subscription is active for the customer before its first service day.
    // Keep the persisted lifecycle status for scheduling and current-plan selection.
    private static SubscriptionStatus CustomerDisplayStatus(SubscriptionStatus status) =>
        status == SubscriptionStatus.Scheduled ? SubscriptionStatus.Active : status;

    private static string CustomerFacingStatus(SubscriptionStatus status) =>
        status == SubscriptionStatus.SuspendedForReview ? nameof(SubscriptionStatus.Paid) : status.ToString();

    private static string NormalizeInvoiceStatus(string status) => status switch
    {
        "FinancialReviewRequired" => "PAID_REVIEW_REQUIRED",
        "FulfillmentRetryPending" => "PAID_PENDING_FULFILLMENT",
        "PaymentVerified" or "FulfillmentCompleted" or "FulfillmentReviewRequired" or "LatePaymentReview" => "PAID",
        _ => status.ToUpperInvariant(),
    };

    private static bool IsCurrentStatus(string status) =>
        status is nameof(SubscriptionStatus.Active)
            or nameof(SubscriptionStatus.Scheduled)
            or nameof(SubscriptionStatus.Frozen)
            or nameof(SubscriptionStatus.Paid)
            or nameof(SubscriptionStatus.SuspendedForReview);

    private static int CurrentStatusPriority(string status) => status switch
    {
        nameof(SubscriptionStatus.Active) => 0,
        nameof(SubscriptionStatus.Frozen) => 1,
        nameof(SubscriptionStatus.Scheduled) => 2,
        nameof(SubscriptionStatus.Paid) => 3,
        nameof(SubscriptionStatus.SuspendedForReview) => 4,
        _ => 5,
    };

    private sealed record InvoiceReceiptRow(PaymentWebhookReceipt Receipt, decimal FallbackAmount, string FallbackCurrency);
    private sealed record FamilySubscriptionDisplay(
        string Kind,
        Guid? FamilyGroupId,
        int MemberCount,
        string? FamilyName,
        decimal? PaidAmount,
        string StatusNameAr,
        Guid? FamilyMemberId = null,
        string? FamilyMemberName = null,
        string? StatusNameEn = null);
    private sealed record SubscriptionCommonData(
        string StatusNameAr,
        string TierNameAr,
        string TierNameEn,
        string? TierLogoUrl,
        string? ImageUrl,
        int TotalDays,
        int CompletedDays,
        int RemainingDays,
        decimal Progress,
        CustomerFrozenRestaurantDto? Restaurant,
        CustomerPendingFreezeDto? PendingFreeze,
        CustomerPendingUpgradeDto? PendingUpgrade,
        CustomerSubscriptionActionsDto Actions,
        CustomerActiveFreezeDto? ActiveFreeze);
}
