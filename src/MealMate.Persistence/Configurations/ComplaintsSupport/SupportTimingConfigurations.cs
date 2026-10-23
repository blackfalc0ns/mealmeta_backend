using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ComplaintsSupport;

public sealed class SupportTimingPolicyConfiguration : IEntityTypeConfiguration<SupportTimingPolicy>
{
    public void Configure(EntityTypeBuilder<SupportTimingPolicy> builder)
    {
        builder.ToTable("SupportTimingPolicies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.PublishedAtUtc).IsRequired();
        builder.Property(x => x.PublishedByUserId).IsRequired();
        builder.Property(x => x.PublicationReason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();

        builder.HasMany(x => x.Rules)
            .WithOne()
            .HasForeignKey(r => r.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Rules).AutoInclude();

        builder.HasIndex(x => x.Version).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
    }
}

public sealed class SupportTimingRuleConfiguration : IEntityTypeConfiguration<SupportTimingRule>
{
    public void Configure(EntityTypeBuilder<SupportTimingRule> builder)
    {
        builder.ToTable("SupportTimingRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PolicyId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.FirstResponseDeadline).IsRequired();
        builder.Property(x => x.ResolutionDeadline).IsRequired();
        builder.Property(x => x.EscalationThreshold).IsRequired();
        builder.Property(x => x.RequiresImmediateAck).IsRequired();
        builder.Property(x => x.EvaluationOrder).IsRequired();

        builder.HasIndex(x => new { x.PolicyId, x.EvaluationOrder });
    }
}

public sealed class SupportCaseTimingCycleConfiguration : IEntityTypeConfiguration<SupportCaseTimingCycle>
{
    public void Configure(EntityTypeBuilder<SupportCaseTimingCycle> builder)
    {
        builder.ToTable("SupportCaseTimingCycles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SupportCaseId).IsRequired();
        builder.Property(x => x.CycleNumber).IsRequired();
        builder.Property(x => x.StartedAtUtc).IsRequired();
        builder.Property(x => x.EscalationDeadlineUtc);
        builder.Property(x => x.LastWorkerAttemptAtUtc);
        builder.Property(x => x.EscalationReason).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasOne<SupportCase>()
            .WithMany()
            .HasForeignKey(x => x.SupportCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SupportCaseId, x.CycleNumber }).IsUnique();
        builder.HasIndex(x => new { x.IsResolutionBreached, x.ResolvedAtUtc });
        builder.HasIndex(x => new { x.IsFirstResponseBreached, x.FirstResponseAtUtc });
        builder.HasIndex(x => new { x.IsEscalated, x.EscalatedAtUtc });
        builder.HasIndex(x => x.LastWorkerAttemptAtUtc);
    }
}
