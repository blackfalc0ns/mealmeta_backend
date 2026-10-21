using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Ledger;

public sealed class JournalSourceService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    LedgerAccess access,
    IApplicationTransactionExecutor transactions) : IJournalSourceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<LedgerSourceDto>> CreateDraftAsync(LedgerSourceDraftRequest request, CancellationToken cancellationToken = default) =>
        transactions.ExecuteResultAsync(async ct =>
        {
            var permission = access.Require(PermissionCodes.LedgerManage);
            if (permission is not null) return Result.Failure<LedgerSourceDto>(permission);
            if (!Enum.IsDefined(request.Type)
                || request.BusinessDate == default || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
                || string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 500
                || request.Lines is null || request.Lines.Count > 500)
                return Result.Failure<LedgerSourceDto>(Error.Validation("ledger.source_draft_invalid"));
            var entityError = await access.ValidateEntityAsync(request.LegalEntityId, requireActiveKwd: true, ct);
            if (entityError is not null) return Result.Failure<LedgerSourceDto>(entityError);
            IReadOnlyList<LedgerLineDraft> requestedLines = request.Lines;
            if (request.Type == JournalSourceDocumentType.Reversal)
            {
                if (request.Lines.Count != 0)
                    return Result.Failure<LedgerSourceDto>(Error.Validation("ledger.reversal_caller_lines_forbidden"));
                if (request.OriginalJournalEntryId is not Guid originalId || originalId == Guid.Empty)
                    return Result.Failure<LedgerSourceDto>(Error.Validation("ledger.reversal_original_required"));
                var original = await db.JournalEntries.AsNoTracking().Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == originalId && x.LegalEntityId == request.LegalEntityId
                        && x.Status == Domain.Enums.JournalEntryStatus.Posted, ct);
                if (original is null) return Result.Failure<LedgerSourceDto>(Error.NotFound("ledger.reversal_original_not_found"));
                var originalScope = await access.ValidateExistingScopeAsync(original.LegalEntityId!.Value, ct);
                if (originalScope is not null) return Result.Failure<LedgerSourceDto>(originalScope);
                if (await db.JournalReversals.AnyAsync(x => x.OriginalJournalEntryId == original.Id, ct)
                    || await db.JournalSourceDocuments.AnyAsync(x => x.OriginalJournalEntryId == original.Id
                        && x.Status != JournalSourceDocumentStatus.Rejected, ct))
                    return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.original_already_reversed"));
                requestedLines = original.Lines.Select(x => new LedgerLineDraft(x.AccountId,
                    x.Credit?.Amount, x.Debit?.Amount, x.PartyId, x.PartyType, x.Memo)).ToList();
            }
            else if (request.OriginalJournalEntryId is not null)
            {
                return Result.Failure<LedgerSourceDto>(Error.Validation("ledger.reversal_original_invalid"));
            }
            var mapped = await MapLinesAsync(request.LegalEntityId, request.BusinessDate, requestedLines, ct);
            if (mapped.IsFailure) return Result.Failure<LedgerSourceDto>(mapped.Error);
            var validation = JournalInvariant.ValidateLines(mapped.Value, request.BusinessDate, request.LegalEntityId);
            if (!validation.IsValid) return Result.Failure<LedgerSourceDto>(Error.Validation(validation.ErrorCode ?? "ledger.journal_invalid"));

            var actor = currentUser.UserId!.Value;
            var id = Guid.NewGuid();
            var normalizedLines = requestedLines.Select(NormalizeLine).ToList();
            var content = JsonSerializer.Serialize(normalizedLines, JsonOptions);
            var document = new JournalSourceDocument
            {
                Id = id,
                DocumentNumber = $"LJ-{id:N}",
                Type = request.Type,
                Status = JournalSourceDocumentStatus.Draft,
                LegalEntityId = request.LegalEntityId,
                BusinessDate = request.BusinessDate,
                CurrencyCode = "KWD",
                Reason = request.Reason.Trim(),
                EvidenceReference = request.EvidenceReference.Trim(),
                ContentJson = content,
                ContentFingerprint = ComputeFingerprint(id, actor, request.OriginalJournalEntryId, request.Type, request.LegalEntityId, request.BusinessDate,
                    "KWD", request.Reason.Trim(), request.EvidenceReference.Trim(), content),
                CreatedByUserId = actor,
                CreatedAtUtc = clock.UtcNow,
                RowVersion = 1,
                OriginalJournalEntryId = request.OriginalJournalEntryId,
            };
            db.JournalSourceDocuments.Add(document);
            await db.SaveChangesAsync(ct);
            return Result.Success(ToDto(document));
        }, cancellationToken);

    public async Task<Result<LedgerSourceDto>> GetAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<LedgerSourceDto>(permission);
        var document = await db.JournalSourceDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (document is null) return Result.Failure<LedgerSourceDto>(Error.NotFound("ledger.source_not_found"));
        var scope = await access.ValidateExistingScopeAsync(document.LegalEntityId, cancellationToken);
        return scope is null ? ToDto(document) : Result.Failure<LedgerSourceDto>(scope);
    }

    public async Task<Result<IReadOnlyList<LedgerSourceDto>>> ListAsync(Guid legalEntityId, int take = 100, CancellationToken cancellationToken = default)
    {
        var permission = access.Require(PermissionCodes.LedgerView);
        if (permission is not null) return Result.Failure<IReadOnlyList<LedgerSourceDto>>(permission);
        if (take is < 1 or > 200) return Result.Failure<IReadOnlyList<LedgerSourceDto>>(Error.Validation("ledger.page_size_invalid"));
        var scope = await access.ValidateExistingScopeAsync(legalEntityId, cancellationToken);
        if (scope is not null) return Result.Failure<IReadOnlyList<LedgerSourceDto>>(scope);
        var rows = await db.JournalSourceDocuments.AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId).OrderByDescending(x => x.CreatedAtUtc).Take(take)
            .ToListAsync(cancellationToken);
        IReadOnlyList<LedgerSourceDto> result = rows.Select(ToDto).ToList();
        return Result.Success(result);
    }

    public Task<Result<LedgerSourceDto>> SubmitAsync(Guid sourceId, LedgerSourceSubmitRequest request, CancellationToken cancellationToken = default) =>
        transactions.ExecuteResultAsync(async ct =>
        {
            var permission = access.Require(PermissionCodes.LedgerManage);
            if (permission is not null) return Result.Failure<LedgerSourceDto>(permission);
            var document = await db.JournalSourceDocuments.SingleOrDefaultAsync(x => x.Id == sourceId, ct);
            if (document is null) return Result.Failure<LedgerSourceDto>(Error.NotFound("ledger.source_not_found"));
            var scope = await access.ValidateEntityAsync(document.LegalEntityId, requireActiveKwd: true, ct);
            if (scope is not null) return Result.Failure<LedgerSourceDto>(scope);
            if (document.CreatedByUserId != currentUser.UserId) return Result.Failure<LedgerSourceDto>(Error.Forbidden("ledger.source_creator_only_submit"));
            if (document.Status != JournalSourceDocumentStatus.Draft || document.RowVersion != request.ExpectedRowVersion
                || !FixedEquals(document.ContentFingerprint, request.ExpectedFingerprint))
                return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.source_stale"));
            if (!IsFingerprintValid(document)) return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.source_content_changed"));

            document.Status = JournalSourceDocumentStatus.PendingApproval;
            document.SubmittedByUserId = currentUser.UserId;
            document.SubmittedAtUtc = clock.UtcNow;
            document.SubmittedFingerprint = document.ContentFingerprint;
            document.RowVersion++;
            AddAudit(document, "LedgerSourceSubmitted", null, document.SubmittedFingerprint);
            await db.SaveChangesAsync(ct);
            return ToDto(document);
        }, cancellationToken);

    public Task<Result<LedgerSourceDto>> ApproveAsync(Guid sourceId, LedgerSourceDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(sourceId, request, approve: true, cancellationToken);

    public Task<Result<LedgerSourceDto>> RejectAsync(Guid sourceId, LedgerSourceDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(sourceId, request, approve: false, cancellationToken);

    private async Task<Result<LedgerSourceDto>> DecideAsync(Guid sourceId, LedgerSourceDecisionRequest request, bool approve, CancellationToken cancellationToken)
    {
        try
        {
            return await transactions.ExecuteResultAsync(async ct =>
            {
            var permission = access.Require(approve ? PermissionCodes.LedgerApprove : PermissionCodes.LedgerApprove);
            if (permission is not null) return Result.Failure<LedgerSourceDto>(permission);
            var document = await db.JournalSourceDocuments.SingleOrDefaultAsync(x => x.Id == sourceId, ct);
            if (document is null) return Result.Failure<LedgerSourceDto>(Error.NotFound("ledger.source_not_found"));
            var scope = await access.ValidateExistingScopeAsync(document.LegalEntityId, ct);
            if (scope is not null) return Result.Failure<LedgerSourceDto>(scope);
            var actor = currentUser.UserId!.Value;
            if (actor == document.CreatedByUserId || actor == document.SubmittedByUserId)
                return Result.Failure<LedgerSourceDto>(Error.Forbidden("ledger.maker_checker_required"));
            if (document.Status != JournalSourceDocumentStatus.PendingApproval || document.RowVersion != request.ExpectedRowVersion
                || !FixedEquals(document.SubmittedFingerprint ?? string.Empty, request.ExpectedFingerprint))
                return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.source_stale"));
            if (!IsFingerprintValid(document)) return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.source_content_changed"));
            if (!approve && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000))
                return Result.Failure<LedgerSourceDto>(Error.Validation("ledger.rejection_reason_required"));

            var before = document.Status.ToString();
            if (approve)
            {
                document.Status = JournalSourceDocumentStatus.Approved;
                document.ApprovedByUserId = actor;
                document.ApprovedAtUtc = clock.UtcNow;
            }
            else
            {
                document.Status = JournalSourceDocumentStatus.Rejected;
                document.RejectedByUserId = actor;
                document.RejectedAtUtc = clock.UtcNow;
                document.RejectionReason = request.Reason!.Trim();
            }
            document.RowVersion++;
            AddAudit(document, approve ? "LedgerSourceApproved" : "LedgerSourceRejected", before, approve ? document.SubmittedFingerprint : request.Reason);
            if (approve)
            {
                db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
                {
                    Id = Guid.NewGuid(), EventType = "LedgerSourceApproved", AggregateType = "JournalSourceDocument",
                    AggregateId = document.Id.ToString(), DeduplicationKey = $"ledger-source-approved:{document.Id:N}",
                    EventVersion = 1, PayloadJson = JsonSerializer.Serialize(new { document.Id, Fingerprint = document.SubmittedFingerprint }, JsonOptions),
                    CreatedAtUtc = clock.UtcNow,
                });
            }
            await db.SaveChangesAsync(ct);
                return ToDto(document);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<LedgerSourceDto>(Error.Conflict("ledger.source_stale"));
        }
    }

    internal async Task<Result<List<JournalEntryLine>>> MapLinesAsync(Guid entityId, DateOnly businessDate,
        IReadOnlyList<LedgerLineDraft> drafts, CancellationToken ct)
    {
        if (drafts.Count < 2 || drafts.Any(x => x.AccountId == Guid.Empty || x.Memo?.Length > 1000 || x.PartyType?.Length > 50))
            return Result.Failure<List<JournalEntryLine>>(Error.Validation("ledger.lines_invalid"));
        var accountIds = drafts.Select(x => x.AccountId).Distinct().ToList();
        var accounts = await db.ChartOfAccounts.Include(x => x.Children)
            .Where(x => x.LegalEntityId == entityId && accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var lines = drafts.Select(draft => new JournalEntryLine
        {
            Id = Guid.NewGuid(), LegalEntityId = entityId, AccountId = draft.AccountId,
            Account = accounts.GetValueOrDefault(draft.AccountId)!,
            Debit = draft.Debit is { } debit ? new Money(debit, "KWD") : null,
            Credit = draft.Credit is { } credit ? new Money(credit, "KWD") : null,
            PartyId = draft.PartyId, PartyType = draft.PartyType, Memo = draft.Memo,
        }).ToList();
        var validation = JournalInvariant.ValidateLines(lines, businessDate, entityId);
        if (!validation.IsValid) return Result.Failure<List<JournalEntryLine>>(Error.Validation(validation.ErrorCode ?? "ledger.journal_invalid"));
        return Result.Success(lines);
    }

    internal static bool IsFingerprintValid(JournalSourceDocument document)
    {
        try
        {
            var lines = JsonSerializer.Deserialize<List<LedgerLineDraft>>(document.ContentJson, JsonOptions) ?? throw new JsonException();
            var canonical = JsonSerializer.Serialize(lines.Select(NormalizeLine).ToList(), JsonOptions);
            return FixedEquals(document.ContentFingerprint, ComputeFingerprint(document.Id, document.CreatedByUserId, document.OriginalJournalEntryId, document.Type, document.LegalEntityId,
                document.BusinessDate, document.CurrencyCode, document.Reason, document.EvidenceReference, canonical));
        }
        catch (JsonException) { return false; }
    }

    internal static string ComputeFingerprint(Guid documentId, Guid makerUserId, Guid? originalJournalEntryId,
        JournalSourceDocumentType type, Guid legalEntityId, DateOnly businessDate,
        string currency, string reason, string evidence, string contentJson)
    {
        var lines = JsonSerializer.Deserialize<List<LedgerLineDraft>>(contentJson, JsonOptions) ?? throw new JsonException();
        var canonical = JsonSerializer.Serialize(lines.Select(NormalizeLine).ToList(), JsonOptions);
        var payload = JsonSerializer.Serialize(new { DocumentId = documentId, MakerUserId = makerUserId, OriginalJournalEntryId = originalJournalEntryId,
            Type = type, LegalEntityId = legalEntityId, BusinessDate = businessDate, Currency = currency,
            Reason = reason, Evidence = evidence, Lines = canonical }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    internal static bool FixedEquals(string left, string right) => left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static LedgerLineDraft NormalizeLine(LedgerLineDraft line) => line with
    {
        Debit = Normalize(line.Debit),
        Credit = Normalize(line.Credit),
        Memo = line.Memo?.Trim(),
        PartyType = line.PartyType?.Trim(),
    };

    private static decimal? Normalize(decimal? value) => value is null ? null
        : decimal.Parse(value.Value.ToString("0.############################", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private void AddAudit(JournalSourceDocument document, string action, string? before, string? after) =>
        db.FinancialAuditTrails.Add(new FinancialAuditTrail
        {
            Id = Guid.NewGuid(), Action = action, EntityType = "JournalSourceDocument", EntityId = document.Id,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(new { Status = before }, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(new { Detail = after }, JsonOptions),
            ActorUserId = currentUser.UserId, OccurredAtUtc = clock.UtcNow,
        });

    private static LedgerSourceDto ToDto(JournalSourceDocument document) => new(document.Id, document.DocumentNumber,
        document.Type, document.Status, document.LegalEntityId, document.BusinessDate, document.CurrencyCode,
        document.Reason, document.EvidenceReference, document.SubmittedFingerprint ?? document.ContentFingerprint,
        document.RowVersion, document.CreatedByUserId, document.SubmittedByUserId, document.ApprovedByUserId,
        document.PostedJournalEntryId, document.OriginalJournalEntryId, JsonSerializer.Deserialize<List<LedgerLineDraft>>(document.ContentJson, JsonOptions) ?? new());
}
