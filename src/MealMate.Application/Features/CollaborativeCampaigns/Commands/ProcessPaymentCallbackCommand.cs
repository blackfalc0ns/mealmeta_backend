using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public record ProcessPaymentCallbackCommand(
    Guid CheckoutSessionId,
    string PaymentProvider,
    string ProviderTransactionId,
    string? ProviderPaymentReference,
    decimal Amount,
    string Currency,
    bool IsSuccess,
    string? FailureReason = null) : ICommand<ProcessPaymentCallbackResultDto>;

public record ProcessPaymentCallbackResultDto(
    Guid CheckoutSessionId,
    Guid PaymentTransactionId,
    string Status,
    string SessionStatus,
    bool IsLatePayment = false);

public sealed class ProcessPaymentCallbackCommandHandler
    : ICommandHandler<ProcessPaymentCallbackCommand, ProcessPaymentCallbackResultDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ILatePaymentResolutionService _latePaymentService;

    public ProcessPaymentCallbackCommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        ILatePaymentResolutionService latePaymentService)
    {
        _db = db;
        _clock = clock;
        _latePaymentService = latePaymentService;
    }

    public async Task<Result<ProcessPaymentCallbackResultDto>> Handle(
        ProcessPaymentCallbackCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Idempotency check: provider transaction deduplication
        var existingTx = await _db.CampaignPaymentTransactions
            .FirstOrDefaultAsync(t => t.PaymentProvider == request.PaymentProvider &&
                                      t.ProviderTransactionId == request.ProviderTransactionId,
                cancellationToken);

        if (existingTx != null)
        {
            var existingSession = await _db.CampaignCheckoutSessions
                .FirstOrDefaultAsync(s => s.Id == existingTx.CheckoutSessionId, cancellationToken);

            return Result.Success(new ProcessPaymentCallbackResultDto(
                existingTx.CheckoutSessionId,
                existingTx.Id,
                existingTx.Status.ToString(),
                existingSession?.Status.ToString() ?? "Unknown",
                IsLatePayment: false));
        }

        // 2. Fetch checkout session
        var session = await _db.CampaignCheckoutSessions
            .Include(s => s.Allocations)
            .FirstOrDefaultAsync(s => s.Id == request.CheckoutSessionId, cancellationToken);

        if (session == null)
        {
            return Result.Failure<ProcessPaymentCallbackResultDto>(
                Error.NotFound("Campaign.SessionNotFound", $"Campaign checkout session '{request.CheckoutSessionId}' was not found."));
        }

        var utcNow = _clock.UtcNow;
        var isExpired = session.Status == CampaignCheckoutSessionStatus.Expired ||
                        (session.ExpiresAtUtc <= utcNow &&
                         session.Status != CampaignCheckoutSessionStatus.Paid &&
                         session.Status != CampaignCheckoutSessionStatus.Committing &&
                         session.Status != CampaignCheckoutSessionStatus.Completed);

        // 3. Handle Successful Payment
        if (request.IsSuccess)
        {
            var tx = new CampaignPaymentTransaction
            {
                CheckoutSessionId = session.Id,
                CampaignId = session.CampaignId,
                PaymentProvider = request.PaymentProvider,
                ProviderTransactionId = request.ProviderTransactionId,
                ProviderPaymentReference = request.ProviderPaymentReference,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = PaymentTransactionStatus.Succeeded,
                InitiatedAtUtc = utcNow,
                CompletedAtUtc = utcNow
            };

            _db.CampaignPaymentTransactions.Add(tx);

            if (isExpired)
            {
                await _db.SaveChangesAsync(cancellationToken);
                var lateResult = await _latePaymentService.ResolveLatePaymentAsync(session, tx, cancellationToken);

                return Result.Success(new ProcessPaymentCallbackResultDto(
                    session.Id,
                    tx.Id,
                    tx.Status.ToString(),
                    lateResult.NewSessionStatus.ToString(),
                    IsLatePayment: true));
            }

            session.MarkPaid();
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new ProcessPaymentCallbackResultDto(
                session.Id,
                tx.Id,
                tx.Status.ToString(),
                session.Status.ToString(),
                IsLatePayment: false));
        }

        // 4. Handle Failed Payment
        session.MarkPaymentFailed();

        var failedTx = new CampaignPaymentTransaction
        {
            CheckoutSessionId = session.Id,
            CampaignId = session.CampaignId,
            PaymentProvider = request.PaymentProvider,
            ProviderTransactionId = request.ProviderTransactionId,
            ProviderPaymentReference = request.ProviderPaymentReference,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = PaymentTransactionStatus.Failed,
            InitiatedAtUtc = utcNow,
            FailedAtUtc = utcNow
        };

        _db.CampaignPaymentTransactions.Add(failedTx);

        // Release reserved capacity
        var restIds = session.Allocations.Select(a => a.RestaurantId).Distinct().ToList();
        var dates = session.Allocations.Select(a => a.ServiceDate).Distinct().ToList();

        var capacities = await _db.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == session.CampaignId &&
                        restIds.Contains(c.RestaurantId) &&
                        c.ServiceDate.HasValue &&
                        dates.Contains(c.ServiceDate.Value))
            .ToListAsync(cancellationToken);

        foreach (var alloc in session.Allocations)
        {
            var cap = capacities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
            if (cap != null)
            {
                cap.ReservedBoxes = Math.Max(0, cap.ReservedBoxes - alloc.Units);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ProcessPaymentCallbackResultDto(
            session.Id,
            failedTx.Id,
            failedTx.Status.ToString(),
            session.Status.ToString(),
            IsLatePayment: false));
    }
}
