using System.Text.Json;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>Customer-facing subscription management, lifecycle actions and payment history.</summary>
[Authorize]
[Route("api/v1/customer/subscriptions")]
public sealed class CustomerSubscriptionManagementController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CustomerSubscriptionManagementController(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
        => (_db, _currentUser, _clock) = (db, currentUser, clock);

    /// <summary>Complete data for the subscriptions and invoices screen.</summary>
    [HttpGet("management")]
    public async Task<IActionResult> Management([FromQuery] bool includeHistory = true, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var subscriptions = await QueryCustomerSubscriptions(includeHistory, ct).ToListAsync(ct);
        var cards = new List<CustomerManagedSubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSubscription(subscription, includeInvoices: true, ct));

        var current = cards.Where(x => IsCurrentStatus(x.Status)).OrderBy(x => CurrentStatusPriority(x.Status)).ThenByDescending(x => x.StartDate).FirstOrDefault();
        return Ok(new CustomerSubscriptionManagementScreenDto(current, cards, current?.Invoices ?? [],
            current is null ? "no_current_subscription" : null));
    }

    /// <summary>Current active subscription card for the home/dashboard screen.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var subscriptions = await QueryCustomerSubscriptions(includeHistory: true, ct).ToListAsync(ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct));

        var current = cards.Where(x => IsCurrentStatus(x.Status)).OrderBy(x => CurrentStatusPriority(x.Status)).ThenByDescending(x => x.StartDate).FirstOrDefault();
        return Ok(new CustomerCurrentSubscriptionScreenDto(current, current is null ? "no_current_subscription" : null));
    }

    /// <summary>Slim list of the caller subscriptions, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeHistory = true, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var subscriptions = await QueryCustomerSubscriptions(includeHistory, ct).ToListAsync(ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct));

        return Ok(cards);
    }

    /// <summary>All verified payment receipts for subscriptions owned by the current customer.</summary>
    [HttpGet("/api/v1/customer/invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var rows = await InvoiceReceiptQuery().ToListAsync(ct);
        var grouped = rows
            .GroupBy(x => x.Receipt.InvoiceId ?? x.Receipt.PaymentId ?? x.Receipt.EventKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Receipt.ProcessedAtUtc).First())
            .OrderByDescending(x => x.Receipt.ProcessedAtUtc);

        return Ok(grouped.Select(x => MapInvoice(x.Receipt, x.FallbackAmount, x.FallbackCurrency)).ToList());
    }

    /// <summary>One owned verified payment receipt.</summary>
    [HttpGet("/api/v1/customer/invoices/{receiptId:guid}")]
    public async Task<IActionResult> InvoiceDetail(Guid receiptId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var row = await (from r in _db.PaymentWebhookReceipts.AsNoTracking()
                         join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
                         where s.CustomerUserId == _currentUser.UserId!.Value
                               && r.Id == receiptId
                               && r.VerifiedAtUtc.HasValue
                               && r.Status != "PaymentFailureVerified"
                               && r.Status != "VerificationRejected"
                         orderby r.ProcessedAtUtc descending
                         select new InvoiceReceiptRow(r, s.PaidAmount.Amount, s.PaidAmount.CurrencyCode))
            .FirstOrDefaultAsync(ct);

        if (row is null) return NotFound(new { error = "invoice.not_found" });

        return Ok(MapInvoice(row.Receipt, row.FallbackAmount, row.FallbackCurrency));
    }

    /// <summary>The customer's default payment method summary.</summary>
    [HttpGet("/api/v1/customer/payment-methods/default")]
    public async Task<IActionResult> DefaultPaymentMethod(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var latestGateway = await (from r in _db.PaymentWebhookReceipts.AsNoTracking()
                                   join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
                                   where s.CustomerUserId == _currentUser.UserId!.Value && r.VerifiedAtUtc.HasValue
                                   orderby r.ProcessedAtUtc descending
                                   select r.Gateway).FirstOrDefaultAsync(ct);

        return Ok(new CustomerDefaultPaymentMethodDto(false, null, null, null, latestGateway, "saved_card_not_available"));
    }

    /// <summary>Freeze policy configured by operations.</summary>
    [HttpGet("freeze-policy")]
    public async Task<IActionResult> FreezePolicy(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var policyRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);

        return Ok(new CustomerSubscriptionFreezePolicyDto(
            policy.DefaultDurationDays,
            policy.StartDelayDays,
            policy.ExtendSubscriptionEnd,
            $"يمكن تجميد الاشتراك لمدة حتى {policy.DefaultDurationDays} أيام، ويبدأ التجميد بعد {policy.StartDelayDays} أيام على الأقل.",
            $"You can freeze your subscription for up to {policy.DefaultDurationDays} days. Freezing can start after at least {policy.StartDelayDays} days."));
    }

    /// <summary>Available tier upgrade options for one owned subscription.</summary>
    [HttpGet("{id:guid}/upgrade-options")]
    public async Task<IActionResult> UpgradeOptions(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == id && x.Status == VerificationStatus.Submitted, ct);

        var (canUpgrade, unavailableReason) = ComputeUpgradeAvailability(subscription, pendingUpgrade);
        var options = await BuildUpgradeOptions(subscription, ct);

        return Ok(new CustomerUpgradeOptionsDto(
            canUpgrade,
            unavailableReason,
            subscription.SelectedTier,
            options,
            $"/api/v1/customer/subscriptions/{id}/upgrade-requests"));
    }

    /// <summary>Request a subscription tier upgrade.</summary>
    [HttpPost("{id:guid}/upgrade-requests")]
    public async Task<IActionResult> RequestUpgrade(Guid id, [FromBody] CustomerUpgradeRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        if (subscription.Status != SubscriptionStatus.Active)
            return Conflict(new { error = "subscription.upgrade_not_available" });

        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == id && x.Status == VerificationStatus.Submitted, ct);
        if (pendingUpgrade) return Conflict(new { error = "subscription.upgrade_request_already_pending" });

        var result = await Sender.Send(new RequestSubscriptionUpgradeCommand(id, body.TargetTier, body.Reason), ct);
        return HandleResult(result);
    }

    /// <summary>Management details for one owned subscription.</summary>
    [HttpGet("{id:guid}/management")]
    public async Task<IActionResult> ManagementDetails(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        return subscription is null
            ? NotFound(new { error = "subscription.not_found" })
            : Ok(await MapSubscription(subscription, includeInvoices: true, ct));
    }

    /// <summary>Verified payment receipts belonging to one owned subscription.</summary>
    [HttpGet("{id:guid}/invoices")]
    public async Task<IActionResult> Invoices(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        return Ok(await GetInvoices(subscription, ct));
    }

    /// <summary>Submit a freeze request under the active admin policy.</summary>
    [HttpPost("{id:guid}/freeze-requests")]
    public async Task<IActionResult> RequestFreeze(Guid id, [FromBody] CustomerFreezeRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        var hasPending = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == id && x.Status == VerificationStatus.Submitted, ct);
        if (hasPending) return Conflict(new { error = "subscription.freeze_request_already_pending" });

        var result = await Sender.Send(new RequestSubscriptionFreezeCommand(id, body.StartDate, body.EndDate, body.Reason), ct);
        return HandleResult(result);
    }

    /// <summary>Create an idempotent renewal intent and return the frozen context used to reopen subscription flow.</summary>
    [HttpPost("{id:guid}/renewal-requests")]
    public async Task<IActionResult> RequestRenewal(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        if (subscription.Status is SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled)
            return Conflict(new { error = "subscription.renewal_not_available" });

        var request = await _db.SubscriptionRenewals
            .FirstOrDefaultAsync(x => x.PreviousSubscriptionId == id && x.Status == "Requested", ct);
        if (request is null)
        {
            request = new SubscriptionRenewal
            {
                PreviousSubscriptionId = id,
                Status = "Requested",
                RequestedAtUtc = _clock.UtcNow,
            };
            _db.SubscriptionRenewals.Add(request);
            await _db.SaveChangesAsync(ct);
        }

        return Accepted(new CustomerRenewalRequestDto(request.Id, id, request.Status, subscription.ProgramId, subscription.BundleId,
            subscription.SelectedTier, subscription.DurationDays, subscription.IncludeFriday, subscription.RegionId,
            subscription.EndDate.AddDays(1), "/api/v1/customer/subscription-flow/configuration", "/api/v1/customer/subscriptions/checkout"));
    }

    /// <summary>Record a cancellation request for operations review; it does not cancel service or release capacity automatically.</summary>
    [HttpPost("{id:guid}/cancellation-requests")]
    public async Task<IActionResult> RequestCancellation(Guid id, [FromBody] CustomerCancellationRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen))
            return Conflict(new { error = "subscription.cancellation_not_available" });

        var existing = await _db.SubscriptionOperations.AsNoTracking()
            .Where(x => x.SubscriptionId == id && x.OperationType == "CANCELLATION_REQUESTED")
            .OrderByDescending(x => x.TimestampUtc).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return Accepted(MapCancellation(existing));

        var operation = new SubscriptionOperation
        {
            SubscriptionId = id,
            OperationType = "CANCELLATION_REQUESTED",
            PreviousStatus = subscription.Status,
            NewStatus = subscription.Status,
            Reason = body.Reason?.Trim(),
            PerformedByUserId = _currentUser.UserId!.Value,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new { requestedEffectiveDate = body.RequestedEffectiveDate }),
        };
        _db.SubscriptionOperations.Add(operation);
        await _db.SaveChangesAsync(ct);
        return Accepted(MapCancellation(operation));
    }

    private IQueryable<Subscription> QueryCustomerSubscriptions(bool includeHistory, CancellationToken ct)
    {
        var query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == _currentUser.UserId!.Value);

        if (!includeHistory)
            query = query.Where(x => x.Status != SubscriptionStatus.Completed && x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired);

        return query.OrderByDescending(x => x.CreatedAtUtc);
    }

    private IQueryable<InvoiceReceiptRow> InvoiceReceiptQuery() =>
        from r in _db.PaymentWebhookReceipts.AsNoTracking()
        join s in _db.Subscriptions.AsNoTracking() on r.SubscriptionId equals s.Id
        where s.CustomerUserId == _currentUser.UserId!.Value
              && r.VerifiedAtUtc.HasValue
              && r.Status != "PaymentFailureVerified"
              && r.Status != "VerificationRejected"
        orderby r.ProcessedAtUtc descending
        select new InvoiceReceiptRow(r, s.PaidAmount.Amount, s.PaidAmount.CurrencyCode);

    private async Task<Subscription?> OwnedSubscription(Guid id, CancellationToken ct) =>
        await _db.Subscriptions.AsNoTracking().Include(x => x.Program).Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == _currentUser.UserId!.Value, ct);

    private async Task<CustomerManagedSubscriptionDto> MapSubscription(Subscription subscription, bool includeInvoices, CancellationToken ct)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var invoices = includeInvoices ? await GetInvoices(subscription, ct) : Array.Empty<CustomerSubscriptionInvoiceDto>();
        return new CustomerManagedSubscriptionDto(subscription.Id, subscription.Status.ToString(), common.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.Actions, invoices);
    }

    private async Task<CustomerSubscriptionSlimDto> MapSlimSubscription(Subscription subscription, CancellationToken ct)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        return new CustomerSubscriptionSlimDto(subscription.Id, subscription.Status.ToString(), common.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.Actions);
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
        var policyRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var pendingFreeze = await _db.SubscriptionFreezeRequests.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new CustomerPendingFreezeDto(x.Id, x.FreezeStartDate, x.FreezeEndDate, x.FreezeDays, x.Status)).FirstOrDefaultAsync(ct);
        var cancellationPending = await _db.SubscriptionOperations.AsNoTracking().AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "CANCELLATION_REQUESTED", ct);
        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking().AnyAsync(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted, ct);

        var (canUpgrade, upgradeUnavailableReason) = ComputeUpgradeAvailability(subscription, pendingUpgrade);

        var canFreeze = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled && pendingFreeze is null;
        var canCancel = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen && !cancellationPending;
        var canRenew = subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active or SubscriptionStatus.Frozen or SubscriptionStatus.Completed or SubscriptionStatus.Expired;

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);

        var statusNameAr = StatusNameAr(subscription.Status);

        var actions = new CustomerSubscriptionActionsDto(canRenew, canFreeze, canCancel, canUpgrade,
            canRenew ? null : "renewal_not_available",
            canFreeze ? null : pendingFreeze is null ? "freeze_not_available_for_status" : "freeze_request_pending",
            canCancel ? null : cancellationPending ? "cancellation_request_pending" : "cancellation_not_available_for_status",
            canUpgrade ? null : upgradeUnavailableReason,
            policy.StartDelayDays, policy.DefaultDurationDays, policy.ExtendSubscriptionEnd,
            $"/api/v1/customer/subscriptions/{subscription.Id}/renewal-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/freeze-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/cancellation-requests",
            $"/api/v1/customer/subscriptions/{subscription.Id}/upgrade-requests");

        return new SubscriptionCommonData(statusNameAr, tierNameAr, tierNameEn, tierLogoUrl, heroImage, totalDays, completedDays, remainingDays, progress, restaurantDto, pendingFreeze, actions);
    }

    private async Task<IReadOnlyList<CustomerSubscriptionInvoiceDto>> GetInvoices(Subscription subscription, CancellationToken ct)
    {
        var receipts = await _db.PaymentWebhookReceipts.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
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
                null, false, "invoice_document_not_available")).ToList();
    }

    private async Task<string?> GetHeroImage(Guid subscriptionId, CancellationToken ct)
    {
        var json = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.ServiceDate).Select(x => x.MealsJson).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<MealSnapshotItem>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.FirstOrDefault()?.ImageUrl; }
        catch (JsonException) { return null; }
    }

    private async Task<IReadOnlyList<CustomerUpgradeOptionDto>> BuildUpgradeOptions(Subscription subscription, CancellationToken ct)
    {
        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);

        var today = _clock.TodayUtc;
        var totalDays = Math.Max(1, subscription.DurationDays);
        var remainingDays = Math.Max(1, subscription.EndDate.DayNumber - today.DayNumber);
        var oldPrice = subscription.PaidAmount.Amount;

        var tiers = new[] { RestaurantTier.Basic, RestaurantTier.Platinum, RestaurantTier.Elite };
        return tiers
            .Where(t => t >= subscription.SelectedTier)
            .Select(t =>
            {
                var (nameAr, nameEn, logoUrl) = ResolveTierInfo(t, pricing.TierLabels);
                var baseNewPrice = oldPrice * (t == RestaurantTier.Elite ? 1.30m : 1.15m);
                var proRataDelta = Math.Round((remainingDays / (decimal)totalDays) * (baseNewPrice - oldPrice), 3);
                var priceDifference = t == subscription.SelectedTier ? 0m : proRataDelta;
                return new CustomerUpgradeOptionDto(t, nameAr, nameEn, priceDifference, subscription.PaidAmount.CurrencyCode);
            })
            .ToList();
    }

    private static (bool CanUpgrade, string? UnavailableReason) ComputeUpgradeAvailability(Subscription subscription, bool pendingUpgrade)
    {
        if (subscription.Status != SubscriptionStatus.Active)
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
            InvoiceStatusNameAr(status),
            x.Gateway,
            null,
            false,
            "invoice_document_not_available");
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;
    private static bool IsCurrentStatus(string status) => status is nameof(SubscriptionStatus.PendingPayment) or nameof(SubscriptionStatus.Paid) or nameof(SubscriptionStatus.Scheduled) or nameof(SubscriptionStatus.Active) or nameof(SubscriptionStatus.Frozen) or nameof(SubscriptionStatus.SuspendedForReview);
    private static int CurrentStatusPriority(string status) => status switch
    {
        nameof(SubscriptionStatus.Active) => 0,
        nameof(SubscriptionStatus.Frozen) => 1,
        nameof(SubscriptionStatus.Scheduled) => 2,
        nameof(SubscriptionStatus.Paid) => 3,
        nameof(SubscriptionStatus.SuspendedForReview) => 4,
        _ => 5,
    };
    private static string NormalizeInvoiceStatus(string status) => status switch
    {
        "PaymentVerified" or "FulfillmentCompleted" => "PAID",
        "FulfillmentRetryPending" => "PAID_PENDING_FULFILLMENT",
        "FinancialReviewRequired" or "FulfillmentReviewRequired" => "REVIEW_REQUIRED",
        _ => status.ToUpperInvariant(),
    };
    private static string InvoiceStatusNameAr(string status) => status switch
    {
        "PAID" => "مدفوع",
        "PAID_PENDING_FULFILLMENT" => "مدفوع (قيد التفعيل)",
        "REVIEW_REQUIRED" => "قيد المراجعة",
        _ => status,
    };
    private static string StatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "بانتظار الدفع", SubscriptionStatus.Paid => "مدفوع", SubscriptionStatus.Scheduled => "مجدول",
        SubscriptionStatus.Active => "نشط", SubscriptionStatus.Frozen => "مجمد", SubscriptionStatus.Completed => "مكتمل",
        SubscriptionStatus.Cancelled => "ملغي", SubscriptionStatus.Expired => "منتهي", SubscriptionStatus.PaymentFailed => "فشل الدفع",
        SubscriptionStatus.SuspendedForReview => "معلق للمراجعة", _ => "مسودة",
    };
    private static CustomerCancellationRequestDto MapCancellation(SubscriptionOperation x) => new(x.Id, x.SubscriptionId, "Submitted", x.Reason, x.TimestampUtc);

    private sealed record InvoiceReceiptRow(PaymentWebhookReceipt Receipt, decimal FallbackAmount, string FallbackCurrency);
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
        CustomerSubscriptionActionsDto Actions);
}

public sealed record CustomerSubscriptionManagementScreenDto(CustomerManagedSubscriptionDto? CurrentSubscription, IReadOnlyList<CustomerManagedSubscriptionDto> Subscriptions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices, string? EmptyStateCode);
public sealed record CustomerCurrentSubscriptionScreenDto(CustomerSubscriptionSlimDto? Subscription, string? EmptyStateCode);
public sealed record CustomerManagedSubscriptionDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly SuggestedRenewalStartDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerSubscriptionActionsDto Actions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices);
public sealed record CustomerSubscriptionSlimDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly NextRenewalDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerSubscriptionActionsDto Actions);
public sealed record CustomerFrozenRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl);
public sealed record CustomerPendingFreezeDto(Guid RequestId, DateOnly StartDate, DateOnly EndDate, int FreezeDays, VerificationStatus Status);
public sealed record CustomerSubscriptionActionsDto(bool CanRenew, bool CanFreeze, bool CanCancel, bool CanUpgrade, string? RenewUnavailableReason, string? FreezeUnavailableReason, string? CancelUnavailableReason, string? UpgradeUnavailableReason, int FreezeStartDelayDays, int MaximumFreezeDays, bool FreezeExtendsSubscriptionEnd, string RenewalEndpoint, string FreezeEndpoint, string CancellationEndpoint, string UpgradeEndpoint);
public sealed record CustomerSubscriptionInvoiceDto(Guid ReceiptId, string? InvoiceNumber, string? PaymentId, DateTime IssuedAtUtc, decimal Amount, string CurrencyCode, string Status, string Gateway, string? MaskedCardNumber, bool CanDownload, string? DownloadUnavailableReason);
public sealed record CustomerSubscriptionInvoiceListDto(Guid ReceiptId, string? InvoiceNumber, string? PaymentId, DateTime IssuedAtUtc, decimal Amount, string CurrencyCode, string Status, string StatusNameAr, string Gateway, string? MaskedCardNumber, bool CanDownload, string? DownloadUnavailableReason);
public sealed record CustomerDefaultPaymentMethodDto(bool HasSavedMethod, string? Brand, string? Last4, string? Expiry, string? Gateway, string EmptyStateCode);
public sealed record CustomerSubscriptionFreezePolicyDto(int DefaultDurationDays, int StartDelayDays, bool ExtendSubscriptionEnd, string MessageAr, string MessageEn);
public sealed record CustomerUpgradeOptionsDto(bool CanUpgrade, string? UnavailableReason, RestaurantTier CurrentTier, IReadOnlyList<CustomerUpgradeOptionDto> Options, string UpgradeEndpoint);
public sealed record CustomerUpgradeOptionDto(RestaurantTier TargetTier, string TierNameAr, string TierNameEn, decimal PriceDifference, string CurrencyCode);
public sealed record CustomerUpgradeRequest(RestaurantTier TargetTier, string? Reason);
public sealed record CustomerFreezeRequest(DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record CustomerCancellationRequest(string? Reason, DateOnly? RequestedEffectiveDate);
public sealed record CustomerCancellationRequestDto(Guid RequestId, Guid SubscriptionId, string Status, string? Reason, DateTime RequestedAtUtc);
public sealed record CustomerRenewalRequestDto(Guid RequestId, Guid PreviousSubscriptionId, string Status, Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, bool IncludeFriday, Guid? RegionId, DateOnly SuggestedStartDate, string ConfigurationEndpoint, string CheckoutEndpoint);
