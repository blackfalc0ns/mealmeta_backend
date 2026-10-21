using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

/// <summary>Builds a journal only from an approved, persisted source snapshot.</summary>
public sealed class PostingSourceFactory(IAppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<List<JournalEntryLine>>> CreateLinesAsync(JournalSourceDocument source, CancellationToken ct = default)
    {
        if (source.Status != JournalSourceDocumentStatus.Approved || source.ApprovedByUserId is null
            || source.ApprovedByUserId == source.CreatedByUserId || source.SubmittedFingerprint != source.ContentFingerprint
            || !JournalSourceService.IsFingerprintValid(source))
            return Result.Failure<List<JournalEntryLine>>(Error.Conflict("ledger.source_not_approved_or_changed"));
        try
        {
            var drafts = JsonSerializer.Deserialize<List<LedgerLineDraft>>(source.ContentJson, JsonOptions) ?? new();
            if (source.Type == JournalSourceDocumentType.Reversal)
            {
                if (source.OriginalJournalEntryId is not Guid originalId)
                    return Result.Failure<List<JournalEntryLine>>(Error.Conflict("ledger.reversal_original_required"));
                var original = await db.JournalEntries.AsNoTracking().Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == originalId && x.LegalEntityId == source.LegalEntityId
                        && x.Status == Domain.Enums.JournalEntryStatus.Posted, ct);
                if (original is null || !IsExactFullReversal(drafts, original.Lines))
                    return Result.Failure<List<JournalEntryLine>>(Error.Conflict("ledger.reversal_content_mismatch"));
            }
            var ids = drafts.Select(x => x.AccountId).Distinct().ToList();
            var accounts = await db.ChartOfAccounts.Include(x => x.Children)
                .Where(x => x.LegalEntityId == source.LegalEntityId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var lines = drafts.Select(draft => new JournalEntryLine
            {
                Id = Guid.NewGuid(), LegalEntityId = source.LegalEntityId, AccountId = draft.AccountId,
                Account = accounts.GetValueOrDefault(draft.AccountId)!,
                Debit = draft.Debit is { } debit ? new Money(debit, source.CurrencyCode) : null,
                Credit = draft.Credit is { } credit ? new Money(credit, source.CurrencyCode) : null,
                PartyId = draft.PartyId, PartyType = draft.PartyType, Memo = draft.Memo,
            }).ToList();
            var validation = JournalInvariant.ValidateLines(lines, source.BusinessDate, source.LegalEntityId);
            if (!validation.IsValid) return Result.Failure<List<JournalEntryLine>>(Error.Validation(validation.ErrorCode ?? "ledger.journal_invalid"));
            return Result.Success(lines);
        }
        catch (JsonException)
        {
            return Result.Failure<List<JournalEntryLine>>(Error.Conflict("ledger.source_content_changed"));
        }
    }

    private static bool IsExactFullReversal(IReadOnlyList<LedgerLineDraft> reversal, IEnumerable<JournalEntryLine> original)
    {
        var remaining = reversal.ToList();
        foreach (var line in original)
        {
            var debit = line.Credit?.Amount;
            var credit = line.Debit?.Amount;
            var index = remaining.FindIndex(candidate => candidate.AccountId == line.AccountId
                && candidate.Debit == debit && candidate.Credit == credit && candidate.PartyId == line.PartyId
                && string.Equals(candidate.PartyType?.Trim(), line.PartyType?.Trim(), StringComparison.Ordinal)
                && string.Equals(candidate.Memo?.Trim(), line.Memo?.Trim(), StringComparison.Ordinal));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return remaining.Count == 0;
    }
}
