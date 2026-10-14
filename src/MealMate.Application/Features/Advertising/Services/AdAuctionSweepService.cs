using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.Advertising.Services;

public sealed record AdAuctionSweepResult(int ClosedAuctions, int ActivatedPlacements, int EndedPlacements);

/// <summary>
/// Periodic sweep: closes bidding windows exactly once, awards the highest bidder
/// (ties resolved by server acceptance order), and activates/ends placements by their
/// display windows. AwaitingPayment beyond the policy deadline becomes NoFill —
/// the next bidder is never debited automatically.
/// </summary>
public sealed class AdAuctionSweepService(
    IAppDbContext context,
    IDateTimeProvider clock,
    ILogger<AdAuctionSweepService>? logger = null,
    IApplicationTransactionExecutor? transactions = null,
    IApplicationLockManager? locks = null)
{
    public Task<AdAuctionSweepResult> SweepAsync(CancellationToken ct = default)
        => transactions is null ? SweepCoreAsync(ct) : transactions.ExecuteAsync(SweepCoreAsync, ct);

    private async Task<AdAuctionSweepResult> SweepCoreAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var closed = 0;
        var activated = 0;
        var ended = 0;

        // 1. Close open auctions whose bidding window ended.
        var toClose = await context.AdAuctions
            .Include(a => a.Bids)
            .Where(a => a.Status == AdAuctionStatus.Open && a.BidsCloseAtUtc <= now)
            .ToListAsync(ct);

        foreach (var auction in toClose)
        {
            if (locks is not null) await locks.LockAdAuctionAsync(auction.Id, ct);
            if (auction.Status != AdAuctionStatus.Open || auction.BidsCloseAtUtc > now) continue;
            var auctionPolicy = await context.AdCommercialPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == auction.PolicyId &&
                                          p.Version == auction.PolicyVersion &&
                                          p.ApprovedByUserId != null, ct);
            if (auctionPolicy is null)
            {
                logger?.LogWarning("Auction {AuctionId} has no approved policy snapshot; left open.", auction.Id);
                continue;
            }

            // Immutable close: winner recorded exactly once in this transaction.
            if (auction.Bids.Count == 0)
            {
                auction.Status = AdAuctionStatus.NoFill;
            }
            else
            {
                var winner = auction.Bids
                    .OrderByDescending(b => b.Amount)
                    .ThenBy(b => b.AcceptedAtUtc) // approved tie policy: earliest accepted wins
                    .ThenBy(b => b.Id)
                    .First();

                auction.Status = AdAuctionStatus.AwaitingPayment;
                auction.WinningRestaurantId = winner.RestaurantId;
                auction.WinningBidId = winner.Id;
                auction.WinningAmount = winner.Amount;
                auction.CurrencyCode = winner.CurrencyCode;
                winner.IsWinning = true;
            }

            auction.ClosedAtUtc = now;
            closed++;
        }

        // 2. Expire unpaid AwaitingPayment auctions past the policy deadline: NoFill, never auto-debit.
        var awaitingPayment = await context.AdAuctions
            .Include(a => a.Bids)
            .Where(a => a.Status == AdAuctionStatus.AwaitingPayment && a.ClosedAtUtc != null)
            .ToListAsync(ct);

        foreach (var auction in awaitingPayment)
        {
            if (locks is not null) await locks.LockAdAuctionAsync(auction.Id, ct);
            if (auction.Status != AdAuctionStatus.AwaitingPayment || auction.ClosedAtUtc is null) continue;
            var policy = await context.AdCommercialPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == auction.PolicyId &&
                                          p.Version == auction.PolicyVersion &&
                                          p.ApprovedByUserId != null, ct);
            if (policy is null || policy.WinnerPaymentDeadlineMinutes <= 0)
            {
                // Missing/corrupt snapshots are held for review; a newer policy must not rewrite this sale.
                logger?.LogWarning(
                    "Auction {AuctionId} is awaiting payment with no valid saved policy {PolicyId} v{PolicyVersion}; left unchanged.",
                    auction.Id, auction.PolicyId, auction.PolicyVersion);
                continue;
            }

            if (auction.ClosedAtUtc!.Value > now.AddMinutes(-policy.WinnerPaymentDeadlineMinutes))
            {
                continue;
            }

            if (await context.AdPaymentTransactions
                .AnyAsync(t => t.AdAuctionId == auction.Id && t.Status == "Succeeded", ct))
            {
                continue; // paid but not yet swept to Awarded; leave for the next pass with payment recorded
            }

            var pendingOffer = await context.AdAuctionNextBidderOffers
                .FirstOrDefaultAsync(o => o.AdAuctionId == auction.Id && o.Status == "Pending", ct);
            if (pendingOffer is not null && pendingOffer.ExpiresAtUtc > now)
            {
                // Keep the auction in AwaitingPayment while explicit consent is pending.
                // This retains the PostgreSQL exclusion reservation for the sold window.
                continue;
            }
            if (pendingOffer is not null)
                pendingOffer.Status = "Expired";

            var offers = await context.AdAuctionNextBidderOffers
                .Where(o => o.AdAuctionId == auction.Id)
                .ToListAsync(ct);
            var alreadyOfferedBidIds = offers.Select(o => o.AdBidId).ToHashSet();
            var alreadyOfferedRestaurantIds = offers.Select(o => o.RestaurantId).ToHashSet();
            if (auction.WinningRestaurantId.HasValue)
            {
                alreadyOfferedRestaurantIds.Add(auction.WinningRestaurantId.Value);
            }

            var next = auction.Bids
                .Where(b => !b.IsWinning
                    && b.Id != auction.WinningBidId
                    && !alreadyOfferedBidIds.Contains(b.Id)
                    && !alreadyOfferedRestaurantIds.Contains(b.RestaurantId))
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.AcceptedAtUtc)
                .FirstOrDefault();
            if (next is not null && policy.NextBidderPolicy == AdCommercialPolicy.NextBidderPolicyOfferWithConsent)
            {
                context.AdAuctionNextBidderOffers.Add(new AdAuctionNextBidderOffer
                {
                    AdAuctionId = auction.Id, AdBidId = next.Id, RestaurantId = next.RestaurantId,
                    Amount = next.Amount, CurrencyCode = next.CurrencyCode, Status = "Pending",
                    ExpiresAtUtc = now.AddMinutes(policy.WinnerPaymentDeadlineMinutes)
                });
                auction.Status = AdAuctionStatus.AwaitingPayment;
            }
            else
            {
                auction.Status = AdAuctionStatus.NoFill;
            }
            auction.WinningRestaurantId = null;
            auction.WinningBidId = null;
            auction.WinningAmount = null;
            closed++;
        }

        // 3. Activate scheduled placements whose display window started (paid + approved only).
        var toActivate = await context.AdPlacements
            .Include(p => p.AdAuction)
            .Where(p => p.Status == AdPlacementStatus.Scheduled && p.DisplayStartsAtUtc != null && p.DisplayStartsAtUtc <= now)
            .ToListAsync(ct);

        foreach (var placement in toActivate)
        {
            if (placement.Activate(now).IsSuccess)
            {
                activated++;
            }
        }

        // 4. End active placements whose display window ended.
        var toEnd = await context.AdPlacements
            .Where(p => p.Status == AdPlacementStatus.Active && p.DisplayEndsAtUtc != null && p.DisplayEndsAtUtc <= now)
            .ToListAsync(ct);

        foreach (var placement in toEnd)
        {
            placement.Status = AdPlacementStatus.Ended;
            ended++;
        }

        if (closed > 0 || activated > 0 || ended > 0)
        {
            await context.SaveChangesAsync(ct);
            logger?.LogInformation(
                "Advertising sweep: {Closed} auctions closed, {Activated} placements activated, {Ended} placements ended.",
                closed, activated, ended);
        }

        return new AdAuctionSweepResult(closed, activated, ended);
    }
}
