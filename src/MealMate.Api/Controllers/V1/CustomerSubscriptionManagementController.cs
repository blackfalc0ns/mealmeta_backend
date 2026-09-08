using System.Text.Json;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Api.Controllers.V1;

/// <summary>Customer-facing subscription management, lifecycle actions and payment history.</summary>
[Authorize]
[Route("api/v1/customer/subscriptions")]
public sealed class CustomerSubscriptionManagementController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionTierUpgradeQuoteBuilder _upgradeQuotes;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration? _configuration;
    private readonly IWebHostEnvironment? _environment;
    private readonly ISubscriptionPaymentProcessor? _paymentProcessor;
    private readonly MealMate.Application.Features.ProgramsSubscriptions.Pricing.IRestaurantTierClassificationService? _classification;
    private readonly IRestaurantDiversificationCalculator? _diversification;
    private readonly ICapacityHoldExpiryService? _holdExpiry;

    public CustomerSubscriptionManagementController(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IMyFatoorahPaymentService payments,
        UserManager<ApplicationUser> users,
        IConfiguration? configuration = null,
        IWebHostEnvironment? environment = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null,
        MealMate.Application.Features.ProgramsSubscriptions.Pricing.IRestaurantTierClassificationService? classification = null,
        IRestaurantDiversificationCalculator? diversification = null,
        ICapacityHoldExpiryService? holdExpiry = null)
        => (_db, _currentUser, _clock, _upgradeQuotes, _payments, _users, _configuration, _environment, _paymentProcessor, _classification, _diversification, _holdExpiry)
            = (db, currentUser, clock, upgradeQuotes, payments, users, configuration, environment, paymentProcessor, classification, diversification, holdExpiry);

    /// <summary>Chooser when the customer already has an individual subscription.</summary>
    [HttpGet("current/conversion-options")]
    [ProducesResponseType(typeof(SubscriptionConversionOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConversionOptions(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetSubscriptionConversionOptionsQuery(), ct));
    }

    /// <summary>Complete data for the subscriptions and invoices screen.</summary>
    [HttpGet("management")]
    public async Task<IActionResult> Management([FromQuery] bool includeHistory = true, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var subscriptions = await VisibleCustomerSubscriptions(includeHistory, ct);
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

        var subscriptions = await VisibleCustomerSubscriptions(includeHistory: true, ct);
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

        var subscriptions = await VisibleCustomerSubscriptions(includeHistory, ct);
        var cards = new List<CustomerSubscriptionSlimDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSlimSubscription(subscription, ct));

        return Ok(cards);
    }

    /// <summary>Uncompleted or draft subscriptions owned by current customer.</summary>
    [HttpGet("uncompleted")]
    [HttpGet("archived")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerUncompletedSubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UncompletedSubscriptions(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var uncompletedStatuses = new[]
        {
            SubscriptionStatus.Draft,
            SubscriptionStatus.PendingPayment,
            SubscriptionStatus.PaymentFailed
        };

        if (_holdExpiry is not null)
            await _holdExpiry.ExpireDueAsync(ct);
        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, ct, _currentUser.UserId);

        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == _currentUser.UserId!.Value
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
                StatusNameAr(sub.Status),
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
            .Where(x => x.ManagerCustomerUserId == _currentUser.UserId!.Value
                && (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        foreach (var group in familyDrafts)
            result.Add(await MapIncompleteFamilyGroup(group, pricing.TierLabels, ct));

        return Ok(result.OrderByDescending(x => x.CreatedAtUtc).ToList());
    }

    /// <summary>Delete or cancel an uncompleted/draft subscription, releasing holds and removing blocks.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUncompletedSubscription(Guid id, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == _currentUser.UserId!.Value, ct);

        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        if (subscription.Status is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Expired))
            return Conflict(new { error = "subscription.cannot_delete_active_subscription" });

        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(ct);

        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        var previousStatus = subscription.Status;
        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = id,
            OperationType = "CUSTOMER_CANCELLED_UNCOMPLETED",
            PreviousStatus = previousStatus,
            NewStatus = SubscriptionStatus.Cancelled,
            Reason = "Customer deleted uncompleted subscription",
            PerformedByUserId = _currentUser.UserId!.Value,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return NoContent();
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
        var earliestStart = _clock.TodayUtc.AddDays(policy.StartDelayDays);
        var leadHours = policy.StartDelayDays * 24;

        var noticeAr = $"عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد. طلبات مهلة المطبخ ({leadHours} ساعة القادمة) مؤكدة وسيتم تسليمها كالمعتاد، ويتوقف التوصيل مؤقتاً أثناء فترة التجميد ويُستأنف تلقائياً بعدها.";
        var noticeEn = $"When freezing: Subscription end date will be extended automatically. Orders within kitchen lead window ({leadHours}h) are confirmed and delivered, and deliveries pause during freeze and resume automatically.";

        return Ok(new CustomerSubscriptionFreezePolicyDto(
            policy.DefaultDurationDays,
            policy.StartDelayDays,
            policy.ExtendSubscriptionEnd,
            earliestStart,
            $"يمكن تجميد الاشتراك لمدة حتى {policy.DefaultDurationDays} أيام، ويبدأ التجميد بعد {policy.StartDelayDays} أيام على الأقل من تاريخ {earliestStart:yyyy-MM-dd}.",
            $"You can freeze your subscription for up to {policy.DefaultDurationDays} days. Freezing can start after at least {policy.StartDelayDays} days from {earliestStart:yyyy-MM-dd}.",
            noticeAr,
            noticeEn));
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

        var quoteBundle = await _upgradeQuotes.BuildAsync(subscription, ct);
        var (canUpgrade, unavailableReason) = ComputeUpgradeAvailability(
            subscription, pendingUpgrade, quoteBundle.RemainingServiceDays, quoteBundle.PricingUnavailableReason);

        var options = quoteBundle.Options.Select(MapUpgradeOption).ToList();

        return Ok(new CustomerUpgradeOptionsDto(
            canUpgrade,
            unavailableReason,
            subscription.SelectedTier,
            quoteBundle.RemainingServiceDays,
            quoteBundle.TotalServiceDays,
            options,
            $"/api/v1/customer/subscriptions/{id}/upgrade-requests",
            null));
    }

    /// <summary>Full summary and restaurant comparison for upgrading to a target tier.</summary>
    [HttpGet("{id:guid}/upgrade-summary")]
    public async Task<IActionResult> UpgradeSummary(Guid id, [FromQuery] RestaurantTier targetTier, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        if (!IsUpgradeableStatus(subscription.Status))
            return Conflict(new { error = "subscription.upgrade_not_available" });

        if (targetTier <= subscription.SelectedTier)
            return BadRequest(new { error = "subscription.upgrade_target_invalid" });

        var pendingUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
            .AnyAsync(x => x.SubscriptionId == id && x.Status == VerificationStatus.Submitted, ct);
        if (pendingUpgrade)
            return Conflict(new { error = "subscription.upgrade_request_pending" });

        var quoteBundle = await _upgradeQuotes.BuildAsync(subscription, ct);
        var (canUpgrade, unavailableReason) = ComputeUpgradeAvailability(
            subscription, pendingUpgrade, quoteBundle.RemainingServiceDays, quoteBundle.PricingUnavailableReason);

        if (!canUpgrade)
            return Conflict(new { error = unavailableReason ?? "subscription.upgrade_not_available" });

        var targetQuote = quoteBundle.Options.FirstOrDefault(x => x.TargetTier == targetTier && x.Selectable);
        if (targetQuote is null)
            return Conflict(new { error = "subscription.target_tier_unavailable" });

        var pricingRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (currentTierNameAr, currentTierNameEn, currentTierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);
        var (targetTierNameAr, targetTierNameEn, targetTierLogoUrl) = ResolveTierInfo(targetTier, pricing.TierLabels);

        var classificationService = _classification ?? new MealMate.Application.Features.ProgramsSubscriptions.Pricing.RestaurantTierClassificationService(_db, _clock);
        var classification = await classificationService.ResolveAsync(subscription.ProgramId, subscription.BundleId, eligibleOnly: true, ct);

        var currentRestaurantIds = classification.Assignments
            .Where(a => RestaurantTierAccess.CanSelect(subscription.SelectedTier, a.Tier))
            .Select(a => a.RestaurantId)
            .Distinct()
            .ToList();

        var targetRestaurantIds = classification.Assignments
            .Where(a => RestaurantTierAccess.CanSelect(targetTier, a.Tier))
            .Select(a => a.RestaurantId)
            .Distinct()
            .ToList();

        var newlyAddedRestaurantIds = targetRestaurantIds.Except(currentRestaurantIds).ToList();
        var allRestaurantIds = targetRestaurantIds.Union(currentRestaurantIds).Distinct().ToList();

        var restaurantProfiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(r => allRestaurantIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.TradeNameAr,
                r.TradeNameEn
            })
            .ToDictionaryAsync(r => r.Id, ct);

        var logoDocuments = await _db.Documents.AsNoTracking()
            .Where(x => allRestaurantIds.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var logos = logoDocuments.GroupBy(x => x.OwnerId).ToDictionary(x => x.Key, x => CatalogImageStorage.ToContentUrl(x.First().StoragePath));

        var ratingRows = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => allRestaurantIds.Contains(x.RestaurantId))
            .Select(x => new { x.RestaurantId, x.Score })
            .ToListAsync(ct);
        var ratings = ratingRows.GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => Math.Round((decimal)x.Average(r => r.Score), 1));

        var assignmentTiers = classification.Assignments
            .GroupBy(a => a.RestaurantId)
            .ToDictionary(g => g.Key, g => g.First().Tier);

        CustomerUpgradeRestaurantDto MapRestaurant(Guid restId)
        {
            var nameAr = restaurantProfiles.TryGetValue(restId, out var profile) ? profile.TradeNameAr : "مطعم معتمد";
            var nameEn = profile?.TradeNameEn ?? "Approved Restaurant";
            var logoUrl = logos.GetValueOrDefault(restId);
            var rating = ratings.TryGetValue(restId, out var r) ? (decimal?)r : null;
            var tier = assignmentTiers.GetValueOrDefault(restId, subscription.SelectedTier);

            return new CustomerUpgradeRestaurantDto(
                restId,
                nameAr,
                nameEn,
                logoUrl,
                rating,
                tier);
        }

        var currentTierRestaurants = currentRestaurantIds.Select(MapRestaurant).ToList();
        var targetTierRestaurants = targetRestaurantIds.Select(MapRestaurant).ToList();
        var newlyAddedRestaurants = newlyAddedRestaurantIds.Select(MapRestaurant).ToList();

        // 72 Hours Effective Date Cutoff
        var earliestUpgradeDate = _clock.TodayUtc.AddDays(3);
        var latestLockedServiceDate = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id &&
                        x.ServiceDate >= _clock.TodayUtc &&
                        (x.Status == SubscriptionDayStatus.Locked72h ||
                         x.Status == SubscriptionDayStatus.SentToRestaurant ||
                         x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
                         x.Status == SubscriptionDayStatus.Preparing))
            .MaxAsync(x => (DateOnly?)x.ServiceDate, ct);

        if (latestLockedServiceDate.HasValue && latestLockedServiceDate.Value >= earliestUpgradeDate)
        {
            earliestUpgradeDate = latestLockedServiceDate.Value.AddDays(1);
        }

        var firstApplicableDay = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.ServiceDate >= earliestUpgradeDate)
            .OrderBy(x => x.ServiceDate)
            .Select(x => (DateOnly?)x.ServiceDate)
            .FirstOrDefaultAsync(ct);

        var effectiveFromDate = firstApplicableDay ?? earliestUpgradeDate;
        var effectiveNoticeAr = $"يبدأ تفعيل التصنيف الجديد على جدول وجباتك اعتباراً من {effectiveFromDate:yyyy-MM-dd} (بعد مهلة الـ 72 ساعة المقررة لتجهيز الطلبات الحالية).";
        var effectiveNoticeEn = $"The new tier will take effect on your meal schedule starting from {effectiveFromDate:yyyy-MM-dd} (after the 72-hour operational preparation cutoff).";

        var diversificationCalculator = _diversification ?? new RestaurantDiversificationCalculator();
        var repetition = diversificationCalculator.CalculateLimit(
            subscription.Id,
            targetQuote.RemainingServiceDays,
            Math.Max(targetRestaurantIds.Count, 1));

        var restaurantRepetition = new RestaurantRepetitionSummaryDto(
            repetition.LimitValue,
            targetRestaurantIds.Count,
            targetQuote.RemainingServiceDays);

        var rawPaymentMethods = await ResolvePaymentMethodsAsync(targetQuote.PriceDifference, ct);
        var paymentMethods = rawPaymentMethods.Select(m => m with
        {
            ServiceCharge = 0m,
            TotalAmount = targetQuote.PriceDifference
        }).ToList();

        var currentTierInfo = new CustomerUpgradeTierInfoDto(
            subscription.SelectedTier,
            currentTierNameAr,
            currentTierNameEn,
            currentTierLogoUrl,
            currentTierRestaurants.Count);

        var targetTierInfo = new CustomerUpgradeTierInfoDto(
            targetTier,
            targetTierNameAr,
            targetTierNameEn,
            targetTierLogoUrl,
            targetTierRestaurants.Count);

        return Ok(new CustomerUpgradeSummaryDto(
            subscription.Id,
            currentTierInfo,
            targetTierInfo,
            currentTierRestaurants,
            targetTierRestaurants,
            newlyAddedRestaurants,
            effectiveFromDate,
            effectiveNoticeAr,
            effectiveNoticeEn,
            restaurantRepetition,
            targetQuote.CurrentRemainingCredit,
            targetQuote.TargetRemainingPrice,
            targetQuote.PriceDifference,
            targetQuote.CurrencyCode,
            targetQuote.RemainingServiceDays,
            targetQuote.TotalServiceDays,
            paymentMethods,
            $"/api/v1/customer/subscriptions/{id}/upgrade-requests"));
    }

    /// <summary>Available payment methods for subscription upgrade.</summary>
    [HttpGet("{id:guid}/upgrade-payment-methods")]
    [HttpGet("{id:guid}/payment-methods")]
    public async Task<IActionResult> UpgradePaymentMethods(Guid id, [FromQuery] decimal? amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        var invoiceAmount = amount.HasValue && amount.Value > 0
            ? amount.Value
            : 1m;

        var methods = await ResolvePaymentMethodsAsync(invoiceAmount, ct);
        return Ok(methods);
    }

    /// <summary>Request a subscription tier upgrade and start MyFatoorah payment when due.</summary>
    [HttpPost("{id:guid}/upgrade-requests")]
    public async Task<IActionResult> RequestUpgrade(Guid id, [FromBody] CustomerUpgradeRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (_currentUser.UserId is not { } userId) return Unauthorized();

        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return Conflict(new { error = "subscription.upgrade_not_available" });

        if (body.TargetTier <= subscription.SelectedTier)
            return BadRequest(new { error = "subscription.upgrade_target_invalid" });

        var pendingUpgrade = await _db.SubscriptionTierUpgrades
            .FirstOrDefaultAsync(x => x.SubscriptionId == id && x.Status == VerificationStatus.Submitted, ct);
        if (pendingUpgrade is not null)
            return Conflict(new { error = "subscription.upgrade_request_already_pending" });

        var option = await _upgradeQuotes.BuildForTargetAsync(subscription, body.TargetTier, ct);
        if (option is null)
            return Conflict(new { error = "subscription.upgrade_not_available" });

        var tracked = await _db.Subscriptions.FirstAsync(x => x.Id == id, ct);
        var currency = tracked.PaidAmount.CurrencyCode;
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = tracked.Id,
            FromTier = tracked.SelectedTier,
            ToTier = body.TargetTier,
            PriceDifference = new Money(option.PriceDifference, currency),
            QuotedRemainingDays = option.RemainingServiceDays,
            QuotedTotalDue = new Money(option.PriceDifference, currency),
            QuoteJson = SubscriptionTierUpgradeQuoteBuilder.SerializeQuoteJson(option),
            Status = VerificationStatus.Submitted,
        };
        _db.SubscriptionTierUpgrades.Add(upgrade);
        await _db.SaveChangesAsync(ct);

        if (!option.PriceDifference.Equals(0m) && option.PriceDifference > 0m)
        {
            if (string.IsNullOrWhiteSpace(body.CallbackUrl) || string.IsNullOrWhiteSpace(body.ErrorUrl))
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return BadRequest(new { error = "upgrade.redirect_urls_required" });
            }

            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return BadRequest(new { error = "upgrade.customer_not_found" });
            }

            string paymentUrl;
            try
            {
                paymentUrl = await _payments.ExecutePaymentAsync(
                    option.PriceDifference,
                    user.FullName ?? "MealMate Customer",
                    user.Email ?? "",
                    user.PhoneNumber ?? "",
                    body.CallbackUrl,
                    body.ErrorUrl,
                    tracked.Id.ToString(),
                    body.PaymentMethodId <= 0 ? 1 : body.PaymentMethodId,
                    ct);
            }
            catch (Exception ex)
            {
                upgrade.Status = VerificationStatus.Rejected;
                await _db.SaveChangesAsync(ct);
                return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: ex.Message);
            }

            paymentUrl = (paymentUrl ?? string.Empty).Trim();
            upgrade.PaymentId = PaymentIdFromUrl(paymentUrl);
            await _db.SaveChangesAsync(ct);

            return StatusCode(StatusCodes.Status201Created, new CustomerUpgradeStartedDto(
                upgrade.Id,
                tracked.Id,
                upgrade.FromTier,
                upgrade.ToTier,
                option.PriceDifference,
                currency,
                paymentUrl,
                false,
                upgrade.Status,
                option.RemainingServiceDays));
        }

        // Zero-due upgrade: apply immediately.
        tracked.SelectedTier = body.TargetTier;
        tracked.LastOperationAtUtc = _clock.UtcNow;
        upgrade.Status = VerificationStatus.Approved;
        upgrade.EffectiveAtUtc = _clock.UtcNow;
        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = tracked.Id,
            OperationType = "UPGRADE_PAID",
            PreviousStatus = tracked.Status,
            NewStatus = tracked.Status,
            Reason = $"Tier upgraded from {upgrade.FromTier} to {upgrade.ToTier} with zero due",
            PerformedByUserId = userId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new CustomerUpgradeStartedDto(
            upgrade.Id,
            tracked.Id,
            upgrade.FromTier,
            upgrade.ToTier,
            0m,
            currency,
            null,
            true,
            upgrade.Status,
            option.RemainingServiceDays));
    }

    /// <summary>Status of one owned upgrade request (for return from payment gateway).</summary>
    [HttpGet("{id:guid}/upgrade-requests/{upgradeId:guid}")]
    public async Task<IActionResult> UpgradeRequestStatus(Guid id, Guid upgradeId, CancellationToken ct = default, [FromQuery] string? paymentId = null)
    {
        if (!IsCustomer()) return Forbid();

        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        var upgrade = await _db.SubscriptionTierUpgrades
            .FirstOrDefaultAsync(x => x.Id == upgradeId && x.SubscriptionId == id, ct);
        if (upgrade is null) return NotFound(new { error = "upgrade.not_found" });

        if (upgrade.Status == VerificationStatus.Submitted && _paymentProcessor is not null)
        {
            await SyncPendingUpgradePaymentAsync(subscription, upgrade, paymentId, ct);
            upgrade = await _db.SubscriptionTierUpgrades.AsNoTracking()
                .FirstAsync(x => x.Id == upgradeId && x.SubscriptionId == id, ct);
        }

        var isPaid = upgrade.Status == VerificationStatus.Approved;
        var isFailed = upgrade.Status is VerificationStatus.Rejected or VerificationStatus.Expired;
        return Ok(new CustomerUpgradeStatusDto(
            upgrade.Id,
            upgrade.SubscriptionId,
            upgrade.FromTier,
            upgrade.ToTier,
            upgrade.QuotedTotalDue.Amount,
            upgrade.QuotedTotalDue.CurrencyCode,
            upgrade.Status,
            isPaid,
            isFailed,
            upgrade.EffectiveAtUtc,
            null));
    }

    private async Task SyncPendingUpgradePaymentAsync(Subscription subscription, SubscriptionTierUpgrade upgrade, string? paymentId, CancellationToken ct)
    {
        var key = paymentId ?? upgrade.PaymentId;
        var keyType = "PaymentId";
        if (string.IsNullOrWhiteSpace(key))
        {
            key = subscription.Id.ToString();
            keyType = "CustomerReference";
        }

        var gateway = await _payments.GetPaymentStatusAsync(key, keyType, ct);
        if (gateway is null) return;
        var resolvedPaymentId = gateway.PaymentId ?? paymentId ?? upgrade.PaymentId;
        if (string.IsNullOrWhiteSpace(resolvedPaymentId)) return;

        var paid = gateway.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase);
        var failed = gateway.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        if (paid)
        {
            await _paymentProcessor!.VerifyAndProcessAsync(subscription.Id, resolvedPaymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CurrencyCode, gateway.CustomerReference, cancellationToken: ct);
        }
        else if (failed)
        {
            await _paymentProcessor!.VerifyAndReleaseFailedAsync(subscription.Id, resolvedPaymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CustomerReference, cancellationToken: ct);
        }
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

    /// <summary>Full freeze details, active dates, compensation days, and history for the current customer subscription.</summary>
    [HttpGet("current/freeze-details")]
    public async Task<IActionResult> CurrentFreezeDetails(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await ResolveCurrentSubscription(ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        return Ok(await BuildFreezeDetails(subscription, ct));
    }

    /// <summary>Full freeze details, active dates, compensation days, and history for a specific owned subscription.</summary>
    [HttpGet("{id:guid}/freeze-details")]
    public async Task<IActionResult> FreezeDetails(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });

        return Ok(await BuildFreezeDetails(subscription, ct));
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

        var result = await Sender.Send(new RequestSubscriptionFreezeCommand(id, body.StartDate, body.EndDate, body.Reason, body.AcknowledgeLostSelections), ct);
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

    /// <summary>Get a quote / preview of the cancellation refund and effective dates prior to confirming cancellation.</summary>
    [HttpGet("{id:guid}/cancellation-quote")]
    public async Task<IActionResult> GetCancellationQuote(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen))
            return Conflict(new { error = "subscription.cancellation_not_available" });

        var (quote, _) = await CalculateCancellationDetails(subscription, ct);
        return Ok(quote);
    }

    /// <summary>Cancel subscription, calculate refund for remaining days after kitchen lock window, cancel remaining days, and update status.</summary>
    [HttpPost("{id:guid}/cancellation-requests")]
    public async Task<IActionResult> RequestCancellation(Guid id, [FromBody] CustomerCancellationRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var tracked = await _db.Subscriptions
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == _currentUser.UserId!.Value, ct);
        if (tracked is null) return NotFound(new { error = "subscription.not_found" });
        if (tracked.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen))
            return Conflict(new { error = "subscription.cancellation_not_available" });

        var (quote, refundableDays) = await CalculateCancellationDetails(tracked, ct);

        // Cancel refundable days in SubscriptionDays
        foreach (var day in refundableDays)
        {
            day.Status = SubscriptionDayStatus.Cancelled;
        }

        var previousStatus = tracked.Status;
        tracked.Status = quote.LockedKitchenDays > 0
            ? SubscriptionStatus.CancellationPending
            : SubscriptionStatus.Cancelled;
        tracked.LastOperationAtUtc = _clock.UtcNow;

        var operation = new SubscriptionOperation
        {
            SubscriptionId = id,
            OperationType = "CANCELLATION_PROCESSED",
            PreviousStatus = previousStatus,
            NewStatus = tracked.Status,
            Reason = body.Reason?.Trim(),
            PerformedByUserId = _currentUser.UserId!.Value,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new
            {
                quote.DailyBoxPrice,
                quote.TotalServiceDays,
                quote.DeliveredDays,
                quote.LockedKitchenDays,
                quote.RefundableDays,
                quote.RefundAmount,
                quote.CurrencyCode,
                effectiveDate = quote.EffectiveCancellationDate,
                reason = body.Reason?.Trim(),
            }),
        };

        _db.SubscriptionOperations.Add(operation);
        await _db.SaveChangesAsync(ct);

        var policyRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var leadWindowHours = policy.StartDelayDays * 24;

        var statusNameAr = tracked.Status == SubscriptionStatus.Cancelled ? "ملغي" : "إلغاء قيد المتابعة";
        var messageAr = quote.LockedKitchenDays > 0
            ? $"تم تأكيد طلب الإلغاء. سيتم تسليم طلبات مهلة المطبخ ({leadWindowHours} ساعة - {quote.LockedKitchenDays} يوم) واسترداد مبلغ {quote.RefundAmount:0.000} {quote.CurrencyCode} لباقي الأيام ({quote.RefundableDays} يوم) خلال 3-5 أيام عمل."
            : $"تم إلغاء الاشتراك بنجاح. المبلغ المسترد المحسوب هو {quote.RefundAmount:0.000} {quote.CurrencyCode} وسيتم تحويله خلال 3-5 أيام عمل.";

        var result = new CustomerCancellationResultDto(
            operation.Id,
            tracked.Id,
            tracked.Status.ToString(),
            statusNameAr,
            quote.TotalServiceDays,
            quote.DeliveredDays,
            quote.LockedKitchenDays,
            quote.RefundableDays,
            quote.DailyBoxPrice,
            quote.RefundAmount,
            quote.CurrencyCode,
            quote.EffectiveCancellationDate,
            messageAr,
            operation.TimestampUtc);

        return Ok(result);
    }

    private async Task<(CustomerCancellationQuoteDto Quote, List<SubscriptionDay> RefundableDays)> CalculateCancellationDetails(Subscription subscription, CancellationToken ct)
    {
        var days = await _db.SubscriptionDays
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);

        var totalDays = subscription.DurationDays > 0 ? subscription.DurationDays : (days.Count > 0 ? days.Count : 1);
        var dailyBoxPrice = totalDays > 0 ? Math.Round(subscription.PaidAmount.Amount / totalDays, 3) : 0m;

        var today = _clock.TodayUtc;
        var policyRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var leadWindowDays = policy.StartDelayDays;
        var leadWindowEnd = today.AddDays(Math.Max(0, leadWindowDays - 1));
        var leadWindowHours = leadWindowDays * 24;

        var deliveredCount = days.Count(x => x.Status == SubscriptionDayStatus.Delivered);

        var lockedDays = days.Where(x =>
            x.Status != SubscriptionDayStatus.Delivered &&
            x.Status != SubscriptionDayStatus.Cancelled &&
            (x.ServiceDate <= leadWindowEnd ||
             x.Status == SubscriptionDayStatus.Locked72h ||
             x.Status == SubscriptionDayStatus.SentToRestaurant ||
             x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
             x.Status == SubscriptionDayStatus.Preparing ||
             x.Status == SubscriptionDayStatus.OutForDelivery)
        ).ToList();

        var refundableDays = days.Where(x =>
            x.ServiceDate > leadWindowEnd &&
            x.Status != SubscriptionDayStatus.Delivered &&
            x.Status != SubscriptionDayStatus.Cancelled &&
            x.Status != SubscriptionDayStatus.Locked72h &&
            x.Status != SubscriptionDayStatus.SentToRestaurant &&
            x.Status != SubscriptionDayStatus.ConfirmedByRestaurant &&
            x.Status != SubscriptionDayStatus.Preparing &&
            x.Status != SubscriptionDayStatus.OutForDelivery
        ).ToList();

        var refundableDaysCount = refundableDays.Count;
        var refundAmount = Math.Round(refundableDaysCount * dailyBoxPrice, 3);
        var effectiveCancellationDate = lockedDays.Count > 0
            ? lockedDays.Max(x => x.ServiceDate).AddDays(1)
            : today;

        var policyMessageAr = lockedDays.Count > 0
            ? $"طلبات مهلة المطبخ ({leadWindowHours} ساعة - {lockedDays.Count} يوم) مؤكدة مع المطبخ وسيتم توصيلها، ويسترد ثمن باقي الأيام ({refundableDaysCount} يوم) بقيمة {refundAmount:0.000} {subscription.PaidAmount.CurrencyCode}."
            : $"سيتم إلغاء الاشتراك واسترداد مبلغ {refundAmount:0.000} {subscription.PaidAmount.CurrencyCode} بالكامل للأيام المتبقية ({refundableDaysCount} يوم).";

        var quote = new CustomerCancellationQuoteDto(
            subscription.Id,
            totalDays,
            deliveredCount,
            lockedDays.Count,
            refundableDaysCount,
            dailyBoxPrice,
            refundAmount,
            subscription.PaidAmount.CurrencyCode,
            effectiveCancellationDate,
            policyMessageAr);

        return (quote, refundableDays);
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

    private async Task<IReadOnlyList<Subscription>> VisibleCustomerSubscriptions(bool includeHistory, CancellationToken ct)
    {
        var subscriptions = await QueryCustomerSubscriptions(includeHistory, ct).ToListAsync(ct);
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

    private async Task<FamilySubscriptionDisplay> FamilyDisplayAsync(Subscription subscription, CancellationToken ct)
    {
        if (subscription.FamilyGroupId is not Guid familyId)
            return new FamilySubscriptionDisplay("INDIVIDUAL", null, 1, null, subscription.PaidAmount.Amount, StatusNameAr(subscription.Status));

        var group = await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.Id == familyId)
            .Select(x => new { x.Id, x.Name, x.QuotedFinalPrice, MemberCount = x.Members.Count })
            .FirstOrDefaultAsync(ct);
        if (group is null)
            return new FamilySubscriptionDisplay("FAMILY", familyId, 1, null, subscription.PaidAmount.Amount, FamilyStatusNameAr(subscription.Status));

        var amount = group.QuotedFinalPrice > 0 ? group.QuotedFinalPrice : subscription.PaidAmount.Amount;
        return new FamilySubscriptionDisplay(
            "FAMILY",
            group.Id,
            Math.Max(group.MemberCount, 1),
            group.Name,
            amount,
            FamilyStatusNameAr(subscription.Status));
    }

    private static string FamilyStatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "اشتراك عائلي بانتظار الدفع",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "اشتراك عائلي",
        SubscriptionStatus.Scheduled => "اشتراك عائلي مجدول",
        SubscriptionStatus.Active => "اشتراك عائلي",
        SubscriptionStatus.Frozen => "اشتراك عائلي مجمد",
        _ => StatusNameAr(status),
    };

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
        var family = await FamilyDisplayAsync(subscription, ct);
        var invoices = includeInvoices ? await GetInvoices(subscription, ct) : Array.Empty<CustomerSubscriptionInvoiceDto>();
        return new CustomerManagedSubscriptionDto(subscription.Id, CustomerFacingStatus(subscription.Status), family.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, family.PaidAmount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.PendingUpgrade, common.Actions, invoices, common.ActiveFreeze,
            family.Kind, family.FamilyGroupId, family.MemberCount, family.FamilyName);
    }

    private async Task<CustomerSubscriptionSlimDto> MapSlimSubscription(Subscription subscription, CancellationToken ct)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        var family = await FamilyDisplayAsync(subscription, ct);
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
        var policyRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
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

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var pricing = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson);
        var (tierNameAr, tierNameEn, tierLogoUrl) = ResolveTierInfo(subscription.SelectedTier, pricing.TierLabels);

        var statusNameAr = StatusNameAr(subscription.Status);

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

        var freezeNoticeAr = $"عند تجميد الاشتراك: يتم تفعيل التجميد تلقائياً وتمديد تاريخ انتهاء اشتراكك فوراً بدون انتظار موافقة الإدارة بعدد أيام التجميد. طلبات مهلة المطبخ ({policy.StartDelayDays * 24} ساعة القادمة) مؤكدة وسيتم تسليمها، ويتوقف توصيل الوجبات مؤقتاً خلال فترة التجميد ويُستأنف تلقائياً بعدها. في حال كان لديك أيام تم اختيار وجباتها مسبقاً ضمن فترة التجميد، سيتم إلغاء تلك الاختيارات وإضافة أيام تعويضية بديلة في نهاية الاشتراك.";
        var freezeNoticeEn = $"When freezing: Subscription end date will be extended automatically without waiting for admin approval. Orders within the kitchen lead window ({policy.StartDelayDays * 24}h) are confirmed and delivered, and deliveries pause during freeze and resume automatically. If you had already selected meals for days within the freeze period, those selections will be cleared and replaced with new days at the end.";

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

            var summaryAr = $"تم تجميد {frozenDates.Count} يوم خدمة للفترة من {approvedFreeze.FreezeStartDate:yyyy-MM-dd} إلى {approvedFreeze.FreezeEndDate:yyyy-MM-dd}، وتم تعويضها بإضافة {compensationDates.Count} يوم بديل في نهاية الاشتراك حتى {subscription.EndDate:yyyy-MM-dd}.";
            var summaryEn = $"{frozenDates.Count} service days frozen from {approvedFreeze.FreezeStartDate:yyyy-MM-dd} to {approvedFreeze.FreezeEndDate:yyyy-MM-dd}, compensated with {compensationDates.Count} replacement days at subscription end until {subscription.EndDate:yyyy-MM-dd}.";

            activeFreezeDto = new CustomerActiveFreezeDto(
                approvedFreeze.Id,
                approvedFreeze.FreezeStartDate,
                approvedFreeze.FreezeEndDate,
                approvedFreeze.FreezeDays,
                frozenDates.Count,
                "Approved",
                "معتمد ومطبق",
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

    private static CustomerUpgradeOptionDto MapUpgradeOption(SubscriptionTierUpgradeOptionQuote option) =>
        new(
            option.TargetTier,
            option.TierNameAr,
            option.TierNameEn,
            option.LogoUrl,
            option.IsCurrent,
            option.Selectable,
            option.AccessibleRestaurantCount,
            option.CurrentRemainingCredit,
            option.TargetRemainingPrice,
            0m,
            option.PriceDifference,
            option.CurrencyCode);

    private static bool IsUpgradeableStatus(SubscriptionStatus status) =>
        status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled;

    private static (bool CanUpgrade, string? UnavailableReason) ComputeUpgradeAvailability(
        Subscription subscription,
        bool pendingUpgrade,
        int remainingServiceDays,
        string? pricingUnavailableReason)
    {
        if (!IsUpgradeableStatus(subscription.Status))
            return (false, "upgrade_not_available_for_status");
        if (subscription.SelectedTier == RestaurantTier.Elite)
            return (false, "already_elite");
        if (pendingUpgrade)
            return (false, "upgrade_request_pending");
        if (remainingServiceDays <= 0)
            return (false, "upgrade_no_remaining_days");
        if (pricingUnavailableReason is not null)
            return (false, pricingUnavailableReason);
        return (true, null);
    }

    private static (bool CanUpgrade, string? UnavailableReason) ComputeUpgradeAvailability(Subscription subscription, bool pendingUpgrade)
    {
        if (!IsUpgradeableStatus(subscription.Status))
            return (false, "upgrade_not_available_for_status");
        if (subscription.SelectedTier == RestaurantTier.Elite)
            return (false, "already_elite");
        if (pendingUpgrade)
            return (false, "upgrade_request_pending");
        return (true, null);
    }

    private static string? PaymentIdFromUrl(string paymentUrl)
    {
        if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var uri)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            if (part[..separator].Equals("paymentId", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(part[(separator + 1)..]);
        }
        return null;
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
            group.Status == FamilyGroupStatus.CheckoutPending ? "اشتراك عائلي بانتظار الدفع" : "اشتراك عائلي غير مكتمل",
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

    private static string CustomerFacingStatus(SubscriptionStatus status) =>
        status == SubscriptionStatus.SuspendedForReview ? nameof(SubscriptionStatus.Paid) : status.ToString();
    private static string NormalizeInvoiceStatus(string status) => status switch
    {
        "PaymentVerified" or "FulfillmentCompleted" or "FinancialReviewRequired" or "FulfillmentReviewRequired" or "LatePaymentReview" => "PAID",
        "FulfillmentRetryPending" => "PAID_PENDING_FULFILLMENT",
        _ => status.ToUpperInvariant(),
    };
    private static string InvoiceStatusNameAr(string status) => status switch
    {
        "PAID" => "تم الدفع",
        "PAID_PENDING_FULFILLMENT" => "تم الدفع (قيد التفعيل)",
        _ => status,
    };
    private static string StatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "بانتظار الدفع",
        SubscriptionStatus.Paid or SubscriptionStatus.SuspendedForReview => "تم الدفع",
        SubscriptionStatus.Scheduled => "مجدول",
        SubscriptionStatus.Active => "نشط",
        SubscriptionStatus.Frozen => "مجمد",
        SubscriptionStatus.Completed => "مكتمل",
        SubscriptionStatus.Cancelled => "ملغي",
        SubscriptionStatus.CancellationPending => "إلغاء قيد المتابعة",
        SubscriptionStatus.Expired => "منتهي",
        SubscriptionStatus.PaymentFailed => "فشل الدفع",
        _ => "مسودة",
    };
    private static CustomerCancellationRequestDto MapCancellation(SubscriptionOperation x) => new(x.Id, x.SubscriptionId, "Submitted", x.Reason, x.TimestampUtc);

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

    private async Task<Subscription?> ResolveCurrentSubscription(CancellationToken ct)
    {
        var subscriptions = await VisibleCustomerSubscriptions(includeHistory: true, ct);
        return subscriptions
            .Where(x => IsCurrentStatus(CustomerFacingStatus(x.Status)))
            .OrderBy(x => CurrentStatusPriority(CustomerFacingStatus(x.Status)))
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefault();
    }

    private async Task<CustomerSubscriptionFreezeDetailsDto> BuildFreezeDetails(Subscription subscription, CancellationToken ct)
    {
        var freezeRequests = await _db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var days = await _db.SubscriptionDays.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);

        var approvedFreeze = freezeRequests
            .FirstOrDefault(x => x.Status == VerificationStatus.Approved && x.FreezeEndDate >= _clock.TodayUtc);

        var isCurrentlyFrozen = subscription.Status == SubscriptionStatus.Frozen
            || (approvedFreeze is not null && _clock.TodayUtc >= approvedFreeze.FreezeStartDate && _clock.TodayUtc <= approvedFreeze.FreezeEndDate);

        CustomerActiveFreezeDto? currentFreezeDto = null;
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

            var summaryAr = $"تم تجميد {frozenDates.Count} يوم خدمة للفترة من {approvedFreeze.FreezeStartDate:yyyy-MM-dd} إلى {approvedFreeze.FreezeEndDate:yyyy-MM-dd}، وتم تعويضها بإضافة {compensationDates.Count} يوم بديل في نهاية الاشتراك حتى {subscription.EndDate:yyyy-MM-dd}.";
            var summaryEn = $"{frozenDates.Count} service days frozen from {approvedFreeze.FreezeStartDate:yyyy-MM-dd} to {approvedFreeze.FreezeEndDate:yyyy-MM-dd}, compensated with {compensationDates.Count} replacement days at subscription end until {subscription.EndDate:yyyy-MM-dd}.";

            currentFreezeDto = new CustomerActiveFreezeDto(
                approvedFreeze.Id,
                approvedFreeze.FreezeStartDate,
                approvedFreeze.FreezeEndDate,
                approvedFreeze.FreezeDays,
                frozenDates.Count,
                "Approved",
                "معتمد ومطبق",
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

        var history = freezeRequests.Select(x => new CustomerFreezeHistoryItemDto(
            x.Id,
            x.FreezeStartDate,
            x.FreezeEndDate,
            x.FreezeDays,
            x.Status.ToString(),
            x.Status == VerificationStatus.Approved ? "معتمد ومطبق" : x.Status == VerificationStatus.Submitted ? "قيد المراجعة" : "مرفوض",
            x.Reason,
            x.CreatedAtUtc,
            x.ProcessedAtUtc)).ToList();

        var noticeAr = currentFreezeDto is not null
            ? currentFreezeDto.SummaryAr
            : "لا يوجد تجميد نشط حالياً لهذا الاشتراك.";
        var noticeEn = currentFreezeDto is not null
            ? currentFreezeDto.SummaryEn
            : "No active freeze for this subscription.";

        return new CustomerSubscriptionFreezeDetailsDto(
            subscription.Id,
            CustomerFacingStatus(subscription.Status),
            StatusNameAr(subscription.Status),
            approvedFreeze is not null,
            isCurrentlyFrozen,
            currentFreezeDto,
            history,
            noticeAr,
            noticeEn);
    }

    private async Task<IReadOnlyList<CustomerPaymentMethodDto>> ResolvePaymentMethodsAsync(decimal invoiceAmount, CancellationToken ct)
    {
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);

        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(invoiceAmount, ct);
        }
        catch
        {
            if (_configuration is not null && _environment is not null && DevPaymentMethods.IsMockEnabled(_configuration, _environment))
                return DevPaymentMethods.BuildMock(invoiceAmount);
            return [];
        }

        var result = methods
            .Where(x => config is null || IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(
                x.PaymentMethodId,
                x.Code,
                x.NameAr,
                x.NameEn,
                "MYFATOORAH_HOSTED",
                x.ServiceCharge,
                x.TotalAmount,
                x.CurrencyCode,
                true,
                null,
                x.ImageUrl))
            .ToList();

        if (result.Count == 0 && _configuration is not null && _environment is not null && DevPaymentMethods.IsMockEnabled(_configuration, _environment))
            return DevPaymentMethods.BuildMock(invoiceAmount);

        return result;
    }

    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = (method.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (method.NameEn ?? string.Empty).Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY") || name.Contains("APPLEPAY")) return apple;
        if (code == "VM" || name.Contains("VISA") || name.Contains("MASTER")) return card;
        return true;
    }
}

public sealed record CustomerSubscriptionManagementScreenDto(CustomerManagedSubscriptionDto? CurrentSubscription, IReadOnlyList<CustomerManagedSubscriptionDto> Subscriptions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices, string? EmptyStateCode);
public sealed record CustomerCurrentSubscriptionScreenDto(CustomerSubscriptionSlimDto? Subscription, string? EmptyStateCode);
public sealed record CustomerManagedSubscriptionDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly SuggestedRenewalStartDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerPendingUpgradeDto? PendingUpgrade, CustomerSubscriptionActionsDto Actions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices, CustomerActiveFreezeDto? ActiveFreeze = null, string Kind = "INDIVIDUAL", Guid? FamilyGroupId = null, int MemberCount = 1, string? FamilyName = null);
public sealed record CustomerSubscriptionSlimDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly NextRenewalDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerPendingUpgradeDto? PendingUpgrade, CustomerSubscriptionActionsDto Actions, CustomerActiveFreezeDto? ActiveFreeze = null, string Kind = "INDIVIDUAL", Guid? FamilyGroupId = null, int MemberCount = 1, string? FamilyName = null);
public sealed record CustomerFrozenRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl);
public sealed record CustomerPendingFreezeDto(Guid RequestId, DateOnly StartDate, DateOnly EndDate, int FreezeDays, VerificationStatus Status);
public sealed record CustomerPendingUpgradeDto(Guid RequestId, RestaurantTier FromTier, RestaurantTier ToTier, decimal PriceDifference, VerificationStatus Status);

public sealed record CustomerActiveFreezeDto(
    Guid RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    int FreezeDays,
    int FrozenServiceDaysCount,
    string Status,
    string StatusNameAr,
    bool IsCurrentlyActive,
    bool IsUpcoming,
    string? Reason,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc,
    IReadOnlyList<DateOnly> FrozenServiceDates,
    IReadOnlyList<DateOnly> CompensationDates,
    DateOnly SubscriptionEndDate,
    string SummaryAr,
    string SummaryEn);

public sealed record CustomerFreezeHistoryItemDto(
    Guid RequestId,
    DateOnly StartDate,
    DateOnly EndDate,
    int FreezeDays,
    string Status,
    string StatusNameAr,
    string? Reason,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc);

public sealed record CustomerSubscriptionFreezeDetailsDto(
    Guid SubscriptionId,
    string SubscriptionStatus,
    string SubscriptionStatusNameAr,
    bool HasFreeze,
    bool IsCurrentlyFrozen,
    CustomerActiveFreezeDto? CurrentFreeze,
    IReadOnlyList<CustomerFreezeHistoryItemDto> FreezeHistory,
    string NoticeAr,
    string NoticeEn);

public sealed record CustomerSubscriptionActionsDto(
    bool CanRenew,
    bool CanFreeze,
    bool CanCancel,
    bool CanUpgrade,
    string? RenewUnavailableReason,
    string? FreezeUnavailableReason,
    string? CancelUnavailableReason,
    string? UpgradeUnavailableReason,
    int FreezeStartDelayDays,
    int MaximumFreezeDays,
    bool FreezeExtendsSubscriptionEnd,
    DateOnly EarliestFreezeStartDate,
    string RenewalEndpoint,
    string FreezeEndpoint,
    string CancellationEndpoint,
    string UpgradeEndpoint,
    string? CancellationQuoteEndpoint = null,
    string? FreezeNoticeAr = null,
    string? FreezeNoticeEn = null,
    string? FreezeNotice = null,
    string? FreezeDetailsEndpoint = null);

public sealed record CustomerCancellationQuoteDto(
    Guid SubscriptionId,
    int TotalServiceDays,
    int DeliveredDays,
    int LockedKitchenDays,
    int RefundableDays,
    decimal DailyBoxPrice,
    decimal RefundAmount,
    string CurrencyCode,
    DateOnly EffectiveCancellationDate,
    string PolicyMessageAr);

public sealed record CustomerCancellationResultDto(
    Guid RequestId,
    Guid SubscriptionId,
    string Status,
    string StatusNameAr,
    int TotalServiceDays,
    int DeliveredDays,
    int LockedKitchenDays,
    int RefundableDays,
    decimal DailyBoxPrice,
    decimal RefundAmount,
    string CurrencyCode,
    DateOnly? EffectiveCancellationDate,
    string MessageAr,
    DateTime TimestampUtc);
public sealed record CustomerSubscriptionInvoiceDto(Guid ReceiptId, string? InvoiceNumber, string? PaymentId, DateTime IssuedAtUtc, decimal Amount, string CurrencyCode, string Status, string Gateway, string? MaskedCardNumber, bool CanDownload, string? DownloadUnavailableReason);
public sealed record CustomerSubscriptionInvoiceListDto(Guid ReceiptId, string? InvoiceNumber, string? PaymentId, DateTime IssuedAtUtc, decimal Amount, string CurrencyCode, string Status, string StatusNameAr, string Gateway, string? MaskedCardNumber, bool CanDownload, string? DownloadUnavailableReason);
public sealed record CustomerDefaultPaymentMethodDto(bool HasSavedMethod, string? Brand, string? Last4, string? Expiry, string? Gateway, string EmptyStateCode);
public sealed record CustomerSubscriptionFreezePolicyDto(int DefaultDurationDays, int StartDelayDays, bool ExtendSubscriptionEnd, DateOnly EarliestAllowedStartDate, string MessageAr, string MessageEn, string? NoticeAr = null, string? NoticeEn = null);
public sealed record CustomerUpgradeOptionsDto(
    bool CanUpgrade,
    string? UnavailableReason,
    RestaurantTier CurrentTier,
    int RemainingServiceDays,
    int TotalServiceDays,
    IReadOnlyList<CustomerUpgradeOptionDto> Options,
    string UpgradeEndpoint,
    IReadOnlyList<CustomerPaymentMethodDto>? PaymentMethods = null);

public sealed record CustomerUpgradeTierInfoDto(
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    string? LogoUrl,
    int AccessibleRestaurantCount);

public sealed record CustomerUpgradeRestaurantDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    decimal? Rating,
    RestaurantTier Tier);

public sealed record CustomerUpgradeSummaryDto(
    Guid SubscriptionId,
    CustomerUpgradeTierInfoDto CurrentTier,
    CustomerUpgradeTierInfoDto TargetTier,
    IReadOnlyList<CustomerUpgradeRestaurantDto> CurrentTierRestaurants,
    IReadOnlyList<CustomerUpgradeRestaurantDto> TargetTierRestaurants,
    IReadOnlyList<CustomerUpgradeRestaurantDto> NewlyAddedRestaurants,
    DateOnly EffectiveFromDate,
    string EffectiveNoticeAr,
    string EffectiveNoticeEn,
    RestaurantRepetitionSummaryDto RestaurantRepetition,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal PriceDifference,
    string CurrencyCode,
    int RemainingServiceDays,
    int TotalServiceDays,
    IReadOnlyList<CustomerPaymentMethodDto> PaymentMethods,
    string UpgradeEndpoint);

public sealed record CustomerUpgradeOptionDto(
    RestaurantTier TargetTier,
    string TierNameAr,
    string TierNameEn,
    string? LogoUrl,
    bool IsCurrent,
    bool Selectable,
    int AccessibleRestaurantCount,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal GatewayRecovery,
    decimal PriceDifference,
    string CurrencyCode);

public sealed record CustomerUpgradeRequest(
    RestaurantTier TargetTier,
    string CallbackUrl,
    string ErrorUrl,
    int PaymentMethodId = 1,
    string? Reason = null);

public sealed record CustomerUpgradeStartedDto(
    Guid UpgradeId,
    Guid SubscriptionId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal Amount,
    string CurrencyCode,
    string? PaymentUrl,
    bool IsPaid,
    VerificationStatus Status,
    int RemainingServiceDays);

public sealed record CustomerUpgradeStatusDto(
    Guid UpgradeId,
    Guid SubscriptionId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal Amount,
    string CurrencyCode,
    VerificationStatus Status,
    bool IsPaid,
    bool IsFailed,
    DateTime? EffectiveAtUtc,
    string? PaymentUrl);
public sealed record CustomerFreezeRequest(DateOnly StartDate, DateOnly EndDate, string? Reason, bool AcknowledgeLostSelections = false);
public sealed record CustomerCancellationRequest(string? Reason, DateOnly? RequestedEffectiveDate);
public sealed record CustomerCancellationRequestDto(Guid RequestId, Guid SubscriptionId, string Status, string? Reason, DateTime RequestedAtUtc);
public sealed record CustomerRenewalRequestDto(Guid RequestId, Guid PreviousSubscriptionId, string Status, Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, bool IncludeFriday, Guid? RegionId, DateOnly SuggestedStartDate, string ConfigurationEndpoint, string CheckoutEndpoint);

public sealed record CustomerUncompletedSubscriptionDto(
    Guid SubscriptionId,
    string Status,
    string StatusNameAr,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    string? TierLogoUrl,
    int MealsPerDay,
    string? ImageUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    int DurationDays,
    bool IncludeFriday,
    decimal Amount,
    string CurrencyCode,
    string? DeliveryAddress,
    DateTime CreatedAtUtc,
    bool IsHoldActive,
    int? HoldRemainingSeconds,
    DateTime? HoldExpiresAtUtc,
    CustomerUncompletedActionsDto Actions,
    string Kind = "INDIVIDUAL",
    Guid? FamilyGroupId = null);

public sealed record CustomerUncompletedActionsDto(
    bool CanDelete,
    bool CanResume,
    string DeleteEndpoint,
    string ResumeEndpoint,
    string NextScreen,
    string PayEndpoint);
