using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
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

    public CustomerSubscriptionQueryService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICapacityHoldExpiryService? holdExpiry = null)
    {
        _db = db;
        _clock = clock;
        _holdExpiry = holdExpiry;
    }

    public async Task<Result<CustomerSubscriptionManagementScreenDto>> GetManagementScreenAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default)
    {
        var subscriptions = await VisibleCustomerSubscriptions(customerUserId, includeHistory, ct);
        var cards = new List<CustomerManagedSubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSubscription(subscription, includeInvoices: true, ct));

        var current = cards
            .Where(x => IsCurrentStatus(x.Status))
            .OrderBy(x => CurrentStatusPriority(x.Status))
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefault();

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
            cards.Add(await MapSlimSubscription(subscription, ct));

        var current = cards
            .Where(x => IsCurrentStatus(x.Status))
            .OrderBy(x => CurrentStatusPriority(x.Status))
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefault();

        return new CustomerCurrentSubscriptionScreenDto(
            current,
            current is null ? MessageCodes.Subscriptions.NoCurrentSubscription : null);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionSlimDto>>> ListSubscriptionsAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default)
    {
        var subscriptions = await VisibleCustomerSubscriptions(customerUserId, includeHistory, ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct));

        return cards;
    }

    public async Task<Result<IReadOnlyList<CustomerUncompletedSubscriptionDto>>> GetUncompletedSubscriptionsAsync(Guid customerUserId, CancellationToken ct = default)
    {
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

        var managerGroupIds = _db.FamilyGroups
            .Where(g => g.ManagerCustomerUserId == customerUserId)
            .Select(g => g.Id);

        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => (x.CustomerUserId == customerUserId || (x.FamilyGroupId != null && managerGroupIds.Contains(x.FamilyGroupId.Value)))
                        && terminalStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var collapsed = await CollapseArchivedSubscriptionsAsync(subscriptions, ct);
        var familyDisplays = await BuildArchivedFamilyDisplaysAsync(collapsed, ct);
        var cards = new List<CustomerSubscriptionSlimDto>(collapsed.Count);
        foreach (var sub in collapsed)
            cards.Add(await MapSlimSubscription(sub, ct, familyDisplays.GetValueOrDefault(sub.Id)));

        return cards;
    }

    public async Task<Result<CustomerManagedSubscriptionDto>> GetManagementDetailsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await OwnedSubscription(subscriptionId, customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        return await MapSubscription(subscription, includeInvoices: true, ct);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceListDto>>> GetInvoicesAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var rows = await InvoiceReceiptQuery(customerUserId).ToListAsync(ct);
        var grouped = rows
            .GroupBy(x => x.Receipt.InvoiceId ?? x.Receipt.PaymentId ?? x.Receipt.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Receipt.ProcessedAtUtc).First())
            .OrderByDescending(x => x.Receipt.ProcessedAtUtc);

        return grouped.Select(x => MapInvoice(x.Receipt, x.FallbackAmount, x.FallbackCurrency)).ToList();
    }

    public async Task<Result<CustomerSubscriptionInvoiceDto>> GetInvoiceDetailAsync(Guid receiptId, Guid customerUserId, CancellationToken ct = default)
    {
        var managerGroupIds = _db.FamilyGroups
            .Where(g => g.ManagerCustomerUserId == customerUserId)
            .Select(g => g.Id);

        var row = await (from r in _db.PaymentWebhookReceipts.AsNoTracking()
                         join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
                         where (s.CustomerUserId == customerUserId || (s.FamilyGroupId != null && managerGroupIds.Contains(s.FamilyGroupId.Value)))
                               && r.Id == receiptId
                               && r.VerifiedAtUtc.HasValue
                               && r.Status != "PaymentFailureVerified"
                               && r.Status != "VerificationRejected"
                         orderby r.ProcessedAtUtc descending
                         select new InvoiceReceiptRow(r, s.PaidAmount.Amount, s.PaidAmount.CurrencyCode))
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return Error.NotFound(MessageCodes.Subscriptions.InvoiceNotFound, "Invoice not found.");

        return MapSingleInvoice(row.Receipt, row.FallbackAmount, row.FallbackCurrency);
    }

    public async Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceDto>>> GetSubscriptionInvoicesAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await OwnedSubscription(subscriptionId, customerUserId, ct);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        return Result.Success(await GetInvoices(subscription, ct));
    }

    public async Task<Result<CustomerDefaultPaymentMethodDto>> GetDefaultPaymentMethodAsync(Guid customerUserId, CancellationToken ct = default)
    {
        var latestGateway = await (from r in _db.PaymentWebhookReceipts.AsNoTracking()
                                   join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
                                   where s.CustomerUserId == customerUserId && r.VerifiedAtUtc.HasValue
                                   orderby r.ProcessedAtUtc descending
                                   select r.Gateway).FirstOrDefaultAsync(ct);

        return new CustomerDefaultPaymentMethodDto(false, null, null, null, latestGateway, MessageCodes.Subscriptions.SavedCardNotAvailable);
    }

    private IQueryable<Subscription> QueryCustomerSubscriptions(Guid customerUserId, bool includeHistory)
    {
        var managerGroupIds = _db.FamilyGroups
            .Where(g => g.ManagerCustomerUserId == customerUserId)
            .Select(g => g.Id);

        var query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == customerUserId || (x.FamilyGroupId != null && managerGroupIds.Contains(x.FamilyGroupId.Value)));

        if (!includeHistory)
            query = query.Where(x => x.Status != SubscriptionStatus.Completed && x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired);

        return query.OrderByDescending(x => x.CreatedAtUtc);
    }

    private async Task<IReadOnlyList<Subscription>> VisibleCustomerSubscriptions(Guid customerUserId, bool includeHistory, CancellationToken ct)
    {
        var subscriptions = await QueryCustomerSubscriptions(customerUserId, includeHistory).ToListAsync(ct);
        return await CollapseFamilyMemberSubscriptionsAsync(subscriptions, ct);
    }

    private async Task<IReadOnlyList<Subscription>> CollapseFamilyMemberSubscriptionsAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        var familyIds = subscriptions
            .Where(x => x.FamilyGroupId is Guid)
            .Select(x => x.FamilyGroupId!.Value)
            .Distinct()
            .ToList();
        if (familyIds.Count == 0)
            return subscriptions;

        var anchors = await _db.FamilyGroups.AsNoTracking()
            .Where(x => familyIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PaymentAnchorSubscriptionId })
            .ToDictionaryAsync(x => x.Id, x => x.PaymentAnchorSubscriptionId, ct);

        var result = new List<Subscription>();
        var seenFamilies = new HashSet<Guid>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.FamilyGroupId is not Guid familyId)
            {
                result.Add(subscription);
                continue;
            }

            if (!seenFamilies.Add(familyId))
                continue;

            var familyRows = subscriptions.Where(x => x.FamilyGroupId == familyId).ToList();
            var anchorId = anchors.GetValueOrDefault(familyId);
            result.Add(familyRows.FirstOrDefault(x => x.Id == anchorId) ?? familyRows[0]);
        }

        return result.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    private async Task<IReadOnlyList<Subscription>> CollapseArchivedSubscriptionsAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        var familyIds = subscriptions
            .Where(x => x.FamilyGroupId is Guid)
            .Select(x => x.FamilyGroupId!.Value)
            .Distinct()
            .ToList();
        if (familyIds.Count == 0)
            return subscriptions;

        var anchors = await _db.FamilyGroups.AsNoTracking()
            .Where(x => familyIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PaymentAnchorSubscriptionId })
            .ToDictionaryAsync(x => x.Id, x => x.PaymentAnchorSubscriptionId, ct);

        var result = new List<Subscription>();
        var seenBatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subscription in subscriptions)
        {
            if (subscription.FamilyGroupId is not Guid familyId)
            {
                result.Add(subscription);
                continue;
            }

            // A payment invoice is assigned to every subscription created by the same family checkout.
            // Historical rows without an invoice cannot be safely batched, so retain each one separately.
            var invoiceId = subscription.PaymentInvoiceId?.Trim();
            var batchKey = string.IsNullOrWhiteSpace(invoiceId)
                ? $"{familyId:N}:subscription:{subscription.Id:N}"
                : $"{familyId:N}:invoice:{invoiceId}";
            if (!seenBatches.Add(batchKey))
                continue;

            var familyBatchRows = string.IsNullOrWhiteSpace(invoiceId)
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
                SubscriptionLocalization.FamilyStatusNameAr(subscription.Status));
        }

        return displays;
    }

    private async Task<FamilySubscriptionDisplay> FamilyDisplayAsync(Subscription subscription, CancellationToken ct)
    {
        if (subscription.FamilyGroupId is not Guid familyId)
            return new FamilySubscriptionDisplay("INDIVIDUAL", null, 1, null, subscription.PaidAmount.Amount, SubscriptionLocalization.StatusNameAr(subscription.Status));

        var group = await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.Id == familyId)
            .Select(x => new { x.Id, x.Name, x.QuotedFinalPrice, MemberCount = x.Members.Count })
            .FirstOrDefaultAsync(ct);
        if (group is null)
            return new FamilySubscriptionDisplay("FAMILY", familyId, 1, null, subscription.PaidAmount.Amount, SubscriptionLocalization.FamilyStatusNameAr(subscription.Status));

        var amount = group.QuotedFinalPrice > 0 ? group.QuotedFinalPrice : subscription.PaidAmount.Amount;
        return new FamilySubscriptionDisplay(
            "FAMILY",
            group.Id,
            Math.Max(group.MemberCount, 1),
            group.Name,
            amount,
            SubscriptionLocalization.FamilyStatusNameAr(subscription.Status));
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

    private async Task<Subscription?> OwnedSubscription(Guid id, Guid customerUserId, CancellationToken ct)
    {
        return await _db.Subscriptions.AsNoTracking().Include(x => x.Program).Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id && (x.CustomerUserId == customerUserId || (x.FamilyGroupId != null && _db.FamilyGroups.Any(g => g.Id == x.FamilyGroupId && g.ManagerCustomerUserId == customerUserId))), ct);
    }

    private async Task<CustomerManagedSubscriptionDto> MapSubscription(Subscription subscription, bool includeInvoices, CancellationToken ct)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var family = await FamilyDisplayAsync(subscription, ct);
        var invoices = includeInvoices ? await GetInvoices(subscription, ct) : Array.Empty<CustomerSubscriptionInvoiceDto>();
        return new CustomerManagedSubscriptionDto(subscription.Id, CustomerFacingStatus(subscription.Status), family.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, family.PaidAmount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.PendingUpgrade, common.Actions, invoices, common.ActiveFreeze,
            family.Kind, family.FamilyGroupId, family.MemberCount, family.FamilyName);
    }

    private async Task<CustomerSubscriptionSlimDto> MapSlimSubscription(
        Subscription subscription, CancellationToken ct, FamilySubscriptionDisplay? preloadedFamily = null)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var family = preloadedFamily ?? await FamilyDisplayAsync(subscription, ct);
        return new CustomerSubscriptionSlimDto(subscription.Id, CustomerFacingStatus(subscription.Status), family.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, family.PaidAmount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.PendingUpgrade, common.Actions, common.ActiveFreeze,
            family.Kind, family.FamilyGroupId, family.MemberCount, family.FamilyName);
    }

    private async Task<SubscriptionCommonData> BuildSubscriptionCommon(Subscription subscription, CancellationToken ct)
    {
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
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Approved && x.FreezeEndDate >= _clock.TodayUtc)
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
        canRenew = canRenew && MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.IsRenewalAvailable(operations, subscription.EndDate, _clock.TodayUtc);
        var freezeUsage = await _db.SubscriptionFreezeRequests.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id &&
            (x.Status == VerificationStatus.Approved || x.Status == VerificationStatus.Submitted)).ToListAsync(ct);
        canFreeze = canFreeze && policy.Enabled && freezeUsage.Count < policy.MaxOccurrences &&
            (policy.MaxTotalDays is null || freezeUsage.Sum(x => x.FreezeDays) + policy.MinDays <= policy.MaxTotalDays);

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);

        var statusNameAr = SubscriptionLocalization.StatusNameAr(subscription.Status);

        var earliestFreezeStartDate = _clock.TodayUtc.AddDays(policy.StartDelayDays);
        var latestLockedServiceDate = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id &&
                        x.ServiceDate >= _clock.TodayUtc &&
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

            var isCurrentlyActive = _clock.TodayUtc >= approvedFreeze.FreezeStartDate && _clock.TodayUtc <= approvedFreeze.FreezeEndDate;
            var isUpcoming = _clock.TodayUtc < approvedFreeze.FreezeStartDate;

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

    private async Task<IReadOnlyList<CustomerSubscriptionInvoiceDto>> GetInvoices(Subscription subscription, CancellationToken ct)
    {
        var subIds = new List<Guid> { subscription.Id };
        if (subscription.FamilyGroupId is Guid familyId)
        {
            var anchorId = await _db.FamilyGroups.AsNoTracking()
                .Where(g => g.Id == familyId)
                .Select(g => g.PaymentAnchorSubscriptionId)
                .FirstOrDefaultAsync(ct);
            if (anchorId.HasValue && !subIds.Contains(anchorId.Value))
                subIds.Add(anchorId.Value);
        }

        var receipts = await _db.PaymentWebhookReceipts.AsNoTracking().Where(x => x.SubscriptionId.HasValue && subIds.Contains(x.SubscriptionId.Value)
                && x.VerifiedAtUtc.HasValue
                && x.Status != "PaymentFailureVerified"
                && x.Status != "VerificationRejected")
            .OrderByDescending(x => x.ProcessedAtUtc).ToListAsync(ct);
        return receipts
            .GroupBy(x => x.InvoiceId ?? x.PaymentId ?? x.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.ProcessedAtUtc).First())
            .OrderByDescending(x => x.ProcessedAtUtc)
            .Select(x => new CustomerSubscriptionInvoiceDto(x.Id, x.InvoiceId, x.PaymentId, x.ProcessedAtUtc,
                x.Amount ?? subscription.PaidAmount.Amount, x.CurrencyCode ?? subscription.PaidAmount.CurrencyCode, NormalizeInvoiceStatus(x.Status), x.Gateway,
                null, false, MessageCodes.Subscriptions.InvoiceDocumentNotAvailable)).ToList();
    }

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
                NextScreen: "family_setup",
                PayEndpoint: "/api/v1/customer/family-subscription-flow/checkout"),
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

    private static string CustomerFacingStatus(SubscriptionStatus status) =>
        status == SubscriptionStatus.SuspendedForReview ? nameof(SubscriptionStatus.Paid) : status.ToString();

    private static string NormalizeInvoiceStatus(string status) => status switch
    {
        "PaymentVerified" or "FulfillmentCompleted" or "FinancialReviewRequired" or "FulfillmentReviewRequired" or "LatePaymentReview" => "PAID",
        "FulfillmentRetryPending" => "PAID_PENDING_FULFILLMENT",
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
        decimal PaidAmount,
        string StatusNameAr);
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

