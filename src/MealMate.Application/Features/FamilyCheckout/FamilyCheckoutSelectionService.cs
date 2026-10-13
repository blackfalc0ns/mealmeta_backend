using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>
/// Explicit "subscribe now" / "subscribe later" choice for one family member.
/// Selection never buys anything by itself and never clears the member's plan:
/// it only maintains CheckoutSelected on the group's open draft batch, which
/// the quote/gate/checkout then honor. Adding a member defaults to deferred
/// (no selection) — plan completion must not silently charge anyone.
/// </summary>
public sealed record SetFamilyCheckoutSelectionRequest(bool SubscribeNow);

public sealed record SetFamilyCheckoutSelectionCommand(Guid MemberId, bool SubscribeNow) : ICommand<FamilyCheckoutSelectionDto>;

public sealed record FamilyCheckoutSelectionDto(
    Guid BatchId,
    long BatchVersion,
    Guid MemberId,
    bool CheckoutSelected,
    int SelectedNewSubscriberCount);

public interface IFamilyCheckoutSelectionService
{
    Task<Result<FamilyCheckoutSelectionDto>> SetAsync(SetFamilyCheckoutSelectionCommand command, CancellationToken cancellationToken);
}

public sealed class FamilyCheckoutSelectionService : IFamilyCheckoutSelectionService,
    IRequestHandler<SetFamilyCheckoutSelectionCommand, Result<FamilyCheckoutSelectionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly IApplicationLockManager _locks;
    private readonly FamilyParticipationContextReader? _participationContextReader;

    public FamilyCheckoutSelectionService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICheckoutTransactionExecutor transactions,
        IApplicationLockManager locks,
        FamilyParticipationContextReader? participationContextReader = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _transactions = transactions;
        _locks = locks;
        _participationContextReader = participationContextReader;
    }

    public Task<Result<FamilyCheckoutSelectionDto>> Handle(SetFamilyCheckoutSelectionCommand request, CancellationToken cancellationToken)
        => SetAsync(request, cancellationToken);

    public async Task<Result<FamilyCheckoutSelectionDto>> SetAsync(
        SetFamilyCheckoutSelectionCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId
                    && (x.Status == FamilyGroupStatus.Draft
                        || x.Status == FamilyGroupStatus.CheckoutPending
                        || x.Status == FamilyGroupStatus.Active),
                cancellationToken);
        if (group is null)
            return Error.NotFound("family.not_found", "No family group was found.");
        if (group.Status == FamilyGroupStatus.CheckoutPending)
            return Error.Conflict("family.checkout_pending", "A family checkout payment is in progress.");

        var member = group.Members.FirstOrDefault(x => x.Id == command.MemberId);
        if (member is null)
            return Error.NotFound("family.not_found", "Family member was not found.");
        var removedAccess = await _db.FamilyMemberAccesses.AsNoTracking()
            .AnyAsync(a => a.FamilyMemberId == member.Id && a.RemovedAtUtc != null, cancellationToken);
        if (removedAccess)
            return Error.Conflict("family.member_not_active", "This member's access was removed.");

        // B02: a PaymentPending batch is the frozen financial quote currently
        // awaiting a gateway callback; participants may not change under it.
        // R04: the batch-freeze race is closed with a transaction + family-group
        // row lock shared with checkout, and the batch state is re-checked INSIDE
        // the protected scope — the pre-check alone can read a stale Draft.
        return await _transactions.ExecuteAsync<Result<FamilyCheckoutSelectionDto>>(async transactionCt =>
        {
            await _locks.LockFamilyGroupAsync(group.Id, transactionCt);
            await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, transactionCt, managerId, groupLockHeld: true);

            member = await _db.FamilyMembers.FirstOrDefaultAsync(x => x.Id == command.MemberId && x.FamilyGroupId == group.Id, transactionCt);
            if (member is null
                || await _db.FamilyMemberAccesses.AnyAsync(x => x.FamilyMemberId == command.MemberId && x.RemovedAtUtc != null, transactionCt))
                return Error.Conflict("family.already_subscribed", "family.already_subscribed");

            if (member.MemberSubscriptionId is Guid existingSubscriptionId
                && !await CanPurchaseAgainAsync(group, member, existingSubscriptionId, transactionCt))
                return Error.Conflict("family.already_subscribed", "This member already has a live subscription.");

            var consumesManagerExemption = false;
            if (command.SubscribeNow && member.Role == FamilyMemberRole.Manager)
            {
                if (await HasLiveIndividualServiceAsync(group, member, transactionCt))
                    return Error.Conflict("family.already_subscribed", "The manager's individual service must end before family service starts.");

                var legacyExemption = group.ManagerIndividualSeatExempt
                    ?? (group.ConvertedFromSubscriptionId is not null
                        && member.MemberSubscriptionId == group.ConvertedFromSubscriptionId);
                if (legacyExemption)
                {
                    group.ManagerIndividualSeatExempt = false;
                    consumesManagerExemption = true;
                }
            }

            var openStatus = await _db.FamilyCheckoutBatches.AsNoTracking()
                .Where(x => x.FamilyGroupId == group.Id
                    && (x.Status == FamilyCheckoutBatchStatus.Draft
                        || x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (FamilyCheckoutBatchStatus?)x.Status)
                .FirstOrDefaultAsync(transactionCt);
            if (openStatus == FamilyCheckoutBatchStatus.ReviewRequired)
                return Error.Conflict("family.batch_review_required", "family.batch_review_required");
            if (openStatus == FamilyCheckoutBatchStatus.PaymentPending)
                return Error.Conflict("family.checkout_pending", "A family checkout payment is in progress. Complete or cancel it before changing the selection.");

            if (consumesManagerExemption && _participationContextReader is not null)
            {
                var selectedIds = await GetSelectedMemberIdsOrNullAsync(_db, group.Id, transactionCt)
                    ?? new HashSet<Guid>();
                selectedIds = selectedIds.Append(member.Id).ToHashSet();
                var participation = await _participationContextReader.GetAsync(group, selectedIds, transactionCt);
                if (participation.MaxSeats > 0 && participation.OccupiedSeats > participation.MaxSeats)
                {
                    group.ManagerIndividualSeatExempt = true;
                    return Error.Conflict("family.max_members", "The selected family package does not have enough seats for this household.");
                }
            }

            var batch = await GetOrCreateDraftBatchAsync(group, managerId, transactionCt);
            var line = await _db.FamilyCheckoutBatchLines
                .FirstOrDefaultAsync(x => x.BatchId == batch.Id && x.FamilyMemberId == member.Id, transactionCt);
            if (line is null)
            {
                line = new FamilyCheckoutBatchLine
                {
                    BatchId = batch.Id,
                    FamilyMemberId = member.Id,
                    CreatedAtUtc = _clock.UtcNow,
                };
                _db.FamilyCheckoutBatchLines.Add(line);
            }

            // Draft-level plan reference; the immutable snapshot freezes at reservation.
            line.ProgramId = member.ProgramId;
            line.BundleId = member.BundleId;
            line.Tier = member.SelectedTier;
            line.DurationDays = member.DurationDays;
            line.IncludeFriday = member.IncludeFriday;
            line.StartDate = member.StartDate;
            line.DeliveryAddressId = member.DeliveryAddressId;
            line.CheckoutSelected = command.SubscribeNow;

            // Single atomic save: selection edit and version bump together (R06).
            batch.Version++;
            await _db.SaveChangesAsync(transactionCt);

            var selectedCount = await _db.FamilyCheckoutBatchLines
                .CountAsync(x => x.BatchId == batch.Id && x.CheckoutSelected, transactionCt);

            return new FamilyCheckoutSelectionDto(batch.Id, batch.Version, member.Id, line.CheckoutSelected, selectedCount);
        }, cancellationToken);
    }

    private async Task<bool> CanPurchaseAgainAsync(
        FamilyGroup group,
        FamilyMember member,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);
        if (subscription is null)
            return false;

        var today = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
        var live = subscription.EndDate >= today && subscription.Status is
            SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active
                or SubscriptionStatus.Frozen or SubscriptionStatus.SuspendedForReview;
        if (live)
            return false;

        var managerIndividualId = group.ManagerIndividualSubscriptionId ?? group.ConvertedFromSubscriptionId;
        if (member.Role == FamilyMemberRole.Manager && subscriptionId == managerIndividualId)
        {
            var legacyIdentity = subscription.Id == group.ConvertedFromSubscriptionId
                && subscription.CustomerUserId == group.ManagerCustomerUserId
                && subscription.FamilyGroupId == group.Id
                && subscription.FamilyMemberId == member.Id;
            var retainedIndividual = subscription.CustomerUserId == group.ManagerCustomerUserId
                && ((subscription.FamilyGroupId is null && subscription.FamilyMemberId is null) || legacyIdentity);
            return retainedIndividual;
        }

        var access = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => x.FamilyMemberId == member.Id && x.RemovedAtUtc == null)
            .Select(x => x.LinkedCustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        var expectedOwnerId = member.Role == FamilyMemberRole.Manager ? group.ManagerCustomerUserId : access;
        return expectedOwnerId is not null
            && subscription.CustomerUserId == expectedOwnerId
            && subscription.FamilyGroupId == group.Id
            && subscription.FamilyMemberId == member.Id;
    }

    private async Task<bool> HasLiveIndividualServiceAsync(
        FamilyGroup group,
        FamilyMember manager,
        CancellationToken cancellationToken)
    {
        var today = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
        var latest = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == group.ManagerCustomerUserId
                && x.FamilyGroupId == null && x.FamilyMemberId == null
                && x.EndDate >= today
                && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Scheduled
                    || x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Frozen
                    || x.Status == SubscriptionStatus.SuspendedForReview))
            .AnyAsync(cancellationToken);
        if (latest)
            return true;

        if (group.ConvertedFromSubscriptionId is not Guid convertedId || manager.MemberSubscriptionId != convertedId)
            return false;
        return await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == convertedId
            && x.CustomerUserId == group.ManagerCustomerUserId
            && x.FamilyGroupId == group.Id && x.FamilyMemberId == manager.Id
            && x.EndDate >= today
            && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Scheduled
                || x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Frozen
                || x.Status == SubscriptionStatus.SuspendedForReview), cancellationToken);
    }

    private async Task<FamilyCheckoutBatch> GetOrCreateDraftBatchAsync(
        FamilyGroup group,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var open = await _db.FamilyCheckoutBatches
            .FirstOrDefaultAsync(
                x => x.FamilyGroupId == group.Id
                    && (x.Status == FamilyCheckoutBatchStatus.Draft
                        || x.Status == FamilyCheckoutBatchStatus.PaymentPending),
                cancellationToken);
        // R04: the in-scope caller guard has already rejected PaymentPending while
        // holding the group lock, so only a Draft batch can reach here.
        if (open is not null)
            return open;

        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id,
            PayerCustomerUserId = managerId,
            // B03: the unique index is (FamilyGroupId, IdempotencyKey); a constant
            // key would collide with the closed batch of a previous purchase cycle.
            IdempotencyKey = "draft-" + Guid.NewGuid().ToString("N"),
            Status = FamilyCheckoutBatchStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };
        // Without an open batch, current/summary expose unpaid active members as
        // selected. Persist that same baseline before editing one member; otherwise
        // creating the first line silently turns every other displayed choice off.
        var previouslySelected = await _db.FamilyMembers.AsNoTracking()
            .Where(m => m.FamilyGroupId == group.Id && m.MemberSubscriptionId == null
                && !_db.FamilyMemberAccesses.Any(a => a.FamilyMemberId == m.Id && a.RemovedAtUtc != null))
            .ToListAsync(cancellationToken);
        foreach (var member in previouslySelected)
        {
            batch.Lines.Add(new FamilyCheckoutBatchLine
            {
                FamilyMemberId = member.Id,
                ProgramId = member.ProgramId,
                BundleId = member.BundleId,
                Tier = member.SelectedTier,
                DurationDays = member.DurationDays,
                IncludeFriday = member.IncludeFriday,
                StartDate = member.StartDate,
                DeliveryAddressId = member.DeliveryAddressId,
                CheckoutSelected = true,
                CreatedAtUtc = _clock.UtcNow,
            });
        }
        _db.FamilyCheckoutBatches.Add(batch);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (_db is DbContext efContext) efContext.ChangeTracker.Clear();
            // A concurrent manager request created the draft batch first.
            batch = await _db.FamilyCheckoutBatches
                .FirstAsync(
                    x => x.FamilyGroupId == group.Id && x.Status == FamilyCheckoutBatchStatus.Draft,
                    cancellationToken);
        }

        return batch;
    }

    /// <summary>
    /// Loads the explicitly selected member ids from the group's open batch.
    /// Null when no open batch exists (legacy flow without any selection data).
    /// </summary>
    public static async Task<IReadOnlySet<Guid>?> GetSelectedMemberIdsOrNullAsync(
        IAppDbContext db,
        Guid familyGroupId,
        CancellationToken cancellationToken)
    {
        var batchId = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.FamilyGroupId == familyGroupId
                && (x.Status == FamilyCheckoutBatchStatus.Draft
                    || x.Status == FamilyCheckoutBatchStatus.PaymentPending))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (batchId is null)
            return null;

        var ids = await db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => x.BatchId == batchId.Value && x.CheckoutSelected)
            .Select(x => x.FamilyMemberId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}

/// <summary>
/// B06: any draft mutation that can change the quote (members, plans, addresses,
/// selection) bumps the open draft batch Version, so a checkout request quoting
/// the stale version is refused with family.quote_changed instead of being
/// accepted at a new amount.
/// </summary>
public static class FamilyCheckoutBatchVersioning
{
    /// <summary>
    /// R06: marks the open draft batch(es) stale WITHOUT saving — call inside the
    /// mutation's own SaveChanges so the invalidation is atomic with the change.
    /// </summary>
    public static async Task TouchDraftVersionsAsync(
        IAppDbContext db,
        Guid familyGroupId,
        CancellationToken cancellationToken)
    {
        var drafts = await db.FamilyCheckoutBatches
            .Where(x => x.FamilyGroupId == familyGroupId
                && x.Status == FamilyCheckoutBatchStatus.Draft)
            .ToListAsync(cancellationToken);
        foreach (var batch in drafts)
            batch.Version++;
    }

    /// <summary>Standalone bump for callers without their own pending save.</summary>
    public static async Task BumpDraftVersionAsync(
        IAppDbContext db,
        Guid familyGroupId,
        CancellationToken cancellationToken)
    {
        await TouchDraftVersionsAsync(db, familyGroupId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
