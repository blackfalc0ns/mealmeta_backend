using MealMate.Domain.CentralAccounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CentralAccounting;

public sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("LegalEntities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LegalNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LegalNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RegistrationNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.BusinessTimezone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.HasIndex(x => x.RegistrationNo).IsUnique();
    }
}

public sealed class ReportingProfileConfiguration : IEntityTypeConfiguration<ReportingProfile>
{
    public void Configure(EntityTypeBuilder<ReportingProfile> builder)
    {
        builder.ToTable("ReportingProfiles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReportType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntryPointId).HasMaxLength(200);
        builder.Property(x => x.ValidatorProfile).HasMaxLength(100);
        builder.Property(x => x.Applicability).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.HasIndex(x => new { x.LegalEntityId, x.Code, x.ReportType }).IsUnique();

        builder.HasOne(x => x.LegalEntity)
            .WithMany()
            .HasForeignKey(x => x.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RegulatoryRequirementConfiguration : IEntityTypeConfiguration<RegulatoryRequirement>
{
    public void Configure(EntityTypeBuilder<RegulatoryRequirement> builder)
    {
        builder.ToTable("RegulatoryRequirements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Authority).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RequirementCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.EvidenceHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => new { x.Authority, x.RequirementCode }).IsUnique();
        builder.HasIndex(x => x.EffectiveFromUtc);
    }
}
