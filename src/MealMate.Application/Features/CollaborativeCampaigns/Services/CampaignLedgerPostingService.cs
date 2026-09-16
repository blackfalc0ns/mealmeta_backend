using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class CampaignLedgerPostingService : ICampaignLedgerPostingService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CampaignLedgerPostingService(
        IAppDbContext db,
        IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<Guid>> PostPurchaseEntriesAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var journalId = Guid.NewGuid();
        var utcNow = _clock.UtcNow;

        // 1. Customer Payment Entry
        var customerPayment = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = session.CampaignId,
            CheckoutSessionId = session.Id,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Description = $"Customer payment for session {session.Id}",
            CreatedAtUtc = utcNow
        };
        _db.CampaignFinancialLedgers.Add(customerPayment);

        // 2. Restaurant Settlements
        if (session.Allocations.Count > 0)
        {
            var totalDays = session.Allocations.Count;
            var perDayAmount = Math.Round(transaction.Amount / totalDays, 3, MidpointRounding.AwayFromZero);

            var groupedByRestaurant = session.Allocations
                .GroupBy(a => a.RestaurantId);

            foreach (var group in groupedByRestaurant)
            {
                var restaurantDays = group.Count();
                var settlementAmount = perDayAmount * restaurantDays;

                var restSettlement = new CampaignFinancialLedger
                {
                    JournalId = journalId,
                    CampaignId = session.CampaignId,
                    CheckoutSessionId = session.Id,
                    RestaurantId = group.Key,
                    EntryType = CampaignLedgerEntryType.RestaurantSettlement,
                    Amount = settlementAmount,
                    Currency = transaction.Currency,
                    Description = $"Restaurant settlement for restaurant {group.Key} ({restaurantDays} day(s))",
                    CreatedAtUtc = utcNow
                };
                _db.CampaignFinancialLedgers.Add(restSettlement);
            }
        }

        // 3. Payment Processing Fee (e.g., 1.5%)
        var feeAmount = Math.Round(transaction.Amount * 0.015m, 3, MidpointRounding.AwayFromZero);
        var feeEntry = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = session.CampaignId,
            CheckoutSessionId = session.Id,
            EntryType = CampaignLedgerEntryType.PaymentProcessingFee,
            Amount = feeAmount,
            Currency = transaction.Currency,
            Description = $"Payment gateway processing fee ({transaction.PaymentProvider})",
            CreatedAtUtc = utcNow
        };
        _db.CampaignFinancialLedgers.Add(feeEntry);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(journalId);
    }

    public async Task<Result<Guid>> PostRefundEntriesAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        string refundReason,
        CancellationToken cancellationToken = default)
    {
        var journalId = Guid.NewGuid();
        var utcNow = _clock.UtcNow;

        var refundEntry = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = session.CampaignId,
            CheckoutSessionId = session.Id,
            EntryType = CampaignLedgerEntryType.CustomerRefund,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Description = $"Customer refund for session {session.Id}. Reason: {refundReason}",
            CreatedAtUtc = utcNow
        };
        _db.CampaignFinancialLedgers.Add(refundEntry);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(journalId);
    }
}
