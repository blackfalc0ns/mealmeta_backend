using MealMate.Application.Common.Models;
using MealMate.Domain.CentralAccounting;

namespace MealMate.Application.Features.Finance.Ledger;

/// <summary>Creates a reversal source from an original journal; caller amounts are ignored.</summary>
public sealed class JournalReversalService(IJournalSourceService sources)
{
    public Task<Result<LedgerSourceDto>> CreateDraftAsync(Guid originalJournalEntryId, DateOnly reversalDate,
        string reason, string evidenceReference, Guid legalEntityId, CancellationToken cancellationToken = default) =>
        sources.CreateDraftAsync(new LedgerSourceDraftRequest(legalEntityId, JournalSourceDocumentType.Reversal,
            reversalDate, reason, evidenceReference, Array.Empty<LedgerLineDraft>(), originalJournalEntryId), cancellationToken);
}
