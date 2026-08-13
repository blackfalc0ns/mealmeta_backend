using System.Text.Json;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
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

        var query = _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Where(x => x.CustomerUserId == _currentUser.UserId!.Value);

        if (!includeHistory)
            query = query.Where(x => x.Status != SubscriptionStatus.Completed && x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired);

        var subscriptions = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var cards = new List<CustomerManagedSubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
            cards.Add(await MapSubscription(subscription, ct));

        var current = cards.Where(x => IsCurrentStatus(x.Status)).OrderBy(x => CurrentStatusPriority(x.Status)).ThenByDescending(x => x.StartDate).FirstOrDefault();
        return Ok(new CustomerSubscriptionManagementScreenDto(current, cards, current?.Invoices ?? [],
            current is null ? "no_current_subscription" : null));
    }

    /// <summary>Management details for one owned subscription.</summary>
    [HttpGet("{id:guid}/management")]
    public async Task<IActionResult> ManagementDetails(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await OwnedSubscription(id, ct);
        return subscription is null
            ? NotFound(new { error = "subscription.not_found" })
            : Ok(await MapSubscription(subscription, ct));
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

    private async Task<Subscription?> OwnedSubscription(Guid id, CancellationToken ct) =>
        await _db.Subscriptions.AsNoTracking().Include(x => x.Program).Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == _currentUser.UserId!.Value, ct);

    private async Task<CustomerManagedSubscriptionDto> MapSubscription(Subscription subscription, CancellationToken ct)
    {
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var totalDays = days.Count > 0 ? days.Count : subscription.DurationDays;
        var completedDays = days.Count(x => x.Status == SubscriptionDayStatus.Delivered);
        var remainingDays = Math.Max(0, totalDays - completedDays);
        var progress = totalDays == 0 ? 0m : decimal.Round(completedDays * 100m / totalDays, 1);

        var restaurant = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.IsInitiallySelected).Select(x => new CustomerFrozenRestaurantDto(x.RestaurantId, x.NameAr, x.NameEn)).FirstOrDefaultAsync(ct);
        var heroImage = await GetHeroImage(subscription.Id, ct);
        var policyRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        var policy = SubscriptionFreezePolicyDefaults.Deserialize(policyRow?.ValueJson);
        var pendingFreeze = await _db.SubscriptionFreezeRequests.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new CustomerPendingFreezeDto(x.Id, x.FreezeStartDate, x.FreezeEndDate, x.FreezeDays, x.Status)).FirstOrDefaultAsync(ct);
        var cancellationPending = await _db.SubscriptionOperations.AsNoTracking().AnyAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "CANCELLATION_REQUESTED", ct);
        var invoices = await GetInvoices(subscription, ct);
        var canFreeze = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled && pendingFreeze is null;
        var canCancel = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen && !cancellationPending;
        var canRenew = subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active or SubscriptionStatus.Frozen or SubscriptionStatus.Completed or SubscriptionStatus.Expired;

        return new CustomerManagedSubscriptionDto(subscription.Id, subscription.Status.ToString(), StatusNameAr(subscription.Status), subscription.ProgramId,
            subscription.Program.NameAr, subscription.Program.NameEn, subscription.BundleId, subscription.Bundle.NameAr, subscription.Bundle.NameEn,
            subscription.SelectedTier, subscription.Bundle.MealsPerDay, heroImage, subscription.StartDate, subscription.EndDate, subscription.EndDate.AddDays(1),
            totalDays, completedDays, remainingDays, progress, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode,
            restaurant, pendingFreeze, new CustomerSubscriptionActionsDto(canRenew, canFreeze, canCancel,
                canRenew ? null : "renewal_not_available", canFreeze ? null : pendingFreeze is null ? "freeze_not_available_for_status" : "freeze_request_pending",
                canCancel ? null : cancellationPending ? "cancellation_request_pending" : "cancellation_not_available_for_status",
                policy.StartDelayDays, policy.DefaultDurationDays, policy.ExtendSubscriptionEnd,
                $"/api/v1/customer/subscriptions/{subscription.Id}/renewal-requests",
                $"/api/v1/customer/subscriptions/{subscription.Id}/freeze-requests",
                $"/api/v1/customer/subscriptions/{subscription.Id}/cancellation-requests"), invoices);
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
    private static string StatusNameAr(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingPayment => "بانتظار الدفع", SubscriptionStatus.Paid => "مدفوع", SubscriptionStatus.Scheduled => "مجدول",
        SubscriptionStatus.Active => "نشط", SubscriptionStatus.Frozen => "مجمد", SubscriptionStatus.Completed => "مكتمل",
        SubscriptionStatus.Cancelled => "ملغي", SubscriptionStatus.Expired => "منتهي", SubscriptionStatus.PaymentFailed => "فشل الدفع",
        SubscriptionStatus.SuspendedForReview => "معلق للمراجعة", _ => "مسودة",
    };
    private static CustomerCancellationRequestDto MapCancellation(SubscriptionOperation x) => new(x.Id, x.SubscriptionId, "Submitted", x.Reason, x.TimestampUtc);
}

public sealed record CustomerSubscriptionManagementScreenDto(CustomerManagedSubscriptionDto? CurrentSubscription, IReadOnlyList<CustomerManagedSubscriptionDto> Subscriptions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices, string? EmptyStateCode);
public sealed record CustomerManagedSubscriptionDto(Guid SubscriptionId, string Status, string StatusNameAr, Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, RestaurantTier Tier, int MealsPerDay, string? ImageUrl, DateOnly StartDate, DateOnly EndDate, DateOnly SuggestedRenewalStartDate, int TotalServiceDays, int CompletedServiceDays, int RemainingServiceDays, decimal ProgressPercentage, decimal PaidAmount, string CurrencyCode, CustomerFrozenRestaurantDto? Restaurant, CustomerPendingFreezeDto? PendingFreeze, CustomerSubscriptionActionsDto Actions, IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices);
public sealed record CustomerFrozenRestaurantDto(Guid RestaurantId, string NameAr, string NameEn);
public sealed record CustomerPendingFreezeDto(Guid RequestId, DateOnly StartDate, DateOnly EndDate, int FreezeDays, VerificationStatus Status);
public sealed record CustomerSubscriptionActionsDto(bool CanRenew, bool CanFreeze, bool CanCancel, string? RenewUnavailableReason, string? FreezeUnavailableReason, string? CancelUnavailableReason, int FreezeStartDelayDays, int MaximumFreezeDays, bool FreezeExtendsSubscriptionEnd, string RenewalEndpoint, string FreezeEndpoint, string CancellationEndpoint);
public sealed record CustomerSubscriptionInvoiceDto(Guid ReceiptId, string? InvoiceNumber, string? PaymentId, DateTime IssuedAtUtc, decimal Amount, string CurrencyCode, string Status, string Gateway, string? MaskedCardNumber, bool CanDownload, string? DownloadUnavailableReason);
public sealed record CustomerFreezeRequest(DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record CustomerCancellationRequest(string? Reason, DateOnly? RequestedEffectiveDate);
public sealed record CustomerCancellationRequestDto(Guid RequestId, Guid SubscriptionId, string Status, string? Reason, DateTime RequestedAtUtc);
public sealed record CustomerRenewalRequestDto(Guid RequestId, Guid PreviousSubscriptionId, string Status, Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, bool IncludeFriday, Guid? RegionId, DateOnly SuggestedStartDate, string ConfigurationEndpoint, string CheckoutEndpoint);
