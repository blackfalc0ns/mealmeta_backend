using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TotalDiscountRate).HasPercentageConversion();
        builder.Property(x => x.MealMateShareRate).HasPercentageConversion();
        builder.Property(x => x.RestaurantShareRate).HasPercentageConversion();

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => x.FundingMode);
        builder.HasIndex(x => new { x.EnrollmentOpensAtUtc, x.EnrollmentClosesAtUtc });
    }
}

public sealed class CampaignDiscountSplitConfiguration : IEntityTypeConfiguration<CampaignDiscountSplit>
{
    public void Configure(EntityTypeBuilder<CampaignDiscountSplit> builder)
    {
        builder.ToTable("CampaignDiscountSplits");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MealMateShareRate).HasPercentageConversion();
        builder.Property(x => x.RestaurantShareRate).HasPercentageConversion();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.FundingMode);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.DiscountSplits)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignProgramConfiguration : IEntityTypeConfiguration<CampaignProgram>
{
    public void Configure(EntityTypeBuilder<CampaignProgram> builder)
    {
        builder.ToTable("CampaignPrograms");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.CampaignId, x.ProgramId }).IsUnique();
        builder.HasIndex(x => x.ProgramId);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.Programs)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignBundleConfiguration : IEntityTypeConfiguration<CampaignBundle>
{
    public void Configure(EntityTypeBuilder<CampaignBundle> builder)
    {
        builder.ToTable("CampaignBundles");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.CampaignId, x.BundleId }).IsUnique();
        builder.HasIndex(x => x.BundleId);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.Bundles)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.DurationPrices)
            .WithOne(x => x.CampaignBundle)
            .HasForeignKey(x => x.CampaignBundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CampaignRestaurantEnrollmentConfiguration : IEntityTypeConfiguration<CampaignRestaurantEnrollment>
{
    public void Configure(EntityTypeBuilder<CampaignRestaurantEnrollment> builder)
    {
        builder.ToTable("CampaignRestaurantEnrollments");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.CampaignId, x.RestaurantId });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.RestaurantEnrollments)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignRestaurantCapacityConfiguration : IEntityTypeConfiguration<CampaignRestaurantCapacity>
{
    public void Configure(EntityTypeBuilder<CampaignRestaurantCapacity> builder)
    {
        builder.ToTable("CampaignRestaurantCapacities");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.CampaignId, x.RestaurantId, x.ServiceDate });
        builder.HasIndex(x => x.ServiceDate);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.RestaurantCapacities)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignSubscriberCapConfiguration : IEntityTypeConfiguration<CampaignSubscriberCap>
{
    public void Configure(EntityTypeBuilder<CampaignSubscriberCap> builder)
    {
        builder.ToTable("CampaignSubscriberCaps");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.LastReviewedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.SubscriberCaps)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignCapacityReviewConfiguration : IEntityTypeConfiguration<CampaignCapacityReview>
{
    public void Configure(EntityTypeBuilder<CampaignCapacityReview> builder)
    {
        builder.ToTable("CampaignCapacityReviews");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.ReviewedByUserId);
        builder.HasIndex(x => x.Decision);
        builder.HasIndex(x => x.ReviewedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.CapacityReviews)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignRestaurantApprovalConfiguration : IEntityTypeConfiguration<CampaignRestaurantApproval>
{
    public void Configure(EntityTypeBuilder<CampaignRestaurantApproval> builder)
    {
        builder.ToTable("CampaignRestaurantApprovals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.EnrollmentId);
        builder.HasIndex(x => x.ApprovedByUserId);
        builder.HasIndex(x => x.Decision);
        builder.HasIndex(x => x.DecidedAtUtc);

        builder.HasOne(x => x.Enrollment)
            .WithMany(x => x.Approvals)
            .HasForeignKey(x => x.EnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignAccountingEntryConfiguration : IEntityTypeConfiguration<CampaignAccountingEntry>
{
    public void Configure(EntityTypeBuilder<CampaignAccountingEntry> builder)
    {
        builder.ToTable("CampaignAccountingEntries");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.CustomerPriceBefore, "CustomerPriceBefore");
        builder.OwnsRequiredMoney(x => x.CustomerDiscount, "CustomerDiscount");
        builder.OwnsRequiredMoney(x => x.CustomerPriceAfter, "CustomerPriceAfter");
        builder.OwnsRequiredMoney(x => x.MealMateFundingNet, "MealMateFundingNet");
        builder.OwnsRequiredMoney(x => x.RestaurantFundingNet, "RestaurantFundingNet");
        builder.OwnsRequiredMoney(x => x.BaseSupplyGross, "BaseSupplyGross");
        builder.OwnsMoney(x => x.CampaignSupplyGross, "CampaignSupplyGross", required: false);
        builder.OwnsRequiredMoney(x => x.Commission, "Commission");
        builder.OwnsRequiredMoney(x => x.RestaurantNet, "RestaurantNet");
        builder.OwnsRequiredMoney(x => x.ReferenceDailyBox, "ReferenceDailyBox");

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.CampaignId, x.RestaurantId, x.RecognizedAtUtc });
        builder.HasIndex(x => x.FundingMode);
        builder.HasIndex(x => x.RecognizedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany(x => x.AccountingEntries)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
