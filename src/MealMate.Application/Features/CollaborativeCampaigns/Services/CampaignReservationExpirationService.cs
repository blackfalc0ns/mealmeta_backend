using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignReservationExpirationService
{
    Task<int> ExpireStaleReservationsAsync(CancellationToken ct = default);
}

public sealed class CampaignReservationExpirationService : ICampaignReservationExpirationService
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;

    public CampaignReservationExpirationService(
        IAppDbContext context,
        IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<int> ExpireStaleReservationsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Query candidate sessions with their allocations and payment transactions
        var candidateSessions = await _context.CampaignCheckoutSessions
            .Include(s => s.Allocations)
            .Include(s => s.PaymentTransactions)
            .Where(s =>
                (s.Status == CampaignCheckoutSessionStatus.Pending && s.ExpiresAtUtc <= now) ||
                (s.Status == CampaignCheckoutSessionStatus.PaymentPending && s.ExpiresAtUtc <= now &&
                 (s.ProcessingLeaseExpiresAtUtc == null || s.ProcessingLeaseExpiresAtUtc <= now)))
            .ToListAsync(ct);

        var expiredCount = 0;

        foreach (var session in candidateSessions)
        {
            // If PaymentPending, double-check that no transaction succeeded
            if (session.Status == CampaignCheckoutSessionStatus.PaymentPending &&
                session.PaymentTransactions.Any(p => p.Status == PaymentTransactionStatus.Succeeded))
            {
                continue;
            }

            // Mark session expired
            session.Status = CampaignCheckoutSessionStatus.Expired;
            expiredCount++;

            // Release daily capacity allocations
            if (session.Allocations.Count > 0)
            {
                var dates = session.Allocations.Select(a => (DateOnly?)a.ServiceDate).Distinct().ToList();
                var restIds = session.Allocations.Select(a => a.RestaurantId).Distinct().ToList();

                var capacities = await _context.CampaignRestaurantCapacities
                    .Where(c => c.CampaignId == session.CampaignId && dates.Contains(c.ServiceDate) && restIds.Contains(c.RestaurantId))
                    .ToListAsync(ct);

                foreach (var alloc in session.Allocations)
                {
                    var cap = capacities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
                    if (cap != null)
                    {
                        cap.ReservedBoxes = Math.Max(0, cap.ReservedBoxes - alloc.Units);
                    }
                }
            }
        }

        if (expiredCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return expiredCount;
    }
}
