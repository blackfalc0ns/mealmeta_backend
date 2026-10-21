using MealMate.Domain.CentralAccounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CentralAccounting;

public sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.ToTable("AccountingPeriods", table =>
        {
            table.HasCheckConstraint("CK_AccountingPeriods_DateWindow", "\"EndDateExclusive\" > \"StartDate\"");
            table.HasCheckConstraint("CK_AccountingPeriods_CloseTotals", "(\"CloseDebitTotal\" IS NULL AND \"CloseCreditTotal\" IS NULL) OR (\"CloseDebitTotal\" >= 0 AND \"CloseDebitTotal\" = \"CloseCreditTotal\")");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.State).HasConversion<int>();
        builder.Property(x => x.PendingCloseReason).HasMaxLength(1000);
        builder.Property(x => x.CloseReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.LegalEntityId, x.StartDate, x.EndDateExclusive }).IsUnique();
        builder.HasIndex(x => new { x.LegalEntityId, x.State, x.StartDate });
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PostingIdentityConfiguration : IEntityTypeConfiguration<PostingIdentity>
{
    public void Configure(EntityTypeBuilder<PostingIdentity> builder)
    {
        builder.ToTable("PostingIdentities", table => table.HasCheckConstraint(
            "CK_PostingIdentities_Hash", "length(\"EconomicHash\") = 64"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Effect).HasConversion<int>();
        builder.Property(x => x.EconomicHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.LegalEntityId, x.SourceType, x.SourceId, x.ServiceDayKey, x.Effect, x.AdjustmentKey }).IsUnique()
            .HasDatabaseName("UX_PostingIdentities_EconomicKey");
        builder.HasIndex(x => x.JournalEntryId).IsUnique();
        builder.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JournalSourceDocumentConfiguration : IEntityTypeConfiguration<JournalSourceDocument>
{
    public void Configure(EntityTypeBuilder<JournalSourceDocument> builder)
    {
        builder.ToTable("JournalSourceDocuments", table =>
        {
            table.HasCheckConstraint("CK_JournalSourceDocuments_Currency", "\"CurrencyCode\" = 'KWD'");
            table.HasCheckConstraint("CK_JournalSourceDocuments_Fingerprints", "length(\"ContentFingerprint\") = 64 AND (\"SubmittedFingerprint\" IS NULL OR length(\"SubmittedFingerprint\") = 64)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.EvidenceReference).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SubmittedFingerprint).HasMaxLength(64);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.DocumentNumber).IsUnique();
        builder.HasIndex(x => new { x.LegalEntityId, x.Status, x.BusinessDate });
        builder.HasIndex(x => x.PostedJournalEntryId).IsUnique().HasFilter("\"PostedJournalEntryId\" IS NOT NULL");
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.PostedJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.OriginalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JournalSourceLinkConfiguration : IEntityTypeConfiguration<JournalSourceLink>
{
    public void Configure(EntityTypeBuilder<JournalSourceLink> builder)
    {
        builder.ToTable("JournalSourceLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.SourceFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.SourceDocumentId).IsUnique();
        builder.HasIndex(x => x.JournalEntryId).IsUnique();
        builder.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.JournalEntry).WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JournalReversalConfiguration : IEntityTypeConfiguration<JournalReversal>
{
    public void Configure(EntityTypeBuilder<JournalReversal> builder)
    {
        builder.ToTable("JournalReversals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.EvidenceReference).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.OriginalJournalEntryId).IsUnique();
        builder.HasIndex(x => x.ReversalJournalEntryId).IsUnique();
        builder.HasIndex(x => x.SourceDocumentId).IsUnique();
        builder.HasOne(x => x.OriginalJournalEntry).WithMany().HasForeignKey(x => x.OriginalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReversalJournalEntry).WithMany().HasForeignKey(x => x.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalSourceDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
