using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class SubscriptionRestaurantPoolSnapshotConfiguration : IEntityTypeConfiguration<SubscriptionRestaurantPoolSnapshot>
{
    public void Configure(EntityTypeBuilder<SubscriptionRestaurantPoolSnapshot> builder)
    {
        builder.ToTable("SubscriptionRestaurantPoolSnapshots");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SubscriptionId).IsUnique();
        builder.HasIndex(x => x.EligibilityCheckedAtUtc);
        builder.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionRestaurantSnapshotConfiguration : IEntityTypeConfiguration<SubscriptionRestaurantSnapshot>
{
    public void Configure(EntityTypeBuilder<SubscriptionRestaurantSnapshot> builder)
    {
        builder.ToTable("SubscriptionRestaurantSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OfferingSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.SubscriptionId, x.RestaurantId }).IsUnique();
        builder.HasIndex(x => new { x.PoolSnapshotId, x.RestaurantId }).IsUnique();
        builder.HasIndex(x => x.RestaurantId);
        builder.HasOne<SubscriptionRestaurantPoolSnapshot>().WithMany().HasForeignKey(x => x.PoolSnapshotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionRestaurantDaySnapshotConfiguration : IEntityTypeConfiguration<SubscriptionRestaurantDaySnapshot>
{
    public void Configure(EntityTypeBuilder<SubscriptionRestaurantDaySnapshot> builder)
    {
        builder.ToTable("SubscriptionRestaurantDaySnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SlotRequirementsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MealsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.SubscriptionId, x.RestaurantId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => new { x.RestaurantSnapshotId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => x.MenuPlanVersionId);
        builder.HasIndex(x => x.ContentHash);
        builder.HasOne<SubscriptionRestaurantSnapshot>().WithMany().HasForeignKey(x => x.RestaurantSnapshotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionSnapshotMigrationIssueConfiguration : IEntityTypeConfiguration<SubscriptionSnapshotMigrationIssue>
{
    public void Configure(EntityTypeBuilder<SubscriptionSnapshotMigrationIssue> builder)
    {
        builder.ToTable("SubscriptionSnapshotMigrationIssues");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReasonCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => x.SubscriptionId).IsUnique();
        builder.HasIndex(x => new { x.ResolvedAtUtc, x.RecordedAtUtc });
        builder.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
