using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class LatePaymentResolutionService : ILatePaymentResolutionService
{
    private readonly IAppDbContext _db;
    private readonly ISubscriberCapManager _capManager;
    private readonly IPaymentRefundGateway _refundGateway;
    private readonly IDateTimeProvider _clock;

    public LatePaymentResolutionService(
        IAppDbContext db,
        ISubscriberCapManager capManager,
        IPaymentRefundGateway refundGateway,
        IDateTimeProvider clock)
    {
        _db = db;
        _capManager = capManager;
        _refundGateway = refundGateway;
        _clock = clock;
    }

    public async Task<LatePaymentResolutionResult> ResolveLatePaymentAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        // 1. Check Subscriber Cap
        var capEntity = await _db.CampaignSubscriberCaps
            .FirstOrDefaultAsync(c => c.CampaignId == session.CampaignId, cancellationToken);

        if (capEntity != null)
        {
            var capState = new SubscriberCapState(capEntity.Cap, 0, capEntity.CurrentCount);
            if (!_capManager.CanReserve(capState, 1))
            {
                return await TriggerAutoRefundAsync(session, transaction, "Campaign subscriber cap reached.", cancellationToken);
            }
        }

        // 2. Check Daily Restaurant Capacities
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
            if (cap == null || (cap.ReservedBoxes + alloc.Units > cap.MaxBoxes))
            {
                return await TriggerAutoRefundAsync(session, transaction, "Daily restaurant fulfillment capacity exhausted.", cancellationToken);
            }
        }

        // 3. Re-reserve capacity & increment cap
        foreach (var alloc in session.Allocations)
        {
            var cap = capacities.First(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
            cap.ReservedBoxes += alloc.Units;
        }

        if (capEntity != null)
        {
            capEntity.CurrentCount += 1;
        }

        session.MarkPaid();
        await _db.SaveChangesAsync(cancellationToken);

        return new LatePaymentResolutionResult(
            Success: true,
            NewSessionStatus: CampaignCheckoutSessionStatus.Paid,
            RequiresRefund: false,
            Message: "Capacity successfully reacquired.");
    }

    private async Task<LatePaymentResolutionResult> TriggerAutoRefundAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        string reason,
        CancellationToken cancellationToken)
    {
        session.MarkRefundPending();
        transaction.Status = PaymentTransactionStatus.RefundPending;

        var refundSuccess = await _refundGateway.IssueRefundAsync(
            transaction.PaymentProvider,
            transaction.ProviderTransactionId,
            transaction.Amount,
            transaction.Currency,
            reason,
            cancellationToken);

        if (refundSuccess)
        {
            session.MarkRefunded();
            transaction.Status = PaymentTransactionStatus.Refunded;
            transaction.RefundedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new LatePaymentResolutionResult(
            Success: false,
            NewSessionStatus: session.Status,
            RequiresRefund: true,
            Message: refundSuccess
                ? $"Capacity unavailable ({reason}). Automatic refund processed."
                : $"Capacity unavailable ({reason}). Refund pending gateway confirmation.");
    }
}
