using MealMate.Application.Common.Abstractions;
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

    public FamilyCheckoutSelectionService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICheckoutTransactionExecutor transactions,
        IApplicationLockManager locks)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _transactions = transactions;
        _locks = locks;
    }

    public Task<Result<FamilyCheckoutSelectionDto>> Handle(SetFamilyCheckoutSelectionCommand request, CancellationToken cancellationToken)
        => SetAsync(request, cancellationToken);

    public async Task<Result<FamilyCheckoutSelectionDto>> SetAsync(
        SetFamilyCheckoutSelectionCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId);

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
        if (member.Role == FamilyMemberRole.Manager && group.ConvertedFromSubscriptionId is not null)
            return Error.Conflict("conversion.manager_plan_locked", "The converted manager subscription is already paid.");
        if (member.MemberSubscriptionId is not null)
            return Error.Conflict("family.already_subscribed", "This member already has a subscription.");

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

            var openStatus = await _db.FamilyCheckoutBatches.AsNoTracking()
                .Where(x => x.FamilyGroupId == group.Id
                    && (x.Status == FamilyCheckoutBatchStatus.Draft
                        || x.Status == FamilyCheckoutBatchStatus.PaymentPending))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (FamilyCheckoutBatchStatus?)x.Status)
                .FirstOrDefaultAsync(transactionCt);
            if (openStatus == FamilyCheckoutBatchStatus.PaymentPending)
                return Error.Conflict("family.checkout_pending", "A family checkout payment is in progress. Complete or cancel it before changing the selection.");

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
