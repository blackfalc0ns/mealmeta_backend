using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Entities;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class CampaignPaymentService : ICampaignPaymentService
{
    private const string Provider = "Payzah";
    private const int MaximumIdempotencyKeyLength = 200;
    private readonly IAppDbContext _db;
    private readonly IApplicationTransactionExecutor _transactions;
    private readonly IApplicationLockManager _locks;
    private readonly IPaymentGatewayService _payments;
    private readonly IUserAccountService _users;
    private readonly ILatePaymentResolutionService _latePayments;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;

    public CampaignPaymentService(
        IAppDbContext db,
        IApplicationTransactionExecutor transactions,
        IApplicationLockManager locks,
        IPaymentGatewayService payments,
        IUserAccountService users,
        ILatePaymentResolutionService latePayments,
        IDateTimeProvider clock,
        IConfiguration configuration)
    {
        _db = db;
        _transactions = transactions;
        _locks = locks;
        _payments = payments;
        _users = users;
        _latePayments = latePayments;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<Result<CampaignPaymentStartedDto>> StartAsync(
        Guid sessionId, Guid customerId, StartCampaignPaymentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > MaximumIdempotencyKeyLength)
            return Error.Validation("campaign.payment.idempotency_key_invalid");

        var hash = ComputeRequestHash(sessionId, customerId, request);
        var scope = $"CampaignPayment:{customerId:N}";
        var preparation = await _transactions.ExecuteResultAsync(async transactionCt =>
        {
            await _locks.LockCampaignPaymentKeyAsync(customerId, request.IdempotencyKey, transactionCt);
            await _locks.LockCampaignCheckoutSessionAsync(sessionId, transactionCt);

            var session = await _db.CampaignCheckoutSessions
                .Include(x => x.Allocations)
                .FirstOrDefaultAsync(x => x.Id == sessionId && x.CustomerId == customerId, transactionCt);
            if (session is null)
                return Result.Failure<StartPreparation>(Error.NotFound("campaign.payment.session_not_found"));

            var priorRecord = await _db.IdempotencyRecords
                .FirstOrDefaultAsync(x => x.Scope == scope && x.Key == request.IdempotencyKey, transactionCt);
            if (priorRecord is not null)
            {
                if (!string.Equals(priorRecord.RequestHash, hash, StringComparison.Ordinal))
                    return Result.Failure<StartPreparation>(Error.Conflict("campaign.payment.idempotency_conflict"));

                if (!string.IsNullOrWhiteSpace(priorRecord.ResponseBody))
                {
                    var saved = JsonSerializer.Deserialize<CampaignPaymentStartedDto>(priorRecord.ResponseBody);
                    if (saved is not null) return Result.Success(new StartPreparation(saved));
                }

                if (!string.IsNullOrWhiteSpace(session.PaymentId) && !string.IsNullOrWhiteSpace(session.PaymentUrl))
                {
                    var saved = new CampaignPaymentStartedDto(session.Id, session.PaymentId, session.PaymentUrl,
                        session.FinalAmount ?? 0m, session.CurrencyCode ?? string.Empty,
                        "Pending", session.ExpiresAtUtc);
                    priorRecord.ResponseBody = JsonSerializer.Serialize(saved);
                    priorRecord.HttpStatusCode = 202;
                    priorRecord.ResourceUri = $"/api/v1/customer/campaigns/{session.Id}/payment";
                    await _db.SaveChangesAsync(transactionCt);
                    return Result.Success(new StartPreparation(saved));
                }

                return Result.Failure<StartPreparation>(Error.Failure(
                    session.PaymentInitializationErrorCode ?? "campaign.payment.initialization_pending"));
            }

            if (!string.IsNullOrWhiteSpace(session.PaymentIdempotencyKey))
                return Result.Failure<StartPreparation>(Error.Conflict("campaign.payment.already_started"));

            if (session.Status != CampaignCheckoutSessionStatus.Pending || session.PaymentWasCancelled)
                return Result.Failure<StartPreparation>(Error.Conflict(
                    session.Status == CampaignCheckoutSessionStatus.Expired || session.ExpiresAtUtc <= _clock.UtcNow
                        ? "campaign.payment.session_expired"
                        : "campaign.payment.session_not_payable"));

            if (session.ExpiresAtUtc <= _clock.UtcNow)
                return Result.Failure<StartPreparation>(Error.Conflict("campaign.payment.session_expired"));

            if (session.FinalAmount is not decimal amount || amount <= 0m || decimal.Round(amount, 3) != amount ||
                !string.Equals(session.CurrencyCode, "KWD", StringComparison.Ordinal))
                return Result.Failure<StartPreparation>(Error.Validation("campaign.payment.quote_missing"));

            var callbackUrl = request.CallbackUrl ?? _configuration["Checkout:DefaultCallbackUrl"];
            var errorUrl = request.ErrorUrl ?? _configuration["Checkout:DefaultErrorUrl"];
            if (string.IsNullOrWhiteSpace(callbackUrl) || string.IsNullOrWhiteSpace(errorUrl))
                return Result.Failure<StartPreparation>(Error.Validation("campaign.payment.return_url_missing"));
            if (!IsAllowedRedirectUrl(callbackUrl) || !IsAllowedRedirectUrl(errorUrl))
                return Result.Failure<StartPreparation>(Error.Validation("campaign.payment.return_url_not_allowed"));

            var user = await _users.FindByIdAsync(customerId, transactionCt);
            if (user is null)
                return Result.Failure<StartPreparation>(Error.NotFound("campaign.payment.customer_not_found"));
            if (string.IsNullOrWhiteSpace(user.PhoneNumber) || !user.PhoneNumberConfirmed)
                return Result.Failure<StartPreparation>(Error.Validation("campaign.payment.customer_contact_unverified"));

            if (request.PaymentMethodId != 3)
                return Result.Failure<StartPreparation>(Error.Validation("campaign.payment.method_invalid"));

            // This durable journal is committed before any request reaches the external provider.
            session.PaymentIdempotencyKey = request.IdempotencyKey;
            session.PaymentRequestHash = hash;
            session.PaymentCallbackUrl = callbackUrl;
            session.PaymentErrorUrl = errorUrl;
            session.PaymentInitializationStartedAtUtc = _clock.UtcNow;
            session.PaymentInitializationErrorCode = null;
            if (session.MarkPaymentPending().IsFailure)
                return Result.Failure<StartPreparation>(Error.Conflict("campaign.payment.session_not_payable"));

            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Scope = scope,
                Key = request.IdempotencyKey,
                RequestHash = hash,
                UserId = customerId,
                CreatedAtUtc = _clock.UtcNow,
                ExpiresAtUtc = _clock.UtcNow.AddYears(1),
                HttpStatusCode = 202,
                ResourceUri = $"/api/v1/customer/campaigns/{session.Id}/payment"
            });
            await _db.SaveChangesAsync(transactionCt);
            return Result.Success(new StartPreparation(null, session.Id, amount, session.CurrencyCode!, session.ExpiresAtUtc,
                user.FullName ?? string.Empty, user.Email ?? string.Empty, user.PhoneNumber, callbackUrl, errorUrl));
        }, ct);

        if (preparation.IsFailure) return preparation.Error;
        if (preparation.Value.Replay is not null) return preparation.Value.Replay;
        var plan = preparation.Value;

        PaymentCreationReceipt receipt;
        try
        {
            receipt = await _payments.ExecutePaymentAsync(plan.Amount, plan.CustomerName, plan.CustomerEmail,
                plan.CustomerPhone, plan.CallbackUrl!, plan.ErrorUrl!, $"campaign-checkout:{plan.SessionId:N}",
                request.PaymentMethodId, ct);
            if (string.IsNullOrWhiteSpace(receipt.PaymentHandle) || string.IsNullOrWhiteSpace(receipt.PaymentUrl))
                throw new InvalidOperationException("The payment provider returned no usable payment handle or hosted URL.");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await RecordInitializationFailureAsync(plan.SessionId, customerId, "campaign.payment.initialization_unknown");
            return Error.Failure("campaign.payment.initialization_unknown");
        }
        catch (OperationCanceledException)
        {
            await RecordInitializationFailureAsync(plan.SessionId, customerId, "campaign.payment.initialization_unknown");
            return Error.Failure("campaign.payment.initialization_unknown");
        }

        var started = new CampaignPaymentStartedDto(plan.SessionId, receipt.PaymentHandle, receipt.PaymentUrl,
            plan.Amount, plan.Currency, "Pending", plan.ExpiresAtUtc);
        var persisted = await PersistReceiptAsync(plan.SessionId, customerId, request.IdempotencyKey, hash, started);
        return persisted.IsSuccess ? started : persisted.Error;
    }

    public async Task<Result<CampaignPaymentStatusDto>> GetStatusAsync(Guid sessionId, Guid customerId, CancellationToken ct = default)
    {
        var session = await _db.CampaignCheckoutSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.CustomerId == customerId, ct);
        if (session is null) return Error.NotFound("campaign.payment.session_not_found");

        if (session.PaymentIdempotencyKey is null)
            return await BuildStatusAsync(session, null, ct);

        var paymentId = session.PaymentId;
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            var attempt = await FindAttemptAsync(session, ct);
            if (attempt is not null && !string.IsNullOrWhiteSpace(attempt.PaymentId))
            {
                paymentId = "pz-" + attempt.TrackId;
                await PersistRecoveredPaymentIdAsync(session.Id, paymentId, ct);
            }
        }

        if (string.IsNullOrWhiteSpace(paymentId))
            return await BuildStatusAsync(session, null, ct);

        var storedAttempt = await FindAttemptAsync(session, ct);
        if (!AttemptMatchesHandle(storedAttempt, paymentId))
            return Error.Conflict("campaign.payment.status_unverified");

        PaymentGatewayStatusResponse? verified;
        try
        {
            verified = await _payments.GetPaymentStatusAsync(paymentId, "PaymentId", ct);
        }
        catch (Exception)
        {
            return Error.Failure("campaign.payment.status_unavailable");
        }

        if (!MatchesQuoteAndAttempt(session, paymentId, verified, storedAttempt))
            return Error.Conflict("campaign.payment.status_unverified");

        if (NormalizeProviderStatus(verified!.InvoiceStatus) != ProviderPaymentState.Pending)
        {
            var apply = await ApplyProviderStatusAsync(session.Id, paymentId, verified, ct);
            if (apply.IsFailure) return apply.Error;
        }
        var fresh = await _db.CampaignCheckoutSessions.AsNoTracking().FirstAsync(x => x.Id == session.Id, ct);
        var subscriptionId = await FindSubscriptionIdAsync(session.Id, ct);
        return BuildStatusValue(fresh, paymentId, subscriptionId, verified!.InvoiceStatus);
    }

    public async Task<Result<ProcessPaymentCallbackResultDto>> ProcessCallbackAsync(
        ProcessPaymentCallbackCommand command, CancellationToken ct = default)
    {
        var initial = await _db.CampaignCheckoutSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.CheckoutSessionId, ct);
        if (initial is null)
            return Error.NotFound("Campaign.SessionNotFound");

        // Preserve established flows exactly for sessions created before this service was introduced.
        if (initial.PaymentIdempotencyKey is null)
            return await LegacyCallbackHandler().Handle(command, ct);

        if (!string.Equals(command.PaymentProvider, Provider, StringComparison.OrdinalIgnoreCase))
            return Error.Validation("campaign.payment.provider_invalid");
        if (initial.FinalAmount is not decimal quote || string.IsNullOrWhiteSpace(initial.CurrencyCode) ||
            command.Amount != quote || !string.Equals(command.Currency, initial.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return Error.Validation("campaign.payment.quote_mismatch");

        var paymentId = initial.PaymentId;
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            var attempt = await FindAttemptAsync(initial, ct);
            if (attempt is null || string.IsNullOrWhiteSpace(attempt.PaymentId))
                return Error.Failure("campaign.payment.initialization_unknown");
            paymentId = "pz-" + attempt.TrackId;
            await PersistRecoveredPaymentIdAsync(initial.Id, paymentId, ct);
        }

        var attemptRecord = await FindAttemptAsync(initial, ct);
        if (!AttemptMatchesHandle(attemptRecord, paymentId))
            return Error.Conflict("campaign.payment.callback_unverified");

        PaymentGatewayStatusResponse? verified;
        try
        {
            verified = await _payments.GetPaymentStatusAsync(paymentId, "PaymentId", ct);
        }
        catch (Exception)
        {
            return Error.Failure("campaign.payment.status_unavailable");
        }

        if (!MatchesQuoteAndAttempt(initial, paymentId, verified, attemptRecord))
            return Error.Conflict("campaign.payment.callback_unverified");

        if (!CallbackIdentifiesAttempt(command.ProviderTransactionId, paymentId, attemptRecord!, verified!))
            return Error.Validation("campaign.payment.callback_identity_invalid");

        if (!string.IsNullOrWhiteSpace(command.ProviderPaymentReference) &&
            !string.Equals(command.ProviderPaymentReference, verified!.InvoiceId, StringComparison.Ordinal))
            return Error.Validation("campaign.payment.callback_identity_invalid");

        return await ApplyProviderStatusAsync(initial.Id, paymentId, verified!, ct);
    }

    private async Task<Result<ProcessPaymentCallbackResultDto>> ApplyProviderStatusAsync(
        Guid sessionId, string paymentId, PaymentGatewayStatusResponse verified, CancellationToken ct)
    {
        return await _transactions.ExecuteResultAsync(async transactionCt =>
        {
            await _locks.LockCampaignCheckoutSessionAsync(sessionId, transactionCt);
            var session = await _db.CampaignCheckoutSessions
                .Include(x => x.Allocations)
                .FirstOrDefaultAsync(x => x.Id == sessionId, transactionCt);
            if (session is null) return Result.Failure<ProcessPaymentCallbackResultDto>(Error.NotFound("Campaign.SessionNotFound"));
            if (!string.Equals(session.PaymentId, paymentId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(session.PaymentId))
                return Result.Failure<ProcessPaymentCallbackResultDto>(Error.Conflict("campaign.payment.callback_identity_invalid"));

            var existing = await _db.CampaignPaymentTransactions.FirstOrDefaultAsync(
                x => x.PaymentProvider == Provider && x.ProviderTransactionId == paymentId, transactionCt);
            if (existing is not null)
            {
                var current = await _db.CampaignCheckoutSessions.AsNoTracking()
                    .FirstAsync(x => x.Id == existing.CheckoutSessionId, transactionCt);
                var recoveredState = NormalizeProviderStatus(verified.InvoiceStatus);
                if (existing.Status == PaymentTransactionStatus.Failed && recoveredState == ProviderPaymentState.Paid &&
                    current.Status is not CampaignCheckoutSessionStatus.Completed)
                {
                    // A failed callback can race a provider capture. Reconcile the same canonical
                    // attempt in place; never create a second payment transaction.
                    existing.Status = PaymentTransactionStatus.Succeeded;
                    existing.CompletedAtUtc = _clock.UtcNow;
                    existing.ProviderPaymentReference = paymentId;
                    session.Status = session.Status is CampaignCheckoutSessionStatus.Expired
                        or CampaignCheckoutSessionStatus.PaymentFailed
                        ? CampaignCheckoutSessionStatus.Paid
                        : session.Status;
                    await _db.SaveChangesAsync(transactionCt);
                    var completed = await new CompleteCampaignPurchaseCommandHandler(_db, _clock)
                        .Handle(new CompleteCampaignPurchaseCommand(session.Id), transactionCt);
                    if (completed.IsSuccess)
                        return Result.Success(new ProcessPaymentCallbackResultDto(session.Id, existing.Id,
                            existing.Status.ToString(), nameof(CampaignCheckoutSessionStatus.Completed), true));
                }
                var result = new ProcessPaymentCallbackResultDto(sessionId, existing.Id,
                    existing.Status.ToString(), current.Status.ToString(), current.Status == CampaignCheckoutSessionStatus.Expired);
                if (existing.Status == PaymentTransactionStatus.Succeeded &&
                    current.Status is CampaignCheckoutSessionStatus.Paid or CampaignCheckoutSessionStatus.Committing)
                {
                    var completed = await new CompleteCampaignPurchaseCommandHandler(_db, _clock)
                        .Handle(new CompleteCampaignPurchaseCommand(sessionId), transactionCt);
                    if (completed.IsSuccess) result = result with { SessionStatus = nameof(CampaignCheckoutSessionStatus.Completed) };
                }
                return Result.Success(result);
            }

            var state = NormalizeProviderStatus(verified.InvoiceStatus);
            if (state == ProviderPaymentState.Pending)
                return Result.Failure<ProcessPaymentCallbackResultDto>(Error.Conflict("campaign.payment.callback_pending"));

            if (state == ProviderPaymentState.Paid)
            {
                if (session.Status == CampaignCheckoutSessionStatus.Expired || session.ExpiresAtUtc <= _clock.UtcNow)
                    await ReleaseReservationsOnceAsync(session, transactionCt);

                var command = new ProcessPaymentCallbackCommand(session.Id, Provider, paymentId, paymentId,
                    session.FinalAmount!.Value, session.CurrencyCode!, IsSuccess: true);
                var processed = await LegacyCallbackHandler().Handle(command, transactionCt);
                if (processed.IsFailure) return processed;

                if (processed.Value.SessionStatus is nameof(CampaignCheckoutSessionStatus.Paid) or nameof(CampaignCheckoutSessionStatus.Committing))
                {
                    var completed = await new CompleteCampaignPurchaseCommandHandler(_db, _clock)
                        .Handle(new CompleteCampaignPurchaseCommand(session.Id), transactionCt);
                    if (completed.IsSuccess)
                        return Result.Success(processed.Value with { SessionStatus = nameof(CampaignCheckoutSessionStatus.Completed) });
                }
                return processed;
            }

            if (session.Status is CampaignCheckoutSessionStatus.Paid or CampaignCheckoutSessionStatus.Committing or CampaignCheckoutSessionStatus.Completed)
                return Result.Failure<ProcessPaymentCallbackResultDto>(Error.Conflict("campaign.payment.terminal_paid"));

            var failed = new CampaignPaymentTransaction
            {
                CheckoutSessionId = session.Id,
                CampaignId = session.CampaignId,
                PaymentProvider = Provider,
                ProviderTransactionId = paymentId,
                ProviderPaymentReference = paymentId,
                Amount = session.FinalAmount!.Value,
                Currency = session.CurrencyCode!,
                Status = PaymentTransactionStatus.Failed,
                InitiatedAtUtc = session.PaymentInitializationStartedAtUtc ?? _clock.UtcNow,
                FailedAtUtc = _clock.UtcNow
            };
            _db.CampaignPaymentTransactions.Add(failed);

            if (state == ProviderPaymentState.Cancelled) session.PaymentWasCancelled = true;
            if (state == ProviderPaymentState.Expired &&
                session.Status is CampaignCheckoutSessionStatus.PaymentPending or CampaignCheckoutSessionStatus.Pending)
                session.MarkExpired(_clock.UtcNow);
            else if (session.Status == CampaignCheckoutSessionStatus.PaymentPending)
                session.MarkPaymentFailed();
            else if (session.Status == CampaignCheckoutSessionStatus.Pending)
                session.MarkPaymentFailed();
            else if (session.Status is not (CampaignCheckoutSessionStatus.Expired or CampaignCheckoutSessionStatus.PaymentFailed))
                return Result.Failure<ProcessPaymentCallbackResultDto>(Error.Conflict("campaign.payment.session_not_payable"));

            await ReleaseReservationsOnceAsync(session, transactionCt);
            await _db.SaveChangesAsync(transactionCt);
            return Result.Success(new ProcessPaymentCallbackResultDto(session.Id, failed.Id, failed.Status.ToString(),
                session.Status.ToString()));
        }, ct);
    }

    private async Task ReleaseReservationsOnceAsync(CampaignCheckoutSession session, CancellationToken ct)
    {
        if (session.ReservationsReleasedAtUtc.HasValue) return;
        var restaurantIds = session.Allocations.Select(x => x.RestaurantId).Distinct().ToArray();
        var serviceDates = session.Allocations.Select(x => x.ServiceDate).Distinct().ToArray();
        var capacities = await _db.CampaignRestaurantCapacities
            .Where(x => x.CampaignId == session.CampaignId && restaurantIds.Contains(x.RestaurantId) &&
                        x.ServiceDate.HasValue && serviceDates.Contains(x.ServiceDate.Value))
            .ToListAsync(ct);
        foreach (var allocation in session.Allocations)
        {
            var capacity = capacities.FirstOrDefault(x => x.RestaurantId == allocation.RestaurantId &&
                                                          x.ServiceDate == allocation.ServiceDate);
            if (capacity is not null)
                capacity.ReservedBoxes = Math.Max(0, capacity.ReservedBoxes - allocation.Units);
        }
        session.ReservationsReleasedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Result<CampaignPaymentStartedDto>> PersistReceiptAsync(
        Guid sessionId, Guid customerId, string key, string hash, CampaignPaymentStartedDto started)
    {
        try
        {
            return await _transactions.ExecuteResultAsync(async transactionCt =>
            {
                await _locks.LockCampaignPaymentKeyAsync(customerId, key, transactionCt);
                await _locks.LockCampaignCheckoutSessionAsync(sessionId, transactionCt);
                var session = await _db.CampaignCheckoutSessions.FirstOrDefaultAsync(
                    x => x.Id == sessionId && x.CustomerId == customerId, transactionCt);
                if (session is null) return Result.Failure<CampaignPaymentStartedDto>(Error.NotFound("campaign.payment.session_not_found"));
                if (!string.Equals(session.PaymentRequestHash, hash, StringComparison.Ordinal))
                    return Result.Failure<CampaignPaymentStartedDto>(Error.Conflict("campaign.payment.idempotency_conflict"));
                session.PaymentId = started.PaymentId;
                session.PaymentUrl = started.PaymentUrl;
                session.PaymentInitializationErrorCode = null;
                var record = await _db.IdempotencyRecords.FirstOrDefaultAsync(x =>
                    x.Scope == $"CampaignPayment:{customerId:N}" && x.Key == key, transactionCt);
                if (record is null) return Result.Failure<CampaignPaymentStartedDto>(Error.Failure("campaign.payment.journal_missing"));
                record.ResponseBody = JsonSerializer.Serialize(started);
                record.HttpStatusCode = 202;
                record.ResourceUri = $"/api/v1/customer/campaigns/{session.Id}/payment";
                await _db.SaveChangesAsync(transactionCt);
                return Result.Success(started);
            }, CancellationToken.None);
        }
        catch
        {
            await RecordInitializationFailureAsync(sessionId, customerId, "campaign.payment.initialization_unknown");
            return Error.Failure("campaign.payment.initialization_unknown");
        }
    }

    private async Task RecordInitializationFailureAsync(Guid sessionId, Guid customerId, string errorCode)
    {
        try
        {
            await _transactions.ExecuteResultAsync(async transactionCt =>
            {
                await _locks.LockCampaignCheckoutSessionAsync(sessionId, transactionCt);
                var session = await _db.CampaignCheckoutSessions.FirstOrDefaultAsync(
                    x => x.Id == sessionId && x.CustomerId == customerId, transactionCt);
                if (session is not null && string.IsNullOrWhiteSpace(session.PaymentId))
                {
                    session.PaymentInitializationErrorCode = errorCode;
                    await _db.SaveChangesAsync(transactionCt);
                }
                return Result.Success(true);
            }, CancellationToken.None);
        }
        catch
        {
            // The durable initiation record remains the authority; a later status request can reconcile its provider attempt.
        }
    }

    private async Task<PaymentGatewayAttempt?> FindAttemptAsync(CampaignCheckoutSession session, CancellationToken ct)
    {
        var reference = $"campaign-checkout:{session.Id:N}";
        return await _db.PaymentGatewayAttempts.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Provider == Provider && x.CustomerReference == reference &&
            x.ExpectedAmount == session.FinalAmount && x.ExpectedCurrency == session.CurrencyCode &&
            !string.IsNullOrWhiteSpace(x.PaymentId), ct);
    }

    private async Task PersistRecoveredPaymentIdAsync(Guid sessionId, string paymentId, CancellationToken ct)
    {
        await _transactions.ExecuteResultAsync(async transactionCt =>
        {
            await _locks.LockCampaignCheckoutSessionAsync(sessionId, transactionCt);
            var session = await _db.CampaignCheckoutSessions.FirstOrDefaultAsync(x => x.Id == sessionId, transactionCt);
            if (session is not null && string.IsNullOrWhiteSpace(session.PaymentId))
            {
                session.PaymentId = paymentId;
                await _db.SaveChangesAsync(transactionCt);
            }
            return Result.Success(true);
        }, ct);
    }

    private bool MatchesQuoteAndAttempt(CampaignCheckoutSession session, string paymentId,
        PaymentGatewayStatusResponse? response, PaymentGatewayAttempt? attempt)
    {
        return AttemptMatchesHandle(attempt, paymentId) && response is not null && session.FinalAmount.HasValue && !string.IsNullOrWhiteSpace(session.CurrencyCode) &&
               response.InvoiceValue == session.FinalAmount.Value &&
               string.Equals(response.CurrencyCode, session.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(response.CustomerReference, $"campaign-checkout:{session.Id:N}", StringComparison.Ordinal) &&
               string.Equals(response.PaymentId, paymentId, StringComparison.Ordinal);
    }

    private static bool AttemptMatchesHandle(PaymentGatewayAttempt? attempt, string paymentId) =>
        attempt is not null && !string.IsNullOrWhiteSpace(attempt.PaymentId) &&
        string.Equals(paymentId, "pz-" + attempt.TrackId, StringComparison.Ordinal);

    private static bool CallbackIdentifiesAttempt(string callbackId, string handle, PaymentGatewayAttempt attempt,
        PaymentGatewayStatusResponse verified) =>
        string.Equals(callbackId, handle, StringComparison.Ordinal) ||
        string.Equals(callbackId, attempt.PaymentId, StringComparison.Ordinal) ||
        string.Equals(callbackId, verified.TransactionId, StringComparison.Ordinal);

    private async Task<Result<CampaignPaymentStatusDto>> BuildStatusAsync(CampaignCheckoutSession session, string? paymentId, CancellationToken ct)
    {
        var subscriptionId = await FindSubscriptionIdAsync(session.Id, ct);
        return BuildStatusValue(session, paymentId, subscriptionId, null);
    }

    private Result<CampaignPaymentStatusDto> BuildStatusValue(
        CampaignCheckoutSession session, string? paymentId, Guid? subscriptionId, string? verifiedProviderState)
    {
        var status = StatusFor(session, verifiedProviderState);
        return Result.Success(new CampaignPaymentStatusDto(session.Id, paymentId, status,
            status == "Paid", status is "Failed" or "Expired" or "Cancelled",
            session.FinalAmount ?? 0m, session.CurrencyCode ?? string.Empty, session.ExpiresAtUtc,
            subscriptionId));
    }

    private string StatusFor(CampaignCheckoutSession session, string? verifiedProviderState)
    {
        if (session.Status == CampaignCheckoutSessionStatus.Completed && verifiedProviderState is not null)
            return "Paid";
        if (session.Status is CampaignCheckoutSessionStatus.Paid or CampaignCheckoutSessionStatus.Committing)
            return verifiedProviderState is null ? "Pending" : "Paid";
        if (session.Status == CampaignCheckoutSessionStatus.Refunded) return "Failed";
        if (session.PaymentWasCancelled) return "Cancelled";
        if (session.Status == CampaignCheckoutSessionStatus.PaymentFailed) return "Failed";
        if (session.Status == CampaignCheckoutSessionStatus.Expired || session.ExpiresAtUtc <= _clock.UtcNow)
            return "Expired";
        var state = verifiedProviderState is null ? ProviderPaymentState.Pending : NormalizeProviderStatus(verifiedProviderState);
        return state switch
        {
            ProviderPaymentState.Paid => "Paid",
            ProviderPaymentState.Failed => "Failed",
            ProviderPaymentState.Expired => "Expired",
            ProviderPaymentState.Cancelled => "Cancelled",
            _ => "Pending"
        };
    }

    private async Task<Guid?> FindSubscriptionIdAsync(Guid sessionId, CancellationToken ct) =>
        await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CorrelationId == sessionId.ToString("N"))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

    private ProcessPaymentCallbackCommandHandler LegacyCallbackHandler() =>
        new(_db, _clock, _latePayments);

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration.GetSection("Checkout:AllowedRedirectSchemes").GetChildren()
            .Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (schemes.Length == 0) schemes = ["https", "mealmate"];
        if (!schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration.GetSection("Checkout:AllowedRedirectHosts").GetChildren()
            .Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (hosts.Length == 0) hosts = ["maelmate.runasp.net", "localhost"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static ProviderPaymentState NormalizeProviderStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        "paid" or "success" or "successful" or "captured" => ProviderPaymentState.Paid,
        "failed" or "failure" or "declined" or "voided" => ProviderPaymentState.Failed,
        "expired" or "timeout" => ProviderPaymentState.Expired,
        "cancelled" or "canceled" => ProviderPaymentState.Cancelled,
        _ => ProviderPaymentState.Pending
    };

    private static string ComputeRequestHash(Guid sessionId, Guid customerId, StartCampaignPaymentRequest request)
    {
        var raw = JsonSerializer.Serialize(new
        {
            SessionId = sessionId,
            CustomerId = customerId,
            CallbackUrl = request.CallbackUrl,
            ErrorUrl = request.ErrorUrl,
            PaymentMethodId = request.PaymentMethodId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed record StartPreparation(
        CampaignPaymentStartedDto? Replay,
        Guid SessionId = default,
        decimal Amount = 0m,
        string Currency = "",
        DateTime ExpiresAtUtc = default,
        string CustomerName = "",
        string CustomerEmail = "",
        string CustomerPhone = "",
        string? CallbackUrl = null,
        string? ErrorUrl = null);

    private enum ProviderPaymentState { Pending, Paid, Failed, Expired, Cancelled }
}

