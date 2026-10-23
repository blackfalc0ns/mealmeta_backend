using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Common;

public sealed class UploadFileRecordConfiguration : IEntityTypeConfiguration<UploadFileRecord>
{
    public void Configure(EntityTypeBuilder<UploadFileRecord> builder)
    {
        builder.ToTable("UploadFileRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(100);
        builder.Property(x => x.Folder).HasMaxLength(100);

        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => x.UploadedByUserId);
        builder.HasIndex(x => new { x.Scope, x.Folder, x.SupportPayloadDeletedAtUtc, x.SupportCleanupLastAttemptAtUtc, x.UploadedAtUtc })
            .HasDatabaseName("IX_UploadFileRecords_SupportCleanup");
    }
}
