using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Foundation;

public sealed class DocumentVerificationConfiguration : IEntityTypeConfiguration<DocumentVerification>
{
    public void Configure(EntityTypeBuilder<DocumentVerification> builder)
    {
        builder.ToTable("DocumentVerifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => new { x.DocumentId, x.Status });
        builder.HasIndex(x => x.ReviewedByUserId);
    }
}
