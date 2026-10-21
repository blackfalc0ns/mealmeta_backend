using MealMate.Domain.CentralAccounting;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed record LedgerLineDraft(Guid AccountId, decimal? Debit, decimal? Credit, Guid? PartyId = null, string? PartyType = null, string? Memo = null);
public sealed record LedgerSourceDraftRequest(Guid LegalEntityId, JournalSourceDocumentType Type, DateOnly BusinessDate,
    string Reason, string EvidenceReference, IReadOnlyList<LedgerLineDraft> Lines, Guid? OriginalJournalEntryId = null);
public sealed record LedgerSourceDto(Guid Id, string DocumentNumber, JournalSourceDocumentType Type,
    JournalSourceDocumentStatus Status, Guid LegalEntityId, DateOnly BusinessDate, string CurrencyCode,
    string Reason, string EvidenceReference, string Fingerprint, long RowVersion, Guid? CreatedByUserId,
    Guid? SubmittedByUserId, Guid? ApprovedByUserId, Guid? PostedJournalEntryId, Guid? OriginalJournalEntryId, IReadOnlyList<LedgerLineDraft> Lines);
public sealed record LedgerSourceSubmitRequest(string ExpectedFingerprint, long ExpectedRowVersion);
public sealed record LedgerSourceDecisionRequest(string ExpectedFingerprint, long ExpectedRowVersion, string? Reason = null);
public sealed record LedgerCreateAccountRequest(Guid LegalEntityId, string Code, string NameAr, string NameEn,
    MealMate.Domain.Enums.AccountType AccountType, AccountNormalBalance NormalBalance, Guid? ParentId,
    bool IsPostable, bool RequiresParty, DateOnly? EffectiveFrom, DateOnly? EffectiveToExclusive, string? ReportingLineCode);
public sealed record LedgerCreatePeriodRequest(Guid LegalEntityId, DateOnly StartDate, DateOnly EndDateExclusive);
public sealed record LedgerPeriodCommandRequest(long ExpectedCloseVersion, string Reason);
public sealed record LedgerPeriodDto(Guid Id, Guid LegalEntityId, DateOnly StartDate, DateOnly EndDateExclusive,
    AccountingPeriodState State, long CloseVersion, long? ClosePostedJournalCount, decimal? CloseDebitTotal, decimal? CloseCreditTotal);
public sealed record LedgerEntityDto(Guid Id, string LegalNameAr, string LegalNameEn, string Currency, string Status);
public sealed record LedgerAccountDto(Guid Id, Guid LegalEntityId, string Code, string NameAr, string NameEn,
    MealMate.Domain.Enums.AccountType AccountType, AccountNormalBalance NormalBalance, Guid? ParentId, bool IsActive, bool IsPostable, bool RequiresParty);
public sealed record LedgerPostingResult(Guid JournalEntryId, Guid SourceDocumentId, string EconomicHash, bool WasReplay);
public sealed record LedgerTrialBalanceRow(Guid AccountId, string Code, string NameAr, string NameEn,
    decimal OpeningDebit, decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit);
public sealed record LedgerTrialBalanceResult(Guid LegalEntityId, Guid PeriodId, long CloseVersion, DateOnly AsOfDate,
    IReadOnlyList<LedgerTrialBalanceRow> Rows, decimal TotalOpeningDebit, decimal TotalOpeningCredit,
    decimal TotalPeriodDebit, decimal TotalPeriodCredit, decimal TotalClosingDebit, decimal TotalClosingCredit,
    int TotalRowCount, string SnapshotHash);
public sealed record LedgerJournalListItem(Guid Id, string EntryNumber, DateOnly BusinessDate, string CurrencyCode,
    string Description, Guid? SourceDocumentId, JournalSourceDocumentType? SourceType);
public sealed record LedgerJournalLineDto(Guid AccountId, string AccountCode, string NameAr, string NameEn,
    decimal Debit, decimal Credit, Guid? PartyId, string? PartyType, string? Memo);
public sealed record LedgerJournalDetail(Guid Id, string EntryNumber, Guid LegalEntityId, Guid? AccountingPeriodId,
    DateOnly BusinessDate, string CurrencyCode, string Description, string? EvidenceReference,
    Guid? SourceDocumentId, JournalSourceDocumentType? SourceType, Guid? OriginalJournalEntryId,
    Guid? ReversalJournalEntryId, IReadOnlyList<LedgerJournalLineDto> Lines);

public interface IJournalSourceService
{
    Task<MealMate.Application.Common.Models.Result<LedgerSourceDto>> CreateDraftAsync(LedgerSourceDraftRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerSourceDto>> GetAsync(Guid sourceId, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<IReadOnlyList<LedgerSourceDto>>> ListAsync(Guid legalEntityId, int take = 100, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerSourceDto>> SubmitAsync(Guid sourceId, LedgerSourceSubmitRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerSourceDto>> ApproveAsync(Guid sourceId, LedgerSourceDecisionRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerSourceDto>> RejectAsync(Guid sourceId, LedgerSourceDecisionRequest request, CancellationToken cancellationToken = default);
}

public interface IPostingService
{
    Task<MealMate.Application.Common.Models.Result<LedgerPostingResult>> PostApprovedSourceAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default);
}

public interface ILedgerEventPostingProcessor
{
    Task<MealMate.Application.Common.Models.Result<LedgerPostingResult>> ProcessApprovedSourceDeliveryAsync(Guid sourceDocumentId,
        Guid deliveryEventId, string expectedFingerprint, CancellationToken cancellationToken = default);
}

public interface IPeriodLifecycleService
{
    Task<MealMate.Application.Common.Models.Result<LedgerPeriodDto>> CreateAsync(LedgerCreatePeriodRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerPeriodDto>> RequestCloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerPeriodDto>> CancelCloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default);
    Task<MealMate.Application.Common.Models.Result<LedgerPeriodDto>> CloseAsync(Guid periodId, LedgerPeriodCommandRequest request, CancellationToken cancellationToken = default);
}

public interface ITrialBalanceQuery
{
    Task<MealMate.Application.Common.Models.Result<LedgerTrialBalanceResult>> GetAsync(Guid legalEntityId, Guid periodId, DateOnly asOfDate, int skip = 0, int take = 500, CancellationToken cancellationToken = default);
}
