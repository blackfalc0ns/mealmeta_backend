using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Foundation;

public sealed class RuleVersionConfiguration : IEntityTypeConfiguration<RuleVersion>
{
    public void Configure(EntityTypeBuilder<RuleVersion> builder)
    {
        builder.ToTable("RuleVersions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RuleKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.VersionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.RuleKey, x.VersionCode }).IsUnique();
        builder.HasIndex(x => new { x.RuleKey, x.ApprovalStatus, x.EffectiveFromUtc });
        builder.HasIndex(x => x.ScopeEntityId);

        builder.HasOne(x => x.PreviousVersion)
            .WithMany()
            .HasForeignKey(x => x.PreviousVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
