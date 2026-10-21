using MealMate.Domain.CentralAccounting;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence;

/// <summary>EF-path guard. Database triggers provide the corresponding SQL-path barrier.</summary>
internal static class FinanceLedgerPersistenceGuard
{
    public static void Validate(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State == EntityState.Added) continue;
            if ((entry.State is EntityState.Modified or EntityState.Deleted)
                && entry.Property(x => x.LegalEntityId).OriginalValue is not null
                && entry.Property(x => x.Status).OriginalValue == MealMate.Domain.Enums.JournalEntryStatus.Posted)
                throw new InvalidOperationException("ledger.posted_journal_immutable");
            if (entry.State == EntityState.Deleted && entry.Property(x => x.LegalEntityId).OriginalValue is not null)
                throw new InvalidOperationException("ledger.governed_journal_delete_forbidden");
        }

        foreach (var entry in db.ChangeTracker.Entries<JournalEntryLine>())
        {
            if (entry.State == EntityState.Added)
            {
                var parent = db.ChangeTracker.Entries<JournalEntry>()
                    .SingleOrDefault(x => x.Entity.Id == entry.Entity.JournalEntryId);
                if (entry.Entity.LegalEntityId is not null
                    && (parent is null || parent.State != EntityState.Added))
                    throw new InvalidOperationException("ledger.journal_line_append_only");
                continue;
            }

            if (entry.Entity.LegalEntityId is not null || entry.Property(x => x.LegalEntityId).OriginalValue is not null)
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                    throw new InvalidOperationException("ledger.journal_line_append_only");
        }

        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.Entity is Money
            && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var ownership = entry.Metadata.FindOwnership();
            if (ownership?.PrincipalEntityType.ClrType != typeof(JournalEntryLine)) continue;
            var foreignKeyValues = ownership.Properties.Select(property => entry.Property(property.Name).CurrentValue).ToArray();
            var owner = db.ChangeTracker.Entries<JournalEntryLine>().FirstOrDefault(candidate =>
                ownership.PrincipalKey.Properties.Select(property => candidate.Property(property.Name).CurrentValue)
                    .SequenceEqual(foreignKeyValues));
            if (owner?.State == EntityState.Added && db.ChangeTracker.Entries<JournalEntry>()
                .Any(journal => journal.Entity.Id == owner.Entity.JournalEntryId && journal.State == EntityState.Added))
                continue;
            if (owner is not null && (owner.Entity.LegalEntityId is not null
                || owner.Property(x => x.LegalEntityId).OriginalValue is not null))
                throw new InvalidOperationException("ledger.journal_line_append_only");
        }

        foreach (var entry in db.ChangeTracker.Entries<JournalSourceDocument>())
        {
            if (entry.State == EntityState.Deleted
                && entry.Property(x => x.Status).OriginalValue != JournalSourceDocumentStatus.Draft)
                throw new InvalidOperationException("ledger.submitted_source_delete_forbidden");

            if (entry.State != EntityState.Modified
                || entry.Property(x => x.Status).OriginalValue == JournalSourceDocumentStatus.Draft)
                continue;

            var immutable = new[]
            {
                nameof(JournalSourceDocument.Type), nameof(JournalSourceDocument.LegalEntityId),
                nameof(JournalSourceDocument.BusinessDate), nameof(JournalSourceDocument.CurrencyCode),
                nameof(JournalSourceDocument.Reason), nameof(JournalSourceDocument.EvidenceReference),
                nameof(JournalSourceDocument.ContentJson), nameof(JournalSourceDocument.ContentFingerprint),
                nameof(JournalSourceDocument.CreatedByUserId), nameof(JournalSourceDocument.CreatedAtUtc),
                nameof(JournalSourceDocument.OriginalJournalEntryId), nameof(JournalSourceDocument.SubmittedFingerprint),
            };
            if (immutable.Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("ledger.submitted_source_immutable");

            var previous = entry.Property(x => x.Status).OriginalValue;
            var next = entry.Entity.Status;
            var valid = previous == next
                || previous == JournalSourceDocumentStatus.PendingApproval && next is JournalSourceDocumentStatus.Approved or JournalSourceDocumentStatus.Rejected
                || previous == JournalSourceDocumentStatus.Approved && next == JournalSourceDocumentStatus.Posted;
            if (!valid) throw new InvalidOperationException("ledger.source_lifecycle_invalid");
        }

        foreach (var entry in db.ChangeTracker.Entries<AccountingPeriod>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("ledger.period_delete_forbidden");
            if (entry.State != EntityState.Modified) continue;
            if (entry.Entity.IsDeleted)
                throw new InvalidOperationException("ledger.period_soft_delete_forbidden");
            if (entry.Property(x => x.LegalEntityId).IsModified || entry.Property(x => x.StartDate).IsModified
                || entry.Property(x => x.EndDateExclusive).IsModified)
                throw new InvalidOperationException("ledger.period_window_immutable");
            var oldState = entry.Property(x => x.State).OriginalValue;
            var nextState = entry.Entity.State;
            if (oldState != nextState && !(oldState == AccountingPeriodState.Open && nextState == AccountingPeriodState.PendingClose)
                && !(oldState == AccountingPeriodState.PendingClose && nextState is AccountingPeriodState.Open or AccountingPeriodState.Closed))
                throw new InvalidOperationException("ledger.period_lifecycle_invalid");
            if (oldState != AccountingPeriodState.Closed) continue;
            var closedProperties = new[] { nameof(AccountingPeriod.LegalEntityId), nameof(AccountingPeriod.StartDate),
                nameof(AccountingPeriod.EndDateExclusive), nameof(AccountingPeriod.State), nameof(AccountingPeriod.ClosedAtUtc),
                nameof(AccountingPeriod.ClosedByUserId), nameof(AccountingPeriod.CloseReason), nameof(AccountingPeriod.ClosePostedJournalCount),
                nameof(AccountingPeriod.CloseDebitTotal), nameof(AccountingPeriod.CloseCreditTotal), nameof(AccountingPeriod.CloseWatermarkUtc) };
            if (closedProperties.Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("ledger.closed_period_immutable");
        }

        foreach (var entry in db.ChangeTracker.Entries<ChartOfAccount>())
            if (entry.State == EntityState.Modified && entry.Entity.LegalEntityId is not null && entry.Entity.IsDeleted)
                throw new InvalidOperationException("ledger.governed_account_soft_delete_forbidden");

        foreach (var entry in db.ChangeTracker.Entries<FinancialAuditTrail>())
            if (entry.State is EntityState.Modified or EntityState.Deleted
                && entry.Property(x => x.Action).OriginalValue.StartsWith("Ledger", StringComparison.Ordinal))
                throw new InvalidOperationException("ledger.audit_append_only");

        RequireAppendOnly(db.ChangeTracker.Entries<JournalSourceLink>());
        RequireAppendOnly(db.ChangeTracker.Entries<PostingIdentity>());
        RequireAppendOnly(db.ChangeTracker.Entries<JournalReversal>());
    }

    private static void RequireAppendOnly<TEntity>(IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>> entries)
        where TEntity : class
    {
        if (entries.Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("ledger.posting_trace_append_only");
    }
}
