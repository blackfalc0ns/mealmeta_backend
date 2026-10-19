using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
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
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public CampaignReservationExpirationService(
        IAppDbContext context,
        IDateTimeProvider clock,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _context = context;
        _clock = clock;
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<int> ExpireStaleReservationsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var candidateSessions = await _context.CampaignCheckoutSessions
            .AsNoTracking()
            .Where(s =>
                (s.Status == CampaignCheckoutSessionStatus.Pending && s.ExpiresAtUtc <= now) ||
                (s.Status == CampaignCheckoutSessionStatus.PaymentPending && s.ExpiresAtUtc <= now &&
                 (s.ProcessingLeaseExpiresAtUtc == null || s.ProcessingLeaseExpiresAtUtc <= now)))
            .Select(s => s.Id).ToListAsync(ct);

        var expiredCount = 0;

        foreach (var sessionId in candidateSessions)
        {
            async Task<Result<int>> ExpireLocked(CancellationToken token)
            {
                if (_locks is not null) await _locks.LockCampaignCheckoutSessionAsync(sessionId, token);
                var session = await _context.CampaignCheckoutSessions.Include(s => s.Allocations)
                    .Include(s => s.PaymentTransactions).FirstOrDefaultAsync(s => s.Id == sessionId, token);
                if (session is null || session.ExpiresAtUtc > now ||
                    session.Status is not (CampaignCheckoutSessionStatus.Pending or CampaignCheckoutSessionStatus.PaymentPending) ||
                    session.PaymentTransactions.Any(p => p.Status == PaymentTransactionStatus.Succeeded) ||
                    session.MarkExpired(now).IsFailure)
                    return Result.Success(0);

                if (!session.ReservationsReleasedAtUtc.HasValue)
                {
                    var dates = session.Allocations.Select(a => (DateOnly?)a.ServiceDate).Distinct().ToList();
                    var restIds = session.Allocations.Select(a => a.RestaurantId).Distinct().ToList();
                    var capacities = await _context.CampaignRestaurantCapacities
                        .Where(c => c.CampaignId == session.CampaignId && dates.Contains(c.ServiceDate) && restIds.Contains(c.RestaurantId))
                        .ToListAsync(token);
                    foreach (var alloc in session.Allocations)
                    {
                        var cap = capacities.FirstOrDefault(c => c.RestaurantId == alloc.RestaurantId && c.ServiceDate == alloc.ServiceDate);
                        if (cap is not null) cap.ReservedBoxes = Math.Max(0, cap.ReservedBoxes - alloc.Units);
                    }
                    session.ReservationsReleasedAtUtc = now;
                }
                await _context.SaveChangesAsync(token);
                return Result.Success(1);
            }
            var result = _transactions is null
                ? await ExpireLocked(ct)
                : await _transactions.ExecuteResultAsync(ExpireLocked, ct);
            if (result.IsSuccess) expiredCount += result.Value;
        }

        return expiredCount;
    }
}
