using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Referrals;

public interface IReferralRewardService
{
    Task CompleteForPaidSubscriptionAsync(Subscription subscription, CancellationToken ct = default);
}

/// <summary>
/// Credits the referrer's promotional wallet when a referred customer's subscription is marked paid.
/// The ReferralAttribution row is the source of truth (checkout hold links it via RewardSubscriptionId;
/// the explicit attribution endpoint leaves it null until the customer's first qualifying paid
/// subscription). Idempotent: a completed attribution (RewardedAtUtc set) is never credited twice.
/// </summary>
public sealed class CustomerReferralRewardsService(IAppDbContext db, IDateTimeProvider clock, IApplicationLockManager? locks = null)
    : IReferralRewardService
{
    private const string ReferralRewardReference = "ReferralReward";

    public async Task CompleteForPaidSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
    {
        var candidate = await db.ReferralAttributions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.RewardedAtUtc == null
            && (x.RewardSubscriptionId == subscription.Id
                || (x.RewardSubscriptionId == null && x.ReferredCustomerUserId == subscription.CustomerUserId)), ct);
        if (candidate is null)
            return;

        if (locks is not null)
            await locks.LockCustomerWalletAsync(candidate.ReferrerUserId, ct);
        var pending = await db.ReferralAttributions.FirstOrDefaultAsync(x =>
            x.RewardedAtUtc == null
            && (x.RewardSubscriptionId == subscription.Id
                || (x.RewardSubscriptionId == null && x.ReferredCustomerUserId == subscription.CustomerUserId)), ct);
        if (pending is null)
            return;

        var paidSubscriptionStatuses = new[]
        {
            SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
        };
        var hasPriorPaidSubscription = await db.Subscriptions.AsNoTracking().AnyAsync(x =>
            x.CustomerUserId == subscription.CustomerUserId
            && x.Id != subscription.Id
            && (x.PaymentVerifiedAtUtc != null || paidSubscriptionStatuses.Contains(x.Status)), ct);
        if (hasPriorPaidSubscription)
            return;

        var percentSnapshot = pending.RewardPercentSnapshot ?? await db.ReferralCodes.AsNoTracking()
            .Where(x => x.Id == pending.ReferralCodeId)
            .Select(x => (decimal?)x.RewardPercent!.Value)
            .FirstOrDefaultAsync(ct);
        var currencyCode = subscription.PaidAmount?.CurrencyCode ?? "KWD";
        var rewardAmount = Math.Round(
            (subscription.PaidAmount?.Amount ?? 0m) * (percentSnapshot ?? 0m),
            3, MidpointRounding.AwayFromZero);
        pending.RewardSubscriptionId = subscription.Id;
        pending.RewardPercentSnapshot = percentSnapshot ?? 0m;
        pending.RewardedAtUtc = clock.UtcNow;
        pending.ModifiedAtUtc = clock.UtcNow;
        if (rewardAmount <= 0m)
            return;

        var customerDiscount = await ResolveCustomerReferralDiscountAsync(subscription.Id, ct);
        if (customerDiscount is { } discount && discount > 0m)
        {
            db.DiscountsAndReferralLogs.Add(new DiscountsAndReferralLog
            {
                SubscriptionId = subscription.Id,
                DiscountType = "ReferralReward",
                Code = pending.Code,
                Amount = new Money(discount, currencyCode),
                FundingMode = CampaignFundingMode.MealMateFull,
                AppliedAtUtc = clock.UtcNow,
            });
        }

        var creditAmount = new Money(rewardAmount, currencyCode);
        var wallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(x => x.CustomerUserId == pending.ReferrerUserId, ct);
        if (wallet is null)
        {
            wallet = new CustomerPromotionalWallet
            {
                CustomerUserId = pending.ReferrerUserId,
                Balance = new Money(rewardAmount, currencyCode),
                IsActive = true,
                CreatedAtUtc = clock.UtcNow,
            };
            db.CustomerPromotionalWallets.Add(wallet);
        }
        else
        {
            wallet.Balance = wallet.Balance.Add(creditAmount);
            wallet.ModifiedAtUtc = clock.UtcNow;
        }

        var balanceAfter = new Money(wallet.Balance.Amount, currencyCode);
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            WalletType = WalletType.Promotional,
            CustomerUserId = pending.ReferrerUserId,
            Type = WalletTransactionType.Credit,
            Amount = new Money(rewardAmount, currencyCode),
            BalanceAfter = balanceAfter,
            ReferenceType = ReferralRewardReference,
            ReferenceId = subscription.Id,
            CorrelationId = subscription.CorrelationId,
            OccurredAtUtc = clock.UtcNow,
            Description = "مكافأة الإحالة / Referral reward",
        });
    }

    private async Task<decimal?> ResolveCustomerReferralDiscountAsync(Guid subscriptionId, CancellationToken ct)
    {
        var snapshotJson = await db.SubscriptionSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => x.SnapshotJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;
        try
        {
            var document = JsonSerializer.Deserialize<JsonElement>(snapshotJson);
            if (document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty("CouponDiscountKwd", out var value)
                && value.ValueKind is JsonValueKind.Number)
                return value.GetDecimal();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
