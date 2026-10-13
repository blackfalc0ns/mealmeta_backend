using MealMate.Application.Common.Models;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Application.Features.FamilyCheckout;

public interface IFamilyPaymentRetryService
{
    Task<Result<FamilyCheckoutStartedDto>> RetryAsync(FamilyStartCheckoutRequest request, CancellationToken ct = default);
}

public sealed class FamilyPaymentRetryService(IAppDbContext db, ICurrentUser user, IDateTimeProvider clock,
    IFamilySubscriptionFlowService flow, ICapacityHoldExpiryService expiry,
    ICheckoutTransactionExecutor transactions, IApplicationLockManager locks,
    IConfiguration? configuration = null) : IFamilyPaymentRetryService
{
    public async Task<Result<FamilyCheckoutStartedDto>> RetryAsync(FamilyStartCheckoutRequest request, CancellationToken ct = default)
    {
        if (user.UserType != UserType.Customer || user.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden");
        if (!FamilySubscriptionFlowService.IsAllowedRedirectUrl(request.CallbackUrl, configuration)
            || !FamilySubscriptionFlowService.IsAllowedRedirectUrl(request.ErrorUrl, configuration))
            return Error.Validation("checkout.redirect_url_not_allowed");
        await FamilyCheckoutResumeReader.RecoverExpiredAsync(db, clock, expiry, managerId, ct);
        var groupId = await db.FamilyGroups.AsNoTracking().Where(g => g.ManagerCustomerUserId == managerId
            && (g.Status == FamilyGroupStatus.Draft || g.Status == FamilyGroupStatus.CheckoutPending || g.Status == FamilyGroupStatus.Active))
            .Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        if (groupId is null) return Error.NotFound("family.not_found");

        var prepared = await transactions.ExecuteAsync<Result<FamilyStartCheckoutRequest>>(async token =>
        {
            await locks.LockFamilyGroupAsync(groupId.Value, token);
            if (await db.FamilyCheckoutBatches.AnyAsync(b => b.FamilyGroupId == groupId.Value
                && b.Status == FamilyCheckoutBatchStatus.ReviewRequired, token))
                return Error.Conflict("family.batch_review_required");
            var open = await db.FamilyCheckoutBatches.Include(b => b.Lines)
                .Where(b => b.FamilyGroupId == groupId.Value && b.Purpose == "Initial"
                    && (b.Status == FamilyCheckoutBatchStatus.Draft || b.Status == FamilyCheckoutBatchStatus.PaymentPending))
                .OrderByDescending(b => b.CreatedAtUtc).FirstOrDefaultAsync(token);
            if (open?.Status != FamilyCheckoutBatchStatus.PaymentPending)
            {
                var previous = await db.FamilyCheckoutBatches.AsNoTracking().Include(b => b.Lines)
                    .Where(b => b.FamilyGroupId == groupId.Value && b.Purpose == "Initial" && b.PaidAtUtc == null
                        && (b.Status == FamilyCheckoutBatchStatus.Expired || b.Status == FamilyCheckoutBatchStatus.Failed)
                        && b.PaymentAttempts.Any(a => a.ResultCode == "Initialized" || a.ResultCode == "Failed"))
                    .OrderByDescending(b => b.CreatedAtUtc).FirstOrDefaultAsync(token);
                if (previous is null) return Error.NotFound("family.not_found");
                if (open is null)
                {
                    var ids = previous.Lines.Where(l => l.CheckoutSelected).Select(l => l.FamilyMemberId).ToHashSet();
                    var members = await db.FamilyMembers.AsNoTracking().Where(m => m.FamilyGroupId == groupId.Value && ids.Contains(m.Id)
                        && !db.FamilyMemberAccesses.Any(a => a.FamilyMemberId == m.Id && a.RemovedAtUtc != null)).ToListAsync(token);
                    if (members.Count != ids.Count) return Error.Conflict("family.member_not_active");
                    open = new FamilyCheckoutBatch { FamilyGroupId = groupId.Value, PayerCustomerUserId = managerId,
                        IdempotencyKey = "retry-" + Guid.NewGuid().ToString("N"), CreatedAtUtc = clock.UtcNow };
                    foreach (var member in members)
                        open.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = member.Id, CheckoutSelected = true,
                            ProgramId = member.ProgramId, BundleId = member.BundleId, Tier = member.SelectedTier,
                            DurationDays = member.DurationDays, IncludeFriday = member.IncludeFriday,
                            StartDate = member.StartDate, DeliveryAddressId = member.DeliveryAddressId, CreatedAtUtc = clock.UtcNow });
                    db.FamilyCheckoutBatches.Add(open);
                    await db.SaveChangesAsync(token);
                }
            }
            return Result.Success(request with { BatchId = open!.Id, BatchVersion = open.Version, IdempotencyKey = open.IdempotencyKey });
        }, ct);
        if (prepared.IsFailure) return prepared.Error;
        // The existing checkout owns all pricing, eligibility, gateway initialization
        // and retry idempotency. A pending attempt reuses its saved receipt.
        return await flow.StartCheckoutAsync(prepared.Value, ct);
    }
}
