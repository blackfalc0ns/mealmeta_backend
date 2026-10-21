using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class PostingService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    LedgerAccess access,
    PostingSourceFactory factory,
    IApplicationTransactionExecutor transactions,
    IApplicationLockManager locks) : IPostingService, ILedgerEventPostingProcessor
{
    private const string ConsumerName = "finance.ledger.source";

    public Task<Result<LedgerPostingResult>> PostApprovedSourceAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default) =>
        PostAsync(sourceDocumentId, Guid.NewGuid(), null, fromOutbox: false, cancellationToken);

    public Task<Result<LedgerPostingResult>> ProcessApprovedSourceDeliveryAsync(Guid sourceDocumentId, Guid deliveryEventId,
        string expectedFingerprint, CancellationToken cancellationToken = default) =>
        PostAsync(sourceDocumentId, deliveryEventId, expectedFingerprint, fromOutbox: true, cancellationToken);

    private Task<Result<LedgerPostingResult>> PostAsync(Guid sourceDocumentId, Guid deliveryEventId, string? expectedFingerprint, bool fromOutbox,
        CancellationToken cancellationToken) => transactions.ExecuteResultAsync(async ct =>
    {
        if (sourceDocumentId == Guid.Empty)
            return Result.Failure<LedgerPostingResult>(Error.Validation("ledger.source_id_required"));
        if (!fromOutbox)
        {
            var permission = access.Require(PermissionCodes.LedgerManage);
            if (permission is not null) return Result.Failure<LedgerPostingResult>(permission);
        }
        var source = await db.JournalSourceDocuments.SingleOrDefaultAsync(x => x.Id == sourceDocumentId, ct);
        if (source is null) return Result.Failure<LedgerPostingResult>(Error.NotFound("ledger.source_not_found"));
        if (!fromOutbox)
        {
            var scope = await access.ValidateExistingScopeAsync(source.LegalEntityId, ct);
            if (scope is not null) return Result.Failure<LedgerPostingResult>(scope);
        }
        if (!JournalSourceService.IsFingerprintValid(source))
            return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.source_content_changed"));

        var periodId = await db.AccountingPeriods.AsNoTracking().Where(x => x.LegalEntityId == source.LegalEntityId
                && x.StartDate <= source.BusinessDate && x.EndDateExclusive > source.BusinessDate)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (periodId is null) return Result.Failure<LedgerPostingResult>(Error.Validation("ledger.period_not_found_for_date"));

        // Global lock order is period, then economic identity.
        await locks.LockLedgerPeriodAsync(periodId.Value, ct);
        var identitySourceType = source.Type == JournalSourceDocumentType.Reversal ? "JournalReversal" : "ManualJournalSource";
        var identitySourceId = source.Type == JournalSourceDocumentType.Reversal ? source.OriginalJournalEntryId ?? Guid.Empty : source.Id;
        var identityEffect = source.Type == JournalSourceDocumentType.Reversal ? PostingEffect.FullReversal : PostingEffect.Original;
        var adjustmentKey = source.Type == JournalSourceDocumentType.Reversal ? identitySourceId : Guid.Empty;
        await locks.LockLedgerPostingIdentityAsync(source.LegalEntityId, identitySourceType, identitySourceId,
            Guid.Empty, (int)identityEffect, adjustmentKey, ct);
        await db.ReloadEntityAsync(source, ct);
        if (!JournalSourceService.IsFingerprintValid(source))
            return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.source_content_changed"));
        if (fromOutbox && (string.IsNullOrWhiteSpace(expectedFingerprint)
            || !JournalSourceService.FixedEquals(source.SubmittedFingerprint ?? string.Empty, expectedFingerprint)))
            return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.outbox_fingerprint_mismatch"));

        var sourceFingerprint = source.SubmittedFingerprint ?? source.ContentFingerprint;
        var economicHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"LedgerPostingFactory:v1|{sourceFingerprint}"))).ToLowerInvariant();
        var eventId = deliveryEventId;
        var priorInbox = await db.InboxProcessedEvents.SingleOrDefaultAsync(x => x.ConsumerName == ConsumerName && x.EventId == eventId, ct);
        if (priorInbox is not null)
        {
            if (priorInbox.FinancePayloadHash != economicHash || priorInbox.ResultJournalEntryId is null)
                return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.delivery_content_conflict"));
            return Result.Success(new LedgerPostingResult(priorInbox.ResultJournalEntryId.Value, source.Id, economicHash, true));
        }

        var identity = await db.PostingIdentities.Include(x => x.JournalEntry).SingleOrDefaultAsync(x => x.LegalEntityId == source.LegalEntityId
            && x.SourceType == identitySourceType && x.SourceId == identitySourceId && x.ServiceDayKey == Guid.Empty
            && x.Effect == identityEffect && x.AdjustmentKey == adjustmentKey, ct);
        if (identity is not null)
        {
            if (!JournalSourceService.FixedEquals(identity.EconomicHash, economicHash))
                return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.economic_content_conflict"));
            db.InboxProcessedEvents.Add(NewInbox(eventId, economicHash, identity.JournalEntryId));
            await db.SaveChangesAsync(ct);
            return Result.Success(new LedgerPostingResult(identity.JournalEntryId, source.Id, economicHash, true));
        }

        if (source.Status != JournalSourceDocumentStatus.Approved || source.ApprovedByUserId is null)
            return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.source_not_approved"));
        var entityError = fromOutbox
            ? await access.ValidateActiveKwdEntityAsync(source.LegalEntityId, ct)
            : await access.ValidateEntityAsync(source.LegalEntityId, requireActiveKwd: true, ct);
        if (entityError is not null) return Result.Failure<LedgerPostingResult>(entityError);
        var period = await db.AccountingPeriods.SingleOrDefaultAsync(x => x.Id == periodId.Value && x.LegalEntityId == source.LegalEntityId, ct);
        if (period is null || period.State != AccountingPeriodState.Open || !period.Contains(source.BusinessDate))
            return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.period_not_open"));

        var builtLines = await factory.CreateLinesAsync(source, ct);
        if (builtLines.IsFailure) return Result.Failure<LedgerPostingResult>(builtLines.Error);
        var now = clock.UtcNow;
        var journalId = Guid.NewGuid();
        var journal = new JournalEntry
        {
            Id = journalId, EntryNumber = $"LJ-{journalId:N}", Status = JournalEntryStatus.Draft, PostedAtUtc = null,
            Description = source.Reason, SourceReference = source.EvidenceReference, CorrelationId = eventId.ToString(),
            LegalEntityId = source.LegalEntityId, AccountingPeriodId = period.Id, BusinessDate = source.BusinessDate,
            CurrencyCode = source.CurrencyCode, PayloadHash = economicHash, SourceOccurredAtUtc = source.CreatedAtUtc,
            Lines = builtLines.Value,
        };
        db.JournalEntries.Add(journal);
        await db.SaveChangesAsync(ct);

        journal.Status = JournalEntryStatus.Posted;
        journal.PostedAtUtc = now;
        source.Status = JournalSourceDocumentStatus.Posted;
        source.PostedJournalEntryId = journal.Id;
        source.RowVersion++;
        db.JournalSourceLinks.Add(new JournalSourceLink
        {
            Id = Guid.NewGuid(), LegalEntityId = source.LegalEntityId, SourceDocumentId = source.Id,
            JournalEntryId = journal.Id, Type = source.Type switch
            {
                JournalSourceDocumentType.OpeningBalance => JournalSourceLinkType.OpeningBalance,
                JournalSourceDocumentType.Reversal => JournalSourceLinkType.Reversal,
                _ => JournalSourceLinkType.Adjustment,
            }, SourceFingerprint = economicHash, LinkedAtUtc = now,
        });
        if (source.Type == JournalSourceDocumentType.Reversal && source.OriginalJournalEntryId is Guid originalJournalId)
        {
            var original = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == originalJournalId
                && x.LegalEntityId == source.LegalEntityId && x.Status == JournalEntryStatus.Posted, ct);
            if (original is null) return Result.Failure<LedgerPostingResult>(Error.Conflict("ledger.reversal_original_not_posted"));
            db.JournalReversals.Add(new JournalReversal
            {
                Id = Guid.NewGuid(), LegalEntityId = source.LegalEntityId, OriginalJournalEntryId = originalJournalId,
                ReversalJournalEntryId = journal.Id, SourceDocumentId = source.Id, Reason = source.Reason,
                EvidenceReference = source.EvidenceReference, OriginalBusinessDate = original.BusinessDate!.Value,
                ReversalBusinessDate = source.BusinessDate,
            });
        }
        db.PostingIdentities.Add(new PostingIdentity
        {
            Id = Guid.NewGuid(), LegalEntityId = source.LegalEntityId, SourceType = identitySourceType, SourceId = identitySourceId,
            ServiceDayKey = Guid.Empty, Effect = identityEffect, AdjustmentKey = adjustmentKey,
            EconomicHash = economicHash, JournalEntryId = journal.Id, JournalEntry = journal,
        });
        db.InboxProcessedEvents.Add(NewInbox(eventId, economicHash, journal.Id));
        db.FinancialAuditTrails.Add(new FinancialAuditTrail
        {
            Id = Guid.NewGuid(), Action = "LedgerJournalPosted", EntityType = "JournalEntry", EntityId = journal.Id,
            AfterJson = JsonSerializer.Serialize(new { SourceDocumentId = source.Id, EconomicHash = economicHash }),
            ActorUserId = currentUser.UserId, OccurredAtUtc = now,
        });
        db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
        {
            Id = Guid.NewGuid(), EventType = "LedgerJournalPosted", AggregateType = "JournalEntry", AggregateId = journal.Id.ToString(),
            DeduplicationKey = $"ledger-journal-posted:{source.LegalEntityId:N}:{source.Id:N}:original", EventVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { JournalEntryId = journal.Id, SourceDocumentId = source.Id, EconomicHash = economicHash }),
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return Result.Success(new LedgerPostingResult(journal.Id, source.Id, economicHash, false));
    }, cancellationToken);

    private MealMate.Domain.Common.InboxProcessedEvent NewInbox(Guid eventId, string hash, Guid journalId) => new()
    {
        Id = Guid.NewGuid(), ConsumerName = ConsumerName, EventId = eventId, ProcessedAtUtc = clock.UtcNow,
        FinancePayloadHash = hash, ResultJournalEntryId = journalId,
    };
}
