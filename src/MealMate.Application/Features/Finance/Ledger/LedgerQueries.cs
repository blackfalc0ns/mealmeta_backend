using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class LedgerQueries(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
    LedgerAccess access, IApplicationTransactionExecutor transactions)
{
    public async Task<Result<IReadOnlyList<LedgerEntityDto>>> ListEntitiesAsync(CancellationToken ct = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<IReadOnlyList<LedgerEntityDto>>(permission);
        var allowedIds = access.IsSuperAdmin ? null : currentUser.TenantIds.Distinct().ToArray();
        if (allowedIds is { Length: 0 }) return Result.Success<IReadOnlyList<LedgerEntityDto>>(Array.Empty<LedgerEntityDto>());
        var query = db.LegalEntities.AsNoTracking().Where(x => x.Status == MealMate.Domain.Enums.LegalEntityStatus.Active && x.Currency == "KWD");
        if (allowedIds is not null) query = query.Where(x => allowedIds.Contains(x.Id));
        IReadOnlyList<LedgerEntityDto> rows = await query.OrderBy(x => x.LegalNameEn).Take(200)
            .Select(x => new LedgerEntityDto(x.Id, x.LegalNameAr, x.LegalNameEn, x.Currency, x.Status.ToString())).ToListAsync(ct);
        return Result.Success(rows);
    }

    public async Task<Result<LedgerAccountDto>> CreateAccountAsync(LedgerCreateAccountRequest request, CancellationToken ct = default) =>
        await transactions.ExecuteResultAsync(async token =>
        {
            var permission = access.Require(PermissionCodes.LedgerManage);
            if (permission is not null) return Result.Failure<LedgerAccountDto>(permission);
            var entityError = await access.ValidateEntityAsync(request.LegalEntityId, true, token);
            if (entityError is not null) return Result.Failure<LedgerAccountDto>(entityError);
            var code = request.Code?.Trim();
            var nameAr = request.NameAr?.Trim();
            var nameEn = request.NameEn?.Trim();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || string.IsNullOrWhiteSpace(nameAr)
                || nameAr.Length > 200 || string.IsNullOrWhiteSpace(nameEn) || nameEn.Length > 200
                || !Enum.IsDefined(request.AccountType) || !Enum.IsDefined(request.NormalBalance)
                || request.EffectiveFrom is { } from && request.EffectiveToExclusive is { } to && to <= from
                || request.ReportingLineCode?.Length > 100)
                return Result.Failure<LedgerAccountDto>(Error.Validation("ledger.account_invalid"));
            if (await db.ChartOfAccounts.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == code, token))
                return Result.Failure<LedgerAccountDto>(Error.Conflict("ledger.account_code_duplicate"));
            if (request.ParentId is { } parentId)
            {
                var parent = await db.ChartOfAccounts.SingleOrDefaultAsync(x => x.Id == parentId && x.LegalEntityId == request.LegalEntityId, token);
                if (parent is null) return Result.Failure<LedgerAccountDto>(Error.Validation("ledger.account_parent_invalid"));
                if (!parent.IsActive || parent.IsPostable) return Result.Failure<LedgerAccountDto>(Error.Validation("ledger.parent_account_not_postable"));
            }
            var account = new ChartOfAccount
            {
                Id = Guid.NewGuid(), LegalEntityId = request.LegalEntityId, Code = code, NameAr = nameAr,
                NameEn = nameEn, AccountType = request.AccountType, NormalBalance = request.NormalBalance,
                ParentId = request.ParentId, IsActive = true, IsPostable = request.IsPostable, RequiresParty = request.RequiresParty,
                EffectiveFrom = request.EffectiveFrom, EffectiveToExclusive = request.EffectiveToExclusive,
                ReportingLineCode = string.IsNullOrWhiteSpace(request.ReportingLineCode) ? null : request.ReportingLineCode.Trim(),
            };
            db.ChartOfAccounts.Add(account);
            db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Id = Guid.NewGuid(), Action = "LedgerAccountCreated", EntityType = "ChartOfAccount", EntityId = account.Id,
                AfterJson = System.Text.Json.JsonSerializer.Serialize(new { account.LegalEntityId, account.Code, account.AccountType, account.IsPostable }),
                ActorUserId = currentUser.UserId, OccurredAtUtc = clock.UtcNow,
            });
            await db.SaveChangesAsync(token);
            return new LedgerAccountDto(account.Id, account.LegalEntityId!.Value, account.Code, account.NameAr, account.NameEn,
                account.AccountType, account.NormalBalance!.Value, account.ParentId, account.IsActive, account.IsPostable, account.RequiresParty);
        }, ct);

    public async Task<Result<IReadOnlyList<LedgerAccountDto>>> ListAccountsAsync(Guid legalEntityId, int take = 500, CancellationToken ct = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<IReadOnlyList<LedgerAccountDto>>(permission);
        if (take is < 1 or > 1000) return Result.Failure<IReadOnlyList<LedgerAccountDto>>(Error.Validation("ledger.page_size_invalid"));
        var scope = await access.ValidateExistingScopeAsync(legalEntityId, ct);
        if (scope is not null) return Result.Failure<IReadOnlyList<LedgerAccountDto>>(scope);
        IReadOnlyList<LedgerAccountDto> rows = await db.ChartOfAccounts.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId)
            .OrderBy(x => x.Code).Take(take).Select(x => new LedgerAccountDto(x.Id, x.LegalEntityId!.Value, x.Code, x.NameAr, x.NameEn,
                x.AccountType, x.NormalBalance!.Value, x.ParentId, x.IsActive, x.IsPostable, x.RequiresParty)).ToListAsync(ct);
        return Result.Success(rows);
    }

    public async Task<Result<IReadOnlyList<LedgerJournalListItem>>> ListJournalsAsync(Guid legalEntityId, int take = 100, CancellationToken ct = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<IReadOnlyList<LedgerJournalListItem>>(permission);
        if (take is < 1 or > 200) return Result.Failure<IReadOnlyList<LedgerJournalListItem>>(Error.Validation("ledger.page_size_invalid"));
        var scope = await access.ValidateExistingScopeAsync(legalEntityId, ct);
        if (scope is not null) return Result.Failure<IReadOnlyList<LedgerJournalListItem>>(scope);
        var journals = await db.JournalEntries.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId
                && x.Status == MealMate.Domain.Enums.JournalEntryStatus.Posted && x.BusinessDate != null)
            .OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.PostedAtUtc).Take(take)
            .Select(x => new { x.Id, x.EntryNumber, x.BusinessDate, x.CurrencyCode, x.Description }).ToListAsync(ct);
        var ids = journals.Select(x => x.Id).ToArray();
        var links = await db.JournalSourceLinks.AsNoTracking().Where(x => ids.Contains(x.JournalEntryId))
            .Join(db.JournalSourceDocuments.AsNoTracking(), link => link.SourceDocumentId, source => source.Id,
                (link, source) => new { link.JournalEntryId, source.Id, source.Type }).ToDictionaryAsync(x => x.JournalEntryId, ct);
        IReadOnlyList<LedgerJournalListItem> result = journals.Select(x => new LedgerJournalListItem(x.Id, x.EntryNumber,
            x.BusinessDate!.Value, x.CurrencyCode ?? "", x.Description,
            links.TryGetValue(x.Id, out var source) ? source.Id : null,
            links.TryGetValue(x.Id, out source) ? source.Type : null)).ToList();
        return Result.Success(result);
    }

    public async Task<Result<LedgerJournalDetail>> GetJournalAsync(Guid journalId, CancellationToken ct = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<LedgerJournalDetail>(permission);
        var journal = await db.JournalEntries.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Account)
            .SingleOrDefaultAsync(x => x.Id == journalId && x.Status == MealMate.Domain.Enums.JournalEntryStatus.Posted
                && x.LegalEntityId != null, ct);
        if (journal is null) return Result.Failure<LedgerJournalDetail>(Error.NotFound("ledger.journal_not_found"));
        var scope = await access.ValidateExistingScopeAsync(journal.LegalEntityId!.Value, ct);
        if (scope is not null) return Result.Failure<LedgerJournalDetail>(scope);
        var link = await db.JournalSourceLinks.AsNoTracking().Where(x => x.JournalEntryId == journalId)
            .Join(db.JournalSourceDocuments.AsNoTracking(), sourceLink => sourceLink.SourceDocumentId, source => source.Id,
                (sourceLink, source) => new { source.Id, source.Type, source.EvidenceReference, source.OriginalJournalEntryId })
            .SingleOrDefaultAsync(ct);
        var reversalJournalId = await db.JournalReversals.AsNoTracking().Where(x => x.OriginalJournalEntryId == journalId)
            .Select(x => (Guid?)x.ReversalJournalEntryId).SingleOrDefaultAsync(ct);
        var reversalOfJournalId = await db.JournalReversals.AsNoTracking().Where(x => x.ReversalJournalEntryId == journalId)
            .Select(x => (Guid?)x.OriginalJournalEntryId).SingleOrDefaultAsync(ct);
        var detail = new LedgerJournalDetail(journal.Id, journal.EntryNumber, journal.LegalEntityId.Value,
            journal.AccountingPeriodId, journal.BusinessDate!.Value, journal.CurrencyCode ?? "", journal.Description,
            link?.EvidenceReference, link?.Id, link?.Type, link?.OriginalJournalEntryId ?? reversalOfJournalId,
            reversalJournalId, journal.Lines.OrderBy(x => x.Id).Select(x => new LedgerJournalLineDto(x.AccountId,
                x.Account.Code, x.Account.NameAr, x.Account.NameEn, x.Debit?.Amount ?? 0m, x.Credit?.Amount ?? 0m,
                x.PartyId, x.PartyType, x.Memo)).ToList());
        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<LedgerPeriodDto>>> ListPeriodsAsync(Guid legalEntityId, int take = 100, CancellationToken ct = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<IReadOnlyList<LedgerPeriodDto>>(permission);
        if (take is < 1 or > 200) return Result.Failure<IReadOnlyList<LedgerPeriodDto>>(Error.Validation("ledger.page_size_invalid"));
        var scope = await access.ValidateExistingScopeAsync(legalEntityId, ct);
        if (scope is not null) return Result.Failure<IReadOnlyList<LedgerPeriodDto>>(scope);
        IReadOnlyList<LedgerPeriodDto> rows = await db.AccountingPeriods.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId)
            .OrderByDescending(x => x.StartDate).Take(take).Select(x => new LedgerPeriodDto(x.Id, x.LegalEntityId, x.StartDate,
                x.EndDateExclusive, x.State, x.CloseVersion, x.ClosePostedJournalCount, x.CloseDebitTotal, x.CloseCreditTotal)).ToListAsync(ct);
        return Result.Success(rows);
    }

    public async Task<Result<LedgerSourceDto>> GetJournalSourceAsync(Guid sourceId, CancellationToken ct = default) =>
        await new JournalSourceService(db, currentUser, clock, access, transactions).GetAsync(sourceId, ct);
}
