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

    public CustomerSubscriptionManagementController(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IMyFatoorahPaymentService payments,
        UserManager<ApplicationUser> users,
        IConfiguration? configuration = null,
        IWebHostEnvironment? environment = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null)
        => (_db, _currentUser, _clock, _upgradeQuotes, _payments, _users, _configuration, _environment, _paymentProcessor)
            = (db, currentUser, clock, upgradeQuotes, payments, users, configuration, environment, paymentProcessor);

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

        var firstSelectable = options.FirstOrDefault(x => x.Selectable && x.PriceDifference > 0);
        var invoiceAmount = firstSelectable?.PriceDifference ?? 1m;
        var paymentMethods = await ResolvePaymentMethodsAsync(invoiceAmount, ct);

        return Ok(new CustomerUpgradeOptionsDto(
            canUpgrade,
            unavailableReason,
            subscription.SelectedTier,
            quoteBundle.RemainingServiceDays,
            quoteBundle.TotalServiceDays,
            options,
            $"/api/v1/customer/subscriptions/{id}/upgrade-requests",
            paymentMethods));
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
        return new CustomerManagedSubscriptionDto(subscription.Id, CustomerFacingStatus(subscription.Status), common.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.PendingUpgrade, common.Actions, invoices);
    }

    private async Task<CustomerSubscriptionSlimDto> MapSlimSubscription(Subscription subscription, CancellationToken ct)
    {
        var common = await BuildSubscriptionCommon(subscription, ct);
        return new CustomerSubscriptionSlimDto(subscription.Id, CustomerFacingStatus(subscription.Status), common.StatusNameAr, subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, common.TierNameAr, common.TierNameEn, common.TierLogoUrl, subscription.Bundle.MealsPerDay, common.ImageUrl, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            common.TotalDays, common.CompletedDays, common.RemainingDays, common.Progress, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode,
            common.Restaurant, common.PendingFreeze, common.PendingUpgrade, common.Actions);
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
        var pendingFreeze = await _db.SubscriptionFreezeRequests.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id && (x.Status == VerificationStatus.Submitted || (x.Status == VerificationStatus.Approved && x.FreezeEndDate >= _clock.TodayUtc)))
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new CustomerPendingFreezeDto(x.Id, x.FreezeStartDate, x.FreezeEndDate, x.FreezeDays, x.Status)).FirstOrDefaultAsync(ct);
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

        var canFreeze = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled && pendingFreeze is null && !cancellationPending;
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

        var freezeNoticeAr = $"عند تجميد الاشتراك: سيتم تمديد تاريخ انتهاء اشتراكك تلقائياً بعدد أيام التجميد. طلبات مهلة المطبخ ({policy.StartDelayDays * 24} ساعة القادمة) مؤكدة وسيتم تسليمها، ويتوقف توصيل الوجبات مؤقتاً خلال فترة التجميد ويُستأنف تلقائياً بعدها. في حال كان لديك أيام تم اختيار وجباتها مسبقاً ضمن فترة التجميد، سيتم إلغاء تلك الاختيارات وإضافة أيام تعويضية بديلة في نهاية الاشتراك.";
        var freezeNoticeEn = $"When freezing: Subscription end date will be extended automatically. Orders within the kitchen lead window ({policy.StartDelayDays * 24}h) are confirmed and delivered, and deliveries pause during freeze and resume automatically. If you had already selected meals for days within the freeze period, those selections will be cleared and replaced with new days at the end.";

        var actions = new CustomerSubscriptionActionsDto(canRenew, canFreeze, canCancel, canUpgrade,
            canRenew ? null : "renewal_not_available",
            canFreeze ? null : pendingFreeze is null ? "freeze_not_available_for_status" : "freeze_request_pending",
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
            freezeNoticeEn);

        return new SubscriptionCommonData(statusNameAr, tierNameAr, tierNameEn, tierLogoUrl, heroImage, totalDays, completedDays, remainingDays, progress, restaurantDto, pendingFreeze, pendingUpgradeDto, actions);
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
            option.GatewayRecovery,
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
    private static bool IsCurrentStatus(string status) => CurrentIndividualSubscriptionStatuses.IsCurrentStatusName(status);
    private static int CurrentStatusPriority(string status) => status switch
    {
        nameof(SubscriptionStatus.Active) => 0,
        nameof(SubscriptionStatus.Frozen) => 1,
        nameof(SubscriptionStatus.Scheduled) => 2,
        nameof(SubscriptionStatus.Paid) => 3,
        nameof(SubscriptionStatus.SuspendedForReview) => 4,
        _ => 5,
    };
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
        CustomerSubscriptionActionsDto Actions);

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
public sealed record CustomerManagedSubscriptionDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly SuggestedRenewalStartDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerPendingUpgradeDto? PendingUpgrade, CustomerSubscriptionActionsDto Actions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices);
public sealed record CustomerSubscriptionSlimDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, string TierNameAr, string TierNameEn, string? TierLogoUrl, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly NextRenewalDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerPendingUpgradeDto? PendingUpgrade, CustomerSubscriptionActionsDto Actions);
public sealed record CustomerFrozenRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl);
public sealed record CustomerPendingFreezeDto(Guid RequestId, DateOnly StartDate, DateOnly EndDate, int FreezeDays, VerificationStatus Status);
public sealed record CustomerPendingUpgradeDto(Guid RequestId, RestaurantTier FromTier, RestaurantTier ToTier, decimal PriceDifference, VerificationStatus Status);

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
    string? FreezeNoticeEn = null);

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
