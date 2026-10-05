using MealMate.Domain.Offers;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Offers;

public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.BannerImageUrl).HasMaxLength(1000);

        builder.Property(x => x.DiscountRate).HasPercentageConversion();
        builder.OwnsMoney(x => x.DiscountAmount, "DiscountAmount", required: false);

        builder.Property(x => x.PlatformShareRate).HasPercentageConversion();
        builder.Property(x => x.RestaurantShareRate).HasPercentageConversion();
        builder.OwnsMoney(x => x.MinOrderAmount, "MinOrderAmount", required: false);

        builder.Property(x => x.CurrentUsageCount).IsConcurrencyToken();
        builder.Property(x => x.AllowPromoCodeStacking).HasDefaultValue(false);

        builder.Property(x => x.PromoCode).HasMaxLength(50);
        builder.Property(x => x.TermsAndConditionsAr).HasMaxLength(2000);
        builder.Property(x => x.TermsAndConditionsEn).HasMaxLength(2000);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.PromoCode);
    }
}

public sealed class OfferProgramConfiguration : IEntityTypeConfiguration<OfferProgram>
{
    public void Configure(EntityTypeBuilder<OfferProgram> builder)
    {
        builder.ToTable("OfferPrograms");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.ProgramId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligiblePrograms)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferBundleConfiguration : IEntityTypeConfiguration<OfferBundle>
{
    public void Configure(EntityTypeBuilder<OfferBundle> builder)
    {
        builder.ToTable("OfferBundles");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.BundleId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligibleBundles)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferRestaurantConfiguration : IEntityTypeConfiguration<OfferRestaurant>
{
    public void Configure(EntityTypeBuilder<OfferRestaurant> builder)
    {
        builder.ToTable("OfferRestaurants");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.RestaurantId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligibleRestaurants)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferRedemptionConfiguration : IEntityTypeConfiguration<OfferRedemption>
{
    public void Configure(EntityTypeBuilder<OfferRedemption> builder)
    {
        builder.ToTable("OfferRedemptions");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.DiscountAppliedAmount, "DiscountAppliedAmount");
        builder.OwnsRequiredMoney(x => x.PlatformFundedAmount, "PlatformFundedAmount");
        builder.OwnsRequiredMoney(x => x.RestaurantFundedAmount, "RestaurantFundedAmount");

        builder.HasIndex(x => x.OfferId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.RedeemedAtUtc);
        // One coupon redemption per subscription: repeated payment callbacks or retries must
        // never record a second use for the same checkout.
        builder.HasIndex(x => new { x.OfferId, x.SubscriptionId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.Redemptions)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferAuditLogConfiguration : IEntityTypeConfiguration<OfferAuditLog>
{
    public void Configure(EntityTypeBuilder<OfferAuditLog> builder)
    {
        builder.ToTable("OfferAuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.JustificationReason).HasMaxLength(1000);

        builder.HasIndex(x => x.OfferId);
        builder.HasIndex(x => x.CreatedAtUtc);

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
