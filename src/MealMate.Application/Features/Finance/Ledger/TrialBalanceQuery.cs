using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class TrialBalanceQuery(
    IAppDbContext db,
    LedgerAccess access,
    IApplicationLockManager locks,
    IApplicationTransactionExecutor transactions) : ITrialBalanceQuery
{
    public Task<Result<LedgerTrialBalanceResult>> GetAsync(Guid legalEntityId, Guid periodId, DateOnly asOfDate,
        int skip = 0, int take = 500, CancellationToken cancellationToken = default) =>
        transactions.ExecuteResultAsync(async ct =>
        {
            var permission = access.Require(PermissionCodes.LedgerView);
            if (permission is not null) return Result.Failure<LedgerTrialBalanceResult>(permission);
            if (skip < 0 || take is < 1 or > 1000) return Result.Failure<LedgerTrialBalanceResult>(Error.Validation("ledger.page_invalid"));
            var scope = await access.ValidateExistingScopeAsync(legalEntityId, ct);
            if (scope is not null) return Result.Failure<LedgerTrialBalanceResult>(scope);
            var period = await db.AccountingPeriods.SingleOrDefaultAsync(x => x.Id == periodId && x.LegalEntityId == legalEntityId, ct);
            if (period is null) return Result.Failure<LedgerTrialBalanceResult>(Error.NotFound("ledger.period_not_found"));
            if (asOfDate < period.StartDate || asOfDate >= period.EndDateExclusive)
                return Result.Failure<LedgerTrialBalanceResult>(Error.Validation("ledger.trial_balance_date_outside_period"));
            await locks.LockLedgerPeriodAsync(period.Id, ct);
            var closeVersion = period.CloseVersion;

            // One aggregate statement gives opening and movement the same PostgreSQL statement snapshot,
            // even when another open period is posting concurrently.
            var aggregate = await (from line in db.JournalEntryLines.AsNoTracking()
                join journal in db.JournalEntries.AsNoTracking() on line.JournalEntryId equals journal.Id
                where journal.LegalEntityId == legalEntityId && journal.Status == JournalEntryStatus.Posted
                    && journal.BusinessDate != null && journal.BusinessDate <= asOfDate
                group new { line, journal } by line.AccountId into grouping
                select new
                {
                    AccountId = grouping.Key,
                    OpeningDebit = grouping.Sum(x => x.journal.BusinessDate < period.StartDate
                        ? x.line.Debit == null ? 0m : x.line.Debit.Amount : 0m),
                    OpeningCredit = grouping.Sum(x => x.journal.BusinessDate < period.StartDate
                        ? x.line.Credit == null ? 0m : x.line.Credit.Amount : 0m),
                    PeriodDebit = grouping.Sum(x => x.journal.BusinessDate >= period.StartDate
                        ? x.line.Debit == null ? 0m : x.line.Debit.Amount : 0m),
                    PeriodCredit = grouping.Sum(x => x.journal.BusinessDate >= period.StartDate
                        ? x.line.Credit == null ? 0m : x.line.Credit.Amount : 0m),
                }).ToListAsync(ct);
            var ids = aggregate.Select(x => x.AccountId).ToList();
            if (ids.Count == 0)
            {
                var emptyHash = ComputeSnapshotHash(legalEntityId, periodId, closeVersion, asOfDate, Array.Empty<LedgerTrialBalanceRow>());
                return Result.Success(new LedgerTrialBalanceResult(legalEntityId, periodId, closeVersion, asOfDate,
                    Array.Empty<LedgerTrialBalanceRow>(), 0m, 0m, 0m, 0m, 0m, 0m, 0, emptyHash));
            }
            var accounts = await db.ChartOfAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.LegalEntityId == legalEntityId && ids.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.NameAr, x.NameEn }).ToDictionaryAsync(x => x.Id, ct);
            var rows = aggregate.Where(x => accounts.ContainsKey(x.AccountId)).Select(values =>
            {
                var openingNet = values.OpeningDebit - values.OpeningCredit;
                var movementNet = values.PeriodDebit - values.PeriodCredit;
                var closingNet = openingNet + movementNet;
                var account = accounts[values.AccountId];
                return new LedgerTrialBalanceRow(values.AccountId, account.Code, account.NameAr, account.NameEn,
                    Math.Max(openingNet, 0m), Math.Max(-openingNet, 0m), values.PeriodDebit, values.PeriodCredit,
                    Math.Max(closingNet, 0m), Math.Max(-closingNet, 0m));
            }).OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
            var result = new LedgerTrialBalanceResult(legalEntityId, periodId, closeVersion, asOfDate,
                rows.Skip(skip).Take(take).ToList(), rows.Sum(x => x.OpeningDebit), rows.Sum(x => x.OpeningCredit),
                rows.Sum(x => x.PeriodDebit), rows.Sum(x => x.PeriodCredit), rows.Sum(x => x.ClosingDebit), rows.Sum(x => x.ClosingCredit), rows.Count,
                ComputeSnapshotHash(legalEntityId, periodId, closeVersion, asOfDate, rows));
            return Result.Success(result);
        }, cancellationToken);

    private static string ComputeSnapshotHash(Guid entityId, Guid periodId, long closeVersion, DateOnly asOfDate,
        IReadOnlyList<LedgerTrialBalanceRow> rows)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { entityId, periodId, closeVersion, asOfDate, rows });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }
}
