using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public sealed class SubscriptionCancellationService : ISubscriptionCancellationService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger? _audit;
    private readonly IOperationalPolicyService? _policyService;

    public SubscriptionCancellationService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IAuditLogger? audit = null,
        IOperationalPolicyService? policyService = null)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _policyService = policyService;
    }

    public async Task<Result<CustomerCancellationQuoteDto>> GetQuoteAsync(
        Guid subscriptionId,
        Guid callerUserId,
        UserType callerUserType,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{subscriptionId}' was not found.");

        if (callerUserType == UserType.Customer)
        {
            var isAuthorized = subscription.CustomerUserId == callerUserId
                || (subscription.FamilyGroupId != null && await _db.FamilyGroups.AnyAsync(g => g.Id == subscription.FamilyGroupId && g.ManagerCustomerUserId == callerUserId, cancellationToken));
            if (!isAuthorized)
                return Error.Forbidden("subscription.unauthorized", "You are not authorized to view this subscription.");
        }

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen or SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment))
            return Error.Conflict("subscription.cancellation_not_available", $"Subscription in status '{subscription.Status}' cannot be cancelled.");

        var (quote, _) = await CalculateDetailsAsync(subscription, cancellationToken);
        return quote;
    }

    public async Task<Result<CustomerCancellationResultDto>> RequestCancellationAsync(
        Guid subscriptionId,
        Guid callerUserId,
        UserType callerUserType,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{subscriptionId}' was not found.");

        if (callerUserType == UserType.Customer)
        {
            var isAuthorized = subscription.CustomerUserId == callerUserId
                || (subscription.FamilyGroupId != null && await _db.FamilyGroups.AnyAsync(g => g.Id == subscription.FamilyGroupId && g.ManagerCustomerUserId == callerUserId, cancellationToken));
            if (!isAuthorized)
                return Error.Forbidden("subscription.unauthorized", "You are not authorized to cancel this subscription.");

            if (subscription.FamilyGroupId is Guid familyGroupId)
            {
                var isManager = await _db.FamilyGroups.AnyAsync(g => g.Id == familyGroupId && g.ManagerCustomerUserId == callerUserId, cancellationToken);
                if (!isManager)
                    return Error.Conflict("family.manager_only_action", "Only the family manager can cancel a family subscription.");
            }
        }

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen or SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment))
            return Error.Conflict("subscription.cancellation_not_available", $"Subscription in status '{subscription.Status}' cannot be cancelled.");

        var (quote, refundableDays) = await CalculateDetailsAsync(subscription, cancellationToken);

        // Cancel refundable days
        var cancelledDayIds = new List<Guid>();
        foreach (var day in refundableDays)
        {
            day.Status = SubscriptionDayStatus.Cancelled;
            day.ModifiedAtUtc = _clock.UtcNow;
            cancelledDayIds.Add(day.Id);
        }

        // Release any pending capacity holds
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        // Clean up meal/restaurant selections for cancelled days
        if (cancelledDayIds.Count > 0)
        {
            var mealSelections = await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (mealSelections.Count > 0)
                _db.DailyMealSelections.RemoveRange(mealSelections);

            var restaurantSelections = await _db.DailyRestaurantSelections
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (restaurantSelections.Count > 0)
                _db.DailyRestaurantSelections.RemoveRange(restaurantSelections);
        }

        var previousStatus = subscription.Status;
        subscription.Status = quote.LockedKitchenDays > 0
            ? SubscriptionStatus.CancellationPending
            : SubscriptionStatus.Cancelled;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        var refundedToWallet = false;
        if (quote.RefundToCashWallet && quote.NetRefundAmount > 0)
        {
            var wallet = await _db.CustomerCashWallets
                .FirstOrDefaultAsync(w => w.CustomerUserId == subscription.CustomerUserId, cancellationToken);

            if (wallet is null)
            {
                wallet = new CustomerCashWallet
                {
                    CustomerUserId = subscription.CustomerUserId,
                    Balance = new Money(quote.NetRefundAmount, quote.CurrencyCode),
                    IsActive = true,
                    CreatedAtUtc = _clock.UtcNow
                };
                _db.CustomerCashWallets.Add(wallet);
            }
            else
            {
                wallet.Balance = wallet.Balance.Add(new Money(quote.NetRefundAmount, quote.CurrencyCode));
                wallet.ModifiedAtUtc = _clock.UtcNow;
            }

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                WalletType = WalletType.Cash,
                CustomerUserId = subscription.CustomerUserId,
                Type = WalletTransactionType.Refund,
                Amount = new Money(quote.NetRefundAmount, quote.CurrencyCode),
                BalanceAfter = wallet.Balance,
                ReferenceType = "SubscriptionCancellation",
                ReferenceId = subscription.Id,
                OccurredAtUtc = _clock.UtcNow,
                Description = $"استرداد قيمة الاشتراك الملغي ({subscription.Id})"
            };
            _db.WalletTransactions.Add(tx);
            refundedToWallet = true;
        }

        var operation = new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "CANCELLATION_PROCESSED",
            PreviousStatus = previousStatus,
            NewStatus = subscription.Status,
            Reason = reason?.Trim(),
            PerformedByUserId = callerUserId,
            PerformedByUserType = callerUserType,
            TimestampUtc = _clock.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new
            {
                quote.DailyBoxPrice,
                quote.TotalServiceDays,
                quote.DeliveredDays,
                quote.LockedKitchenDays,
                quote.RefundableDays,
                quote.RefundAmount,
                quote.CancellationFeeAmount,
                quote.NetRefundAmount,
                quote.CurrencyCode,
                refundedToWallet,
                effectiveDate = quote.EffectiveCancellationDate,
                reason = reason?.Trim(),
            }),
        };

        _db.SubscriptionOperations.Add(operation);
        await _db.SaveChangesAsync(cancellationToken);

        if (_audit != null)
        {
            await _audit.LogAsync(new AuditEntry(
                "SubscriptionCancelled",
                nameof(Subscription),
                subscription.Id,
                Before: new { Status = previousStatus },
                After: new { Status = subscription.Status, quote.RefundAmount, quote.RefundableDays, quote.CancellationFeeAmount, quote.NetRefundAmount, refundedToWallet },
                Reason: reason,
                SourceChannel: callerUserType == UserType.Admin ? "admin-portal" : "customer-app"), cancellationToken);
        }

        var leadWindowHours = quote.LockedKitchenDays > 0 ? 72 : 0;
        var statusNameAr = subscription.Status == SubscriptionStatus.Cancelled ? "ملغي" : "إلغاء قيد المتابعة";
        string messageAr;
        if (refundedToWallet)
        {
            messageAr = quote.LockedKitchenDays > 0
                ? $"تم تأكيد طلب الإلغاء. سيتم تسليم طلبات مهلة المطبخ ({leadWindowHours} ساعة - {quote.LockedKitchenDays} يوم) وإيداع صافي المبلغ المسترد {quote.NetRefundAmount:0.000} {quote.CurrencyCode} مباشرة في محفظتك الرقمية."
                : $"تم إلغاء الاشتراك بنجاح وإيداع صافي المبلغ المسترد {quote.NetRefundAmount:0.000} {quote.CurrencyCode} مباشرة في محفظتك الرقمية.";
        }
        else
        {
            messageAr = quote.LockedKitchenDays > 0
                ? $"تم تأكيد طلب الإلغاء. سيتم تسليم طلبات مهلة المطبخ ({leadWindowHours} ساعة - {quote.LockedKitchenDays} يوم) واسترداد مبلغ {quote.NetRefundAmount:0.000} {quote.CurrencyCode} لباقي الأيام ({quote.RefundableDays} يوم) خلال 3-5 أيام عمل."
                : $"تم إلغاء الاشتراك بنجاح. المبلغ المسترد المحسوب هو {quote.NetRefundAmount:0.000} {quote.CurrencyCode} وسيتم تحويله خلال 3-5 أيام عمل.";
        }

        return new CustomerCancellationResultDto(
            operation.Id,
            subscription.Id,
            subscription.Status.ToString(),
            statusNameAr,
            quote.TotalServiceDays,
            quote.DeliveredDays,
            quote.LockedKitchenDays,
            quote.RefundableDays,
            quote.DailyBoxPrice,
            quote.RefundAmount,
            quote.CurrencyCode,
            quote.EffectiveCancellationDate,
            messageAr,
            operation.TimestampUtc,
            quote.CancellationFeeAmount,
            quote.NetRefundAmount,
            refundedToWallet);
    }

    public async Task<Result<CustomerCancellationResultDto>> AdminCancelAsync(
        Guid subscriptionId,
        Guid adminUserId,
        string? reason,
        bool forceImmediate,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{subscriptionId}' was not found.");

        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Completed or SubscriptionStatus.Expired)
            return Error.Conflict("subscription.already_terminated", $"Subscription is already terminated with status '{subscription.Status}'.");

        var days = await _db.SubscriptionDays
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(cancellationToken);

        var totalDays = subscription.DurationDays > 0 ? subscription.DurationDays : (days.Count > 0 ? days.Count : 1);
        var dailyBoxPrice = totalDays > 0 ? Math.Round(subscription.PaidAmount.Amount / totalDays, 3) : 0m;
        var deliveredCount = days.Count(x => x.Status == SubscriptionDayStatus.Delivered);

        List<SubscriptionDay> daysToCancel;
        int lockedCount;
        SubscriptionStatus targetStatus;
        DateOnly effectiveDate;

        if (forceImmediate)
        {
            daysToCancel = days.Where(x => x.Status != SubscriptionDayStatus.Delivered && x.Status != SubscriptionDayStatus.Cancelled).ToList();
            lockedCount = 0;
            targetStatus = SubscriptionStatus.Cancelled;
            effectiveDate = _clock.TodayUtc;
        }
        else
        {
            var (quote, refundable) = await CalculateDetailsAsync(subscription, cancellationToken);
            daysToCancel = refundable;
            lockedCount = quote.LockedKitchenDays;
            targetStatus = lockedCount > 0 ? SubscriptionStatus.CancellationPending : SubscriptionStatus.Cancelled;
            effectiveDate = quote.EffectiveCancellationDate;
        }

        var cancelledDayIds = new List<Guid>();
        foreach (var day in daysToCancel)
        {
            day.Status = SubscriptionDayStatus.Cancelled;
            day.ModifiedAtUtc = _clock.UtcNow;
            cancelledDayIds.Add(day.Id);
        }

        // Release holds
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        if (cancelledDayIds.Count > 0)
        {
            var mealSelections = await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (mealSelections.Count > 0)
                _db.DailyMealSelections.RemoveRange(mealSelections);

            var restaurantSelections = await _db.DailyRestaurantSelections
                .Where(x => cancelledDayIds.Contains(x.SubscriptionDayId))
                .ToListAsync(cancellationToken);
            if (restaurantSelections.Count > 0)
                _db.DailyRestaurantSelections.RemoveRange(restaurantSelections);
        }

        var previousStatus = subscription.Status;
        subscription.Status = targetStatus;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        var refundAmount = Math.Round(daysToCancel.Count * dailyBoxPrice, 3);
        var policySettings = _policyService != null ? await _policyService.GetSettingsAsync(cancellationToken) : new OperationalPolicySettings();
        decimal cancellationFee = 0m;
        if (!forceImmediate && refundAmount > 0)
        {
            if (policySettings.Cancellation.FeeType == CancellationFeeType.Percentage)
            {
                cancellationFee = Math.Round(refundAmount * (policySettings.Cancellation.FeeValue / 100m), 3);
            }
            else if (policySettings.Cancellation.FeeType == CancellationFeeType.FixedAmount)
            {
                cancellationFee = Math.Min(refundAmount, Math.Round(policySettings.Cancellation.FeeValue, 3));
            }
        }
        var netRefund = Math.Max(0m, refundAmount - cancellationFee);

        var refundedToWallet = false;
        if (policySettings.Cancellation.RefundToCashWallet && netRefund > 0)
        {
            var wallet = await _db.CustomerCashWallets
                .FirstOrDefaultAsync(w => w.CustomerUserId == subscription.CustomerUserId, cancellationToken);

            if (wallet is null)
            {
                wallet = new CustomerCashWallet
                {
                    CustomerUserId = subscription.CustomerUserId,
                    Balance = new Money(netRefund, subscription.PaidAmount.CurrencyCode),
                    IsActive = true,
                    CreatedAtUtc = _clock.UtcNow
                };
                _db.CustomerCashWallets.Add(wallet);
            }
            else
            {
                wallet.Balance = wallet.Balance.Add(new Money(netRefund, subscription.PaidAmount.CurrencyCode));
                wallet.ModifiedAtUtc = _clock.UtcNow;
            }

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                WalletType = WalletType.Cash,
                CustomerUserId = subscription.CustomerUserId,
                Type = WalletTransactionType.Refund,
                Amount = new Money(netRefund, subscription.PaidAmount.CurrencyCode),
                BalanceAfter = wallet.Balance,
                ReferenceType = "SubscriptionCancellation",
                ReferenceId = subscription.Id,
                OccurredAtUtc = _clock.UtcNow,
                Description = $"استرداد قيمة الاشتراك الملغي من قبل الإدارة ({subscription.Id})"
            };
            _db.WalletTransactions.Add(tx);
            refundedToWallet = true;
        }

        var opType = forceImmediate ? "ADMIN_CANCELLED_IMMEDIATE" : "ADMIN_CANCELLED";

        var operation = new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = opType,
            PreviousStatus = previousStatus,
            NewStatus = subscription.Status,
            Reason = reason?.Trim(),
            PerformedByUserId = adminUserId,
            PerformedByUserType = UserType.Admin,
            TimestampUtc = _clock.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new
            {
                dailyBoxPrice,
                totalServiceDays = totalDays,
                deliveredDays = deliveredCount,
                lockedKitchenDays = lockedCount,
                refundableDays = daysToCancel.Count,
                refundAmount,
                cancellationFee,
                netRefund,
                refundedToWallet,
                currencyCode = subscription.PaidAmount.CurrencyCode,
                effectiveDate,
                forceImmediate,
                reason = reason?.Trim(),
            }),
        };

        _db.SubscriptionOperations.Add(operation);
        await _db.SaveChangesAsync(cancellationToken);

        if (_audit != null)
        {
            await _audit.LogAsync(new AuditEntry(
                "SubscriptionCancelledByAdmin",
                nameof(Subscription),
                subscription.Id,
                Before: new { Status = previousStatus },
                After: new { Status = subscription.Status, RefundAmount = refundAmount, NetRefund = netRefund, CancellationFee = cancellationFee, RefundableDays = daysToCancel.Count, ForceImmediate = forceImmediate, RefundedToWallet = refundedToWallet },
                Reason: reason,
                SourceChannel: "admin-portal"), cancellationToken);
        }

        var statusNameAr = subscription.Status == SubscriptionStatus.Cancelled ? "ملغي" : "إلغاء قيد المتابعة";
        string messageAr;
        if (refundedToWallet)
        {
            messageAr = forceImmediate
                ? $"تم إلغاء الاشتراك فوريًا من قبل الإدارة وإيداع صافي المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode} في المحفظة."
                : (lockedCount > 0
                    ? $"تم جدولة إلغاء الاشتراك من قبل الإدارة بعد مهلة المطبخ ({lockedCount} يوم) وإيداع صافي المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode} في المحفظة."
                    : $"تم إلغاء الاشتراك بنجاح وإيداع صافي المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode} في المحفظة.");
        }
        else
        {
            messageAr = forceImmediate
                ? $"تم إلغاء الاشتراك فوريًا من قبل الإدارة. المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode}."
                : (lockedCount > 0
                    ? $"تم جدولة إلغاء الاشتراك من قبل الإدارة بعد مهلة المطبخ ({lockedCount} يوم). المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode}."
                    : $"تم إلغاء الاشتراك بنجاح من قبل الإدارة. المبلغ المسترد {netRefund:0.000} {subscription.PaidAmount.CurrencyCode}.");
        }

        return new CustomerCancellationResultDto(
            operation.Id,
            subscription.Id,
            subscription.Status.ToString(),
            statusNameAr,
            totalDays,
            deliveredCount,
            lockedCount,
            daysToCancel.Count,
            dailyBoxPrice,
            refundAmount,
            subscription.PaidAmount.CurrencyCode,
            effectiveDate,
            messageAr,
            operation.TimestampUtc,
            cancellationFee,
            netRefund,
            refundedToWallet);
    }

    public async Task<Result> CancelUncompletedSubscriptionAsync(
        Guid subscriptionId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == subscriptionId && (x.CustomerUserId == callerUserId || (x.FamilyGroupId != null && _db.FamilyGroups.Any(g => g.Id == x.FamilyGroupId && g.ManagerCustomerUserId == callerUserId))), cancellationToken);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{subscriptionId}' was not found.");

        if (subscription.Status is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Expired))
            return Error.Conflict("subscription.cannot_delete_active_subscription", "Cannot delete an active subscription.");

        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscriptionId && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);

        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        var previousStatus = subscription.Status;
        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscriptionId,
            OperationType = "CUSTOMER_CANCELLED_UNCOMPLETED",
            PreviousStatus = previousStatus,
            NewStatus = SubscriptionStatus.Cancelled,
            Reason = "Customer deleted uncompleted subscription",
            PerformedByUserId = callerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<(CustomerCancellationQuoteDto Quote, List<SubscriptionDay> RefundableDays)> CalculateDetailsAsync(
        Subscription subscription,
        CancellationToken ct)
    {
        var days = await _db.SubscriptionDays
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);

        var totalDays = subscription.DurationDays > 0 ? subscription.DurationDays : (days.Count > 0 ? days.Count : 1);
        var dailyBoxPrice = totalDays > 0 ? Math.Round(subscription.PaidAmount.Amount / totalDays, 3) : 0m;

        var today = _clock.TodayUtc;
        var policySettings = _policyService != null ? await _policyService.GetSettingsAsync(ct) : new OperationalPolicySettings();
        var leadWindowHours = policySettings.Cutoff.CutoffLeadHours > 0 ? policySettings.Cutoff.CutoffLeadHours : 72;
        var leadWindowDays = Math.Max(1, (int)Math.Ceiling(leadWindowHours / 24.0));
        var leadWindowEnd = today.AddDays(Math.Max(0, leadWindowDays - 1));

        var deliveredCount = days.Count(x => x.Status == SubscriptionDayStatus.Delivered);

        var lockedDays = days.Where(x =>
            x.Status != SubscriptionDayStatus.Delivered &&
            x.Status != SubscriptionDayStatus.Cancelled &&
            (x.ServiceDate <= leadWindowEnd ||
             x.Status == SubscriptionDayStatus.Locked72h ||
             x.Status == SubscriptionDayStatus.SentToRestaurant ||
             x.Status == SubscriptionDayStatus.ConfirmedByRestaurant ||
             x.Status == SubscriptionDayStatus.Preparing ||
             x.Status == SubscriptionDayStatus.OutForDelivery)
        ).ToList();

        var refundableDays = days.Where(x =>
            x.ServiceDate > leadWindowEnd &&
            x.Status != SubscriptionDayStatus.Delivered &&
            x.Status != SubscriptionDayStatus.Cancelled &&
            x.Status != SubscriptionDayStatus.Locked72h &&
            x.Status != SubscriptionDayStatus.SentToRestaurant &&
            x.Status != SubscriptionDayStatus.ConfirmedByRestaurant &&
            x.Status != SubscriptionDayStatus.Preparing &&
            x.Status != SubscriptionDayStatus.OutForDelivery
        ).ToList();

        int refundableDaysCount;
        decimal refundAmount;
        DateOnly effectiveCancellationDate;

        if (days.Count == 0)
        {
            refundableDaysCount = totalDays;
            refundAmount = subscription.PaidAmount.Amount;
            effectiveCancellationDate = today;
        }
        else
        {
            refundableDaysCount = refundableDays.Count;
            refundAmount = Math.Round(refundableDaysCount * dailyBoxPrice, 3);
            effectiveCancellationDate = lockedDays.Count > 0
                ? lockedDays.Max(x => x.ServiceDate).AddDays(1)
                : today;
        }

        decimal cancellationFeeAmount = 0m;
        if (refundAmount > 0)
        {
            if (policySettings.Cancellation.FeeType == CancellationFeeType.Percentage)
            {
                cancellationFeeAmount = Math.Round(refundAmount * (policySettings.Cancellation.FeeValue / 100m), 3);
            }
            else if (policySettings.Cancellation.FeeType == CancellationFeeType.FixedAmount)
            {
                cancellationFeeAmount = Math.Min(refundAmount, Math.Round(policySettings.Cancellation.FeeValue, 3));
            }
        }
        var netRefundAmount = Math.Max(0m, refundAmount - cancellationFeeAmount);

        var currency = subscription.PaidAmount.CurrencyCode;
        string policyMessageAr;
        if (lockedDays.Count > 0)
        {
            policyMessageAr = cancellationFeeAmount > 0
                ? $"طلبات مهلة المطبخ ({leadWindowHours} ساعة - {lockedDays.Count} يوم) مؤكدة مع المطبخ وسيتم توصيلها، ويسترد ثمن باقي الأيام ({refundableDaysCount} يوم) بقيمة {refundAmount:0.000} {currency} بعد خصم رسوم الإلغاء ({cancellationFeeAmount:0.000} {currency}) ليكون الصافي {netRefundAmount:0.000} {currency}."
                : $"طلبات مهلة المطبخ ({leadWindowHours} ساعة - {lockedDays.Count} يوم) مؤكدة مع المطبخ وسيتم توصيلها، ويسترد ثمن باقي الأيام ({refundableDaysCount} يوم) بقيمة {refundAmount:0.000} {currency}.";
        }
        else
        {
            policyMessageAr = cancellationFeeAmount > 0
                ? $"سيتم إلغاء الاشتراك واسترداد صافي مبلغ {netRefundAmount:0.000} {currency} بعد خصم رسوم الإلغاء ({cancellationFeeAmount:0.000} {currency}) للأيام المتبقية ({refundableDaysCount} يوم)."
                : $"سيتم إلغاء الاشتراك واسترداد مبلغ {refundAmount:0.000} {currency} بالكامل للأيام المتبقية ({refundableDaysCount} يوم).";
        }

        var quote = new CustomerCancellationQuoteDto(
            subscription.Id,
            totalDays,
            deliveredCount,
            lockedDays.Count,
            refundableDaysCount,
            dailyBoxPrice,
            refundAmount,
            currency,
            effectiveCancellationDate,
            policyMessageAr,
            cancellationFeeAmount,
            netRefundAmount,
            policySettings.Cancellation.RefundToCashWallet);

        return (quote, refundableDays);
    }
}
