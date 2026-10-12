using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Renewal;

public sealed class SubscriptionRenewalService : ISubscriptionRenewalService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IFamilySubscriptionAccess _familyAccess;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly ISubscriptionScheduleService _schedule;

    public SubscriptionRenewalService(IAppDbContext db, IDateTimeProvider clock, IFamilySubscriptionAccess? familyAccess = null,
        IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null,
        ISubscriptionScheduleService? schedule = null)
    {
        _db = db;
        _clock = clock;
        _familyAccess = familyAccess ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
        _transactions = transactions;
        _locks = locks;
        _schedule = schedule ?? new SubscriptionScheduleService(clock);
    }

    public async Task<Result<CustomerRenewalRequestDto>> RequestRenewalAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);

        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var authorization = await _familyAccess.AuthorizeAsync(subscription, customerUserId, FamilyAction.RequestRenewal, ct);
        if (authorization.IsFailure) return authorization.Error;
        if (subscription.FamilyGroupId is Guid familyGroupId
            && (subscription.FamilyMemberId is not Guid familyMemberId
                || !await _db.FamilyMembers.AnyAsync(x => x.Id == familyMemberId && x.FamilyGroupId == familyGroupId
                    && !x.IsDeleted && x.MemberSubscriptionId == subscriptionId, ct)))
            return Error.Conflict("family.renewal_pointer_changed");

        if (subscription.Status is SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed
            or SubscriptionStatus.Cancelled or SubscriptionStatus.CancellationPending)
            return Error.Conflict("subscription.renewal_not_available", "Renewal is not available for this subscription status.");

        var policy = await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, ct);
        var businessToday = subscription.FamilyGroupId.HasValue ? BusinessCalendarDate.KuwaitToday(_clock.UtcNow) : _clock.TodayUtc;
        if (!MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.IsRenewalAvailable(policy, subscription.EndDate, businessToday))
            return Error.Conflict("subscription.renewal_not_available", "Renewal is disabled or its availability window has not started.");

        if (subscription.FamilyGroupId is Guid groupId && _transactions is not null && _locks is not null)
        {
            var locked = await _transactions.ExecuteResultAsync<CustomerRenewalRequestDto>(async token =>
            {
                await _locks.LockFamilyGroupAsync(groupId, token);
                await _locks.LockSubscriptionAsync(subscriptionId, token);
                await _locks.LockSubscriptionDaysAsync(subscriptionId, token);
                var current = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, token);
                if (current is null || current.FamilyGroupId != groupId)
                    return Error.Conflict("family.renewal_intent_changed");
                var currentAuthorization = await _familyAccess.AuthorizeAsync(current, customerUserId, FamilyAction.RequestRenewal, token);
                if (currentAuthorization.IsFailure) return currentAuthorization.Error;
                if (current.FamilyMemberId is not Guid currentMemberId
                    || !await _db.FamilyMembers.AnyAsync(x => x.Id == currentMemberId && x.FamilyGroupId == groupId
                        && !x.IsDeleted && x.MemberSubscriptionId == subscriptionId, token))
                    return Error.Conflict("family.renewal_pointer_changed");
                var request = await _db.SubscriptionRenewals
                    .Where(x => x.PreviousSubscriptionId == subscriptionId)
                    .OrderByDescending(x => x.RequestedAtUtc)
                    .FirstOrDefaultAsync(token);
                if (request is not null && request.FamilyGroupId == groupId && request.BatchId is Guid priorBatchId
                    && await IsProvenTerminalUnpaidBatchAsync(priorBatchId, token))
                {
                    var priorBatchStatus = await _db.FamilyCheckoutBatches.AsNoTracking()
                        .Where(x => x.Id == priorBatchId).Select(x => x.Status).SingleAsync(token);
                    request.Status = priorBatchStatus == FamilyCheckoutBatchStatus.Failed ? "Failed" : "Expired";
                    request = new SubscriptionRenewal
                    {
                        PreviousSubscriptionId = subscriptionId, Status = "Requested", RequestedAtUtc = _clock.UtcNow,
                        FamilyGroupId = groupId, FamilyMemberId = current.FamilyMemberId,
                    };
                    _db.SubscriptionRenewals.Add(request);
                    await _db.SaveChangesAsync(token);
                }
                if (request is null)
                {
                    request = new SubscriptionRenewal
                    {
                        PreviousSubscriptionId = subscriptionId, Status = "Requested", RequestedAtUtc = _clock.UtcNow,
                        FamilyGroupId = groupId, FamilyMemberId = current.FamilyMemberId,
                    };
                    _db.SubscriptionRenewals.Add(request);
                    await _db.SaveChangesAsync(token);
                }
                return await BuildFamilyRenewalDtoAsync(current, request, customerUserId, token);
            }, ct);
            return locked;
        }

        var request = await _db.SubscriptionRenewals
            .Where(x => x.PreviousSubscriptionId == subscriptionId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (request is null)
        {
            request = new SubscriptionRenewal
            {
                PreviousSubscriptionId = subscriptionId,
                Status = "Requested",
                RequestedAtUtc = _clock.UtcNow,
                FamilyGroupId = subscription.FamilyGroupId,
                FamilyMemberId = subscription.FamilyMemberId,
            };
            _db.SubscriptionRenewals.Add(request);
            await _db.SaveChangesAsync(ct);
        }
        else if (subscription.FamilyGroupId.HasValue && request.FamilyGroupId is null)
        {
            request.FamilyGroupId = subscription.FamilyGroupId;
            request.FamilyMemberId = subscription.FamilyMemberId;
            await _db.SaveChangesAsync(ct);
        }

        var familyRenewal = subscription.FamilyGroupId.HasValue;
        if (familyRenewal) return await BuildFamilyRenewalDtoAsync(subscription, request, customerUserId, ct);
        return new CustomerRenewalRequestDto(
            request.Id,
            subscriptionId,
            request.Status,
            subscription.ProgramId,
            subscription.BundleId,
            subscription.SelectedTier,
            subscription.DurationDays,
            subscription.IncludeFriday,
            subscription.RegionId,
            subscription.EndDate.AddDays(1),
            familyRenewal ? null : "/api/v1/customer/subscription-flow/configuration",
            familyRenewal ? null : "/api/v1/customer/subscriptions/checkout",
            subscription.FamilyGroupId,
            familyRenewal ? subscription.FamilyMemberId : null,
            familyRenewal ? $"/api/v1/customer/family-subscription-flow/renewals/{request.Id}/checkout" : null,
            request.RequestedStartDate,
            request.BatchId,
            request.BatchVersion,
            familyRenewal ? "FamilyCheckout" : "IndividualCheckout");
    }

    private async Task<bool> IsProvenTerminalUnpaidBatchAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _db.FamilyCheckoutBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null || batch.Purpose != "Renewal" || batch.Status == FamilyCheckoutBatchStatus.Paid
            || batch.Status == FamilyCheckoutBatchStatus.ReviewRequired)
            return false;
        var attempts = await _db.FamilyCheckoutPaymentAttempts.AsNoTracking().Where(x => x.BatchId == batchId).ToListAsync(ct);
        if (attempts.Any(x => x.ResultCode == "Paid" || x.VerifiedAtUtc.HasValue && x.ResultCode != "Failed"))
            return false;
        if (batch.Status == FamilyCheckoutBatchStatus.Failed)
            return attempts.Any(x => x.ResultCode == "Failed" && x.VerifiedAtUtc.HasValue);
        if (batch.Status is FamilyCheckoutBatchStatus.Expired or FamilyCheckoutBatchStatus.Cancelled)
            return string.IsNullOrWhiteSpace(batch.GatewayPaymentId) && attempts.All(x => string.IsNullOrWhiteSpace(x.GatewayPaymentId)
                && string.IsNullOrWhiteSpace(x.GatewayPaymentUrl)
                && x.ResultCode is not ("Initializing" or "InitializationUncertain" or "Initialized"));
        return false;
    }

    private async Task<CustomerRenewalRequestDto> BuildFamilyRenewalDtoAsync(Subscription previous,
        SubscriptionRenewal request, Guid actorUserId, CancellationToken ct)
    {
        var batch = request.BatchId is Guid batchId
            ? await _db.FamilyCheckoutBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId, ct) : null;
        var line = batch is null ? null : await _db.FamilyCheckoutBatchLines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BatchId == batch.Id && x.RenewalRequestId == request.Id && x.CheckoutSelected, ct);
        string? paymentUrl = null;
        if (batch?.Purpose == "Renewal" && batch.RenewalRequestId == request.Id
            && batch.FamilyGroupId == previous.FamilyGroupId && batch.PayerCustomerUserId == actorUserId
            && line?.PreviousSubscriptionId == previous.Id && line.FamilyMemberId == previous.FamilyMemberId
            && line.SubscriptionId is Guid nextId)
        {
            var attempt = await _db.FamilyCheckoutPaymentAttempts.AsNoTracking()
                .Where(x => x.BatchId == batch.Id).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
            var next = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nextId, ct);
            var member = previous.FamilyMemberId is Guid memberId
                ? await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberId
                    && x.FamilyGroupId == previous.FamilyGroupId && !x.IsDeleted, ct) : null;
            var proposedDates = line.StartDate is DateOnly savedStartDate
                ? _schedule.GetServiceDates(savedStartDate, previous.DurationDays, previous.IncludeFriday) : [];
            var nextIdForOverlap = (Guid?)nextId;
            var overlapsAnotherService = proposedDates.Count == 0 || await _db.Subscriptions.AsNoTracking().AnyAsync(x =>
                x.FamilyGroupId == previous.FamilyGroupId && x.FamilyMemberId == previous.FamilyMemberId
                && x.Id != previous.Id && (!nextIdForOverlap.HasValue || x.Id != nextIdForOverlap.Value)
                && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Active
                    || x.Status == SubscriptionStatus.Scheduled || x.Status == SubscriptionStatus.Frozen
                    || x.Status == SubscriptionStatus.SuspendedForReview || x.Status == SubscriptionStatus.CancellationPending
                    || x.Status == SubscriptionStatus.PendingPayment)
                && x.StartDate <= proposedDates[proposedDates.Count - 1] && x.EndDate >= proposedDates[0], ct);
            var pendingSafe = batch.Status == FamilyCheckoutBatchStatus.PaymentPending
                && request.Status == "PaymentPending" && batch.ExpiresAtUtc > _clock.UtcNow
                && attempt?.ResultCode == "Initialized" && !string.IsNullOrWhiteSpace(attempt.GatewayPaymentUrl)
                && next?.Status == SubscriptionStatus.PendingPayment && member?.MemberSubscriptionId == previous.Id
                && line.StartDate == request.RequestedStartDate && line.DeliveryAddressId == request.RequestedAddressId
                && next.StartDate == request.RequestedStartDate
                && previous.Status is SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled
                    or SubscriptionStatus.Frozen or SubscriptionStatus.SuspendedForReview
                    or SubscriptionStatus.Completed or SubscriptionStatus.Expired
                && (previous.PaymentVerifiedAtUtc.HasValue || await _db.FamilyCheckoutBatchLines.AsNoTracking()
                    .AnyAsync(x => x.SubscriptionId == previous.Id && x.CheckoutSelected
                        && _db.FamilyCheckoutBatches.Any(b => b.Id == x.BatchId && b.Status == FamilyCheckoutBatchStatus.Paid), ct))
                && proposedDates.Count > 0 && proposedDates[0] > previous.EndDate && !overlapsAnotherService;
            var paidSafe = batch.Status == FamilyCheckoutBatchStatus.Paid && request.Status == "Completed"
                && request.NewSubscriptionId == nextId && attempt?.ResultCode == "Paid"
                && !string.IsNullOrWhiteSpace(attempt.GatewayPaymentUrl)
                && next is not null && next.FamilyGroupId == previous.FamilyGroupId
                && next.FamilyMemberId == previous.FamilyMemberId;
            if (pendingSafe || paidSafe) paymentUrl = attempt!.GatewayPaymentUrl;
        }
        return new CustomerRenewalRequestDto(
            request.Id, previous.Id, request.Status, previous.ProgramId, previous.BundleId, previous.SelectedTier,
            previous.DurationDays, previous.IncludeFriday, previous.RegionId, previous.EndDate.AddDays(1), null, null,
            previous.FamilyGroupId, previous.FamilyMemberId,
            $"/api/v1/customer/family-subscription-flow/renewals/{request.Id}/checkout",
            request.RequestedStartDate, request.BatchId, request.BatchVersion, "FamilyCheckout", line?.SubscriptionId,
            batch?.Status.ToString(), PaymentUrl: paymentUrl);
    }
}
