using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed record FamilySubscriptionAccess(bool IsManager, Guid? MemberId);

public interface IFamilySubscriptionAccess
{
    Task<Result<FamilySubscriptionAccess>> AuthorizeAsync(Subscription subscription, Guid actorUserId,
        FamilyAction action, CancellationToken cancellationToken = default);
    Task<Result<FamilySubscriptionAccess>> AuthorizeFinancialHistoryAsync(Subscription subscription, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class FamilySubscriptionAccessService(IAppDbContext db, IFamilyActionPolicy policy)
    : IFamilySubscriptionAccess
{
    public async Task<Result<FamilySubscriptionAccess>> AuthorizeFinancialHistoryAsync(Subscription subscription, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var live = await AuthorizeAsync(subscription, actorUserId, FamilyAction.ViewInvoices, cancellationToken);
        if (live.IsSuccess || subscription.FamilyGroupId is not Guid groupId) return live;
        var group = await db.FamilyGroups.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken);
        if (group is null || (group.Status != FamilyGroupStatus.Cancelled && !group.IsDeleted
            && subscription.Status is not (SubscriptionStatus.Completed or SubscriptionStatus.Expired or SubscriptionStatus.Cancelled or SubscriptionStatus.Upgraded)))
            return live;
        // Financial history is deliberately narrower than plan/calendar access. Authentic paid
        // payer evidence survives a manager change; it never grants current service access.
        var paid = await db.FamilyCheckoutBatchLines.AsNoTracking().Include(x => x.Batch)
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.FamilyMemberId == subscription.FamilyMemberId
                && x.Batch.FamilyGroupId == groupId && x.Batch.Status == FamilyCheckoutBatchStatus.Paid
                && (x.Batch.PayerCustomerUserId == actorUserId || group.ManagerCustomerUserId == actorUserId), cancellationToken);
        if (paid is not null) return new FamilySubscriptionAccess(false, paid.FamilyMemberId);
        var verifiedUpgradePayer = await (from upgrade in db.SubscriptionTierUpgrades.AsNoTracking()
            join receipt in db.PaymentWebhookReceipts.AsNoTracking() on new { upgrade.SubscriptionId, PaymentId = upgrade.PaymentId }
                equals new { SubscriptionId = receipt.SubscriptionId ?? Guid.Empty, PaymentId = receipt.PaymentId }
            where upgrade.SubscriptionId == subscription.Id && upgrade.PayerCustomerUserId == actorUserId
                && upgrade.VerifiedPaidAmount.HasValue && upgrade.VerifiedCurrencyCode != null
                && upgrade.VerifiedPaidAtUtc.HasValue && receipt.VerifiedAtUtc.HasValue
                && receipt.Amount == upgrade.VerifiedPaidAmount && receipt.CurrencyCode == upgrade.VerifiedCurrencyCode
                && receipt.Status != "PaymentFailureVerified" && receipt.Status != "VerificationRejected"
            select upgrade.Id).AnyAsync(cancellationToken);
        return verifiedUpgradePayer ? new FamilySubscriptionAccess(false, subscription.FamilyMemberId) : live;
    }

    public async Task<Result<FamilySubscriptionAccess>> AuthorizeAsync(Subscription subscription, Guid actorUserId,
        FamilyAction action, CancellationToken cancellationToken = default)
    {
        if (subscription.FamilyGroupId is not Guid groupId)
            return subscription.CustomerUserId == actorUserId
                ? new FamilySubscriptionAccess(false, null)
                : Error.NotFound("common.not_found");
        if (!await db.FamilyGroups.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == groupId, cancellationToken))
            return Error.Conflict("family.group_not_active");
        // Resolve the historical member FK, never another member's current pointer.
        var member = await db.FamilyMembers.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.FamilyGroupId == groupId && (subscription.FamilyMemberId.HasValue
                ? x.Id == subscription.FamilyMemberId.Value
                : x.MemberSubscriptionId == subscription.Id), cancellationToken);
        if (member is null) return Error.NotFound("family.member_not_found");
        var decision = await policy.EvaluateAsync(actorUserId, member.Id, action, cancellationToken);
        if (!decision.Allowed)
            return Error.Forbidden(decision.ReasonCode ?? "family.permission_required");
        var manager = await db.FamilyGroups.AsNoTracking()
            .AnyAsync(x => x.Id == groupId && x.ManagerCustomerUserId == actorUserId, cancellationToken);
        return new FamilySubscriptionAccess(manager, member.Id);
    }
}
