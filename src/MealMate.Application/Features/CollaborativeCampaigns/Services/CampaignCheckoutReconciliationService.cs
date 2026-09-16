using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class CampaignCheckoutReconciliationService : ICampaignCheckoutReconciliationService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IMediator _mediator;

    public CampaignCheckoutReconciliationService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IMediator mediator)
    {
        _db = db;
        _clock = clock;
        _mediator = mediator;
    }

    public async Task<int> ReconcileStuckSessionsAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;

        var stuckSessions = await _db.CampaignCheckoutSessions
            .Include(s => s.Allocations)
            .Include(s => s.PaymentTransactions)
            .Where(s => s.Status == CampaignCheckoutSessionStatus.Committing ||
                        s.Status == CampaignCheckoutSessionStatus.PaymentPending)
            .ToListAsync(cancellationToken);

        var reconciledCount = 0;

        foreach (var session in stuckSessions)
        {
            // Skip sessions with an active processing lease
            if (session.ProcessingLeaseExpiresAtUtc.HasValue && session.ProcessingLeaseExpiresAtUtc.Value > utcNow)
            {
                continue;
            }

            if (session.Status == CampaignCheckoutSessionStatus.Committing)
            {
                var subExists = await _db.Subscriptions
                    .AnyAsync(s => s.CampaignId == session.CampaignId &&
                                   s.CustomerUserId == session.CustomerId &&
                                   s.BundleId == session.BundleId,
                        cancellationToken);

                if (subExists)
                {
                    session.MarkCompleted();
                }
                else
                {
                    await _mediator.Send(new CompleteCampaignPurchaseCommand(session.Id), cancellationToken);
                }

                reconciledCount++;
            }
            else if (session.Status == CampaignCheckoutSessionStatus.PaymentPending)
            {
                var succeededTx = session.PaymentTransactions
                    .FirstOrDefault(t => t.Status == PaymentTransactionStatus.Succeeded)
                    ?? await _db.CampaignPaymentTransactions
                        .FirstOrDefaultAsync(t => t.CheckoutSessionId == session.Id &&
                                                  t.Status == PaymentTransactionStatus.Succeeded,
                            cancellationToken);

                if (succeededTx != null)
                {
                    session.MarkPaid();
                    await _mediator.Send(new CompleteCampaignPurchaseCommand(session.Id), cancellationToken);
                    reconciledCount++;
                }
                else if (session.ExpiresAtUtc <= utcNow)
                {
                    session.MarkExpired(utcNow);

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

                    reconciledCount++;
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return reconciledCount;
    }
}
