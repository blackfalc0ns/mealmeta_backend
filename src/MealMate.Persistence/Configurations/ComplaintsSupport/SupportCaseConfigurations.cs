using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ComplaintsSupport;

public sealed class SupportCaseConfiguration : IEntityTypeConfiguration<SupportCase>
{
    public void Configure(EntityTypeBuilder<SupportCase> builder)
    {
        builder.ToTable("SupportCases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.TriageStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.OwnerTeam).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.RelatedResourceType).HasMaxLength(80);
        builder.Property(x => x.PendingTransferReason).HasMaxLength(1000);
        builder.Property(x => x.ActiveTimingCycleId);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => new { x.RequesterUserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.RestaurantId, x.OwnerTeam, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.OwnerTeam, x.Status, x.Priority, x.CreatedAtUtc });
        builder.HasIndex(x => x.AssignedToUserId);
        builder.HasIndex(x => x.ActiveTimingCycleId);
        builder.HasIndex(x => new { x.TriageCountryId, x.TriageRegionId, x.Status });
    }
}

public sealed class SupportCaseMessageConfiguration : IEntityTypeConfiguration<SupportCaseMessage>
{
    public void Configure(EntityTypeBuilder<SupportCaseMessage> builder)
    {
        builder.ToTable("SupportCaseMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AttachmentKeys).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasOne<SupportCase>().WithMany().HasForeignKey(x => x.SupportCaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SupportCaseTask>().WithMany().HasForeignKey(x => new { x.SupportCaseId, x.SupportCaseTaskId })
            .HasPrincipalKey(x => new { x.SupportCaseId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SupportCaseId, x.CreatedAtUtc });
        builder.HasIndex(x => x.SupportCaseTaskId);
    }
}

public sealed class SupportCaseTaskConfiguration : IEntityTypeConfiguration<SupportCaseTask>
{
    public void Configure(EntityTypeBuilder<SupportCaseTask> builder)
    {
        builder.ToTable("SupportCaseTasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RecipientType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Question).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Result).HasMaxLength(4000);
        builder.HasOne<SupportCase>().WithMany().HasForeignKey(x => x.SupportCaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(x => new { x.SupportCaseId, x.Id });
        builder.HasIndex(x => new { x.RestaurantId, x.RecipientUserId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.SupportCaseId, x.Status });
    }
}

public sealed class SupportCaseLinkConfiguration : IEntityTypeConfiguration<SupportCaseLink>
{
    public void Configure(EntityTypeBuilder<SupportCaseLink> builder)
    {
        builder.ToTable("SupportCaseLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Relation).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(80).IsRequired();
        builder.HasOne<SupportCase>().WithMany().HasForeignKey(x => x.SupportCaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SupportCaseId, x.Relation, x.ResourceType, x.ResourceId }).IsUnique();
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
    }
}

public sealed class SupportCaseEventConfiguration : IEntityTypeConfiguration<SupportCaseEvent>
{
    public void Configure(EntityTypeBuilder<SupportCaseEvent> builder)
    {
        builder.ToTable("SupportCaseEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FromValue).HasMaxLength(100);
        builder.Property(x => x.ToValue).HasMaxLength(100);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.PayloadHash).HasMaxLength(64);
        builder.HasOne<SupportCase>().WithMany().HasForeignKey(x => x.SupportCaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SupportCaseId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ActorUserId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
