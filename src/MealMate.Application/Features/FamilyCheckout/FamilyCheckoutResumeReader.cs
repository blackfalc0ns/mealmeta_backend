using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyCheckoutResumeReader
{
    public const string RetryEndpoint = "/api/v1/customer/family-subscription-flow/retry-payment";

    public static async Task<FamilyCheckoutStartedDto?> GetAsync(IAppDbContext db, Guid groupId, DateTime now, CancellationToken ct)
    {
        var batch = await db.FamilyCheckoutBatches.AsNoTracking().Include(b => b.PaymentAttempts)
            .Where(b => b.FamilyGroupId == groupId && b.Purpose == "Initial"
                && b.Status == FamilyCheckoutBatchStatus.PaymentPending && b.ExpiresAtUtc > now)
            .OrderByDescending(b => b.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (batch?.PaymentAnchorSubscriptionId is not Guid anchor || batch.ExpiresAtUtc is not DateTime expires)
            return null;
        var attempt = batch.PaymentAttempts.Where(a => a.ResultCode == "Initialized"
            && a.GatewayPaymentId == batch.GatewayPaymentId && !string.IsNullOrWhiteSpace(a.GatewayPaymentUrl))
            .OrderByDescending(a => a.RequestedAtUtc).FirstOrDefault();
        return attempt is null ? null : new FamilyCheckoutStartedDto(groupId, anchor, attempt.GatewayPaymentUrl!,
            expires, batch.FinalPrice, batch.Id, batch.Version);
    }

    public static Task<bool> CanRetryAsync(IAppDbContext db, Guid groupId, CancellationToken ct)
        => db.FamilyCheckoutBatches.AsNoTracking().AnyAsync(b => b.FamilyGroupId == groupId && b.Purpose == "Initial"
            && b.PaidAtUtc == null && (b.Status == FamilyCheckoutBatchStatus.PaymentPending
                || b.Status == FamilyCheckoutBatchStatus.Expired || b.Status == FamilyCheckoutBatchStatus.Failed)
            && b.PaymentAttempts.Any(a => a.ResultCode == "Initialized" || a.ResultCode == "Failed"), ct);

    public static async Task RecoverExpiredAsync(IAppDbContext db, IDateTimeProvider clock,
        ICapacityHoldExpiryService? expiry, Guid managerId, CancellationToken ct)
    {
        if (expiry is null) return;
        var due = await db.FamilyCheckoutBatches.AsNoTracking().AnyAsync(b => b.PayerCustomerUserId == managerId
            && b.Status == FamilyCheckoutBatchStatus.PaymentPending && b.ExpiresAtUtc <= clock.UtcNow, ct);
        if (due) await expiry.ExpireDueAsync(ct);
    }
}
