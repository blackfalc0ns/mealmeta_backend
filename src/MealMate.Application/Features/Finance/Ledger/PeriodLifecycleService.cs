using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class PeriodLifecycleService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    LedgerAccess access,
    IApplicationLockManager locks,
    IApplicationTransactionExecutor transactions) : IPeriodLifecycleService
{
    public Task<Result<LedgerPeriodDto>> CreateAsync(LedgerCreatePeriodRequest request, CancellationToken cancellationToken = default) =>
        transactions.ExecuteResultAsync(async ct =>
        {
            var permission = access.Require(PermissionCodes.LedgerPeriodsManage);
            if (permission is not null) return Result.Failure<LedgerPeriodDto>(permission);
            var entityError = await access.ValidateEntityAsync(request.LegalEntityId, true, ct);
            if (entityError is not null) return Result.Failure<LedgerPeriodDto>(entityError);
            if (request.StartDate == default || request.EndDateExclusive <= request.StartDate)
                return Result.Failure<LedgerPeriodDto>(Error.Validation("ledger.period_window_invalid"));
            await locks.LockLedgerPeriodCatalogAsync(request.LegalEntityId, ct);
            if (await db.AccountingPeriods.AnyAsync(x => x.LegalEntityId == request.LegalEntityId
                && x.StartDate < request.EndDateExclusive && request.StartDate < x.EndDateExclusive, ct))
                return Result.Failure<LedgerPeriodDto>(Error.Conflict("ledger.period_overlap"));
            var period = new AccountingPeriod
            {
                Id = Guid.NewGuid(), LegalEntityId = request.LegalEntityId, StartDate = request.StartDate,
                EndDateExclusive = request.EndDateExclusive, State = AccountingPeriodState.Open, CloseVersion = 0,
            };
            db.AccountingPeriods.Add(period);
            await db.SaveChangesAsync(ct);
            return ToDto(period);
        }, cancellationToken);

    public Task<Result<LedgerPeriodDto>> RequestCloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(periodId, request, AccountingPeriodState.PendingClose, cancellationToken);

    public Task<Result<LedgerPeriodDto>> CancelCloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(periodId, request, AccountingPeriodState.Open, cancellationToken);

    public Task<Result<LedgerPeriodDto>> CloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default) =>
        transactions.ExecuteResultAsync(async ct =>
        {
            var permission = access.Require(PermissionCodes.LedgerPeriodsManage);
            if (permission is not null) return Result.Failure<LedgerPeriodDto>(permission);
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
                return Result.Failure<LedgerPeriodDto>(Error.Validation("ledger.period_reason_required"));
            var period = await db.AccountingPeriods.SingleOrDefaultAsync(x => x.Id == periodId, ct);
            if (period is null) return Result.Failure<LedgerPeriodDto>(Error.NotFound("ledger.period_not_found"));
            var scope = await access.ValidateEntityAsync(period.LegalEntityId, true, ct);
            if (scope is not null) return Result.Failure<LedgerPeriodDto>(scope);
            await locks.LockLedgerPeriodAsync(period.Id, ct);
            if (period.State != AccountingPeriodState.PendingClose || period.CloseVersion != request.ExpectedCloseVersion)
                return Result.Failure<LedgerPeriodDto>(Error.Conflict("ledger.period_stale"));
            var posted = db.JournalEntries.AsNoTracking().Where(x => x.LegalEntityId == period.LegalEntityId
                && x.AccountingPeriodId == period.Id && x.Status == JournalEntryStatus.Posted);
            var count = await posted.LongCountAsync(ct);
            var debit = await (from journal in posted from line in db.JournalEntryLines.AsNoTracking()
                where line.JournalEntryId == journal.Id select line.Debit!.Amount).SumAsync(ct);
            var credit = await (from journal in posted from line in db.JournalEntryLines.AsNoTracking()
                where line.JournalEntryId == journal.Id select line.Credit!.Amount).SumAsync(ct);
            debit = Math.Round(debit, 3);
            credit = Math.Round(credit, 3);
            if (debit != credit) return Result.Failure<LedgerPeriodDto>(Error.Conflict("ledger.period_unbalanced"));
            var now = clock.UtcNow;
            var before = period.State.ToString();
            period.State = AccountingPeriodState.Closed;
            period.CloseVersion++;
            period.ClosedAtUtc = now;
            period.ClosedByUserId = currentUser.UserId;
            period.CloseReason = request.Reason.Trim();
            period.ClosePostedJournalCount = count;
            period.CloseDebitTotal = debit;
            period.CloseCreditTotal = credit;
            period.CloseWatermarkUtc = now;
            db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Id = Guid.NewGuid(), Action = "LedgerPeriodClosed", EntityType = "AccountingPeriod", EntityId = period.Id,
                BeforeJson = JsonSerializer.Serialize(new { State = before, Version = request.ExpectedCloseVersion }),
                AfterJson = JsonSerializer.Serialize(new { State = period.State.ToString(), Version = period.CloseVersion, count, debit, credit }),
                ActorUserId = currentUser.UserId, OccurredAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
            return ToDto(period);
        }, cancellationToken);

    private Task<Result<LedgerPeriodDto>> ChangeStateAsync(Guid periodId, LedgerPeriodCommandRequest request,
        AccountingPeriodState next, CancellationToken cancellationToken) => transactions.ExecuteResultAsync(async ct =>
    {
        var permission = access.Require(PermissionCodes.LedgerPeriodsManage);
        if (permission is not null) return Result.Failure<LedgerPeriodDto>(permission);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            return Result.Failure<LedgerPeriodDto>(Error.Validation("ledger.period_reason_required"));
        var period = await db.AccountingPeriods.SingleOrDefaultAsync(x => x.Id == periodId, ct);
        if (period is null) return Result.Failure<LedgerPeriodDto>(Error.NotFound("ledger.period_not_found"));
        var scope = await access.ValidateEntityAsync(period.LegalEntityId, true, ct);
        if (scope is not null) return Result.Failure<LedgerPeriodDto>(scope);
        await locks.LockLedgerPeriodAsync(period.Id, ct);
        if (period.CloseVersion != request.ExpectedCloseVersion
            || !((period.State == AccountingPeriodState.Open && next == AccountingPeriodState.PendingClose)
                || (period.State == AccountingPeriodState.PendingClose && next == AccountingPeriodState.Open)))
            return Result.Failure<LedgerPeriodDto>(Error.Conflict("ledger.period_stale"));
        var before = period.State.ToString();
        period.State = next;
        period.CloseVersion++;
        if (next == AccountingPeriodState.PendingClose)
        {
            period.PendingCloseAtUtc = clock.UtcNow;
            period.PendingCloseByUserId = currentUser.UserId;
            period.PendingCloseReason = request.Reason.Trim();
        }
        else
        {
            period.PendingCloseAtUtc = null;
            period.PendingCloseByUserId = null;
            period.PendingCloseReason = null;
        }
        db.FinancialAuditTrails.Add(new FinancialAuditTrail
        {
            Id = Guid.NewGuid(), Action = next == AccountingPeriodState.Open ? "LedgerPeriodCloseCancelled" : "LedgerPeriodCloseRequested",
            EntityType = "AccountingPeriod", EntityId = period.Id,
            BeforeJson = JsonSerializer.Serialize(new { State = before, Version = request.ExpectedCloseVersion }),
            AfterJson = JsonSerializer.Serialize(new { State = next.ToString(), Version = period.CloseVersion, Reason = request.Reason.Trim() }),
            ActorUserId = currentUser.UserId, OccurredAtUtc = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(period);
    }, cancellationToken);

    private static LedgerPeriodDto ToDto(AccountingPeriod period) => new(period.Id, period.LegalEntityId, period.StartDate,
        period.EndDateExclusive, period.State, period.CloseVersion, period.ClosePostedJournalCount, period.CloseDebitTotal, period.CloseCreditTotal);
}
