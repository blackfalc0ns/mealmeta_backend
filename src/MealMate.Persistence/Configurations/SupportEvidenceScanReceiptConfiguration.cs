using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations;

public sealed class SupportEvidenceScanReceiptConfiguration : IEntityTypeConfiguration<SupportEvidenceScanReceipt>
{
    public void Configure(EntityTypeBuilder<SupportEvidenceScanReceipt> builder)
    {
        builder.ToTable("SupportEvidenceScanReceipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Verdict).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.UploadFileRecordId, x.Sha256, x.SizeBytes, x.ContentType, x.ValidationVersion, x.Verdict })
            .HasDatabaseName("IX_SupportEvidenceScanReceipts_Record_Digest");
        builder.HasOne<MealMate.Domain.Common.UploadFileRecord>().WithMany().HasForeignKey(x => x.UploadFileRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
