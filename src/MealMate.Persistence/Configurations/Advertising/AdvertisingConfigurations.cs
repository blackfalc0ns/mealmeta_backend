using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Advertising;

public sealed class AdvertisingSettingsConfiguration : IEntityTypeConfiguration<AdvertisingSettings>
{
    public void Configure(EntityTypeBuilder<AdvertisingSettings> builder)
    {
        builder.ToTable("AdvertisingSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessTimezone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LastChangeReason).HasMaxLength(500);
    }
}

public sealed class AdCommercialPolicyConfiguration : IEntityTypeConfiguration<AdCommercialPolicy>
{
    public void Configure(EntityTypeBuilder<AdCommercialPolicy> builder)
    {
        builder.ToTable("AdCommercialPolicies");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TiePolicy).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NextBidderPolicy).HasMaxLength(48).IsRequired();
        builder.Property(x => x.StopSettlementRule).HasMaxLength(48).IsRequired();

        builder.HasIndex(x => x.Version).IsUnique();
    }
}

public sealed class AdSpaceConfiguration : IEntityTypeConfiguration<AdSpace>
{
    public void Configure(EntityTypeBuilder<AdSpace> builder)
    {
        builder.ToTable("AdSpaces");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlacementKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MarketIdsJson).IsRequired();

        builder.HasIndex(x => x.PlacementKey).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class AdAuctionConfiguration : IEntityTypeConfiguration<AdAuction>
{
    public void Configure(EntityTypeBuilder<AdAuction> builder)
    {
        builder.ToTable("AdAuctions");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.AdSpaceId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.BidsCloseAtUtc);

        builder.HasOne(x => x.AdSpace)
            .WithMany()
            .HasForeignKey(x => x.AdSpaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Bids)
            .WithOne(x => x.AdAuction)
            .HasForeignKey(x => x.AdAuctionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdBidConfiguration : IEntityTypeConfiguration<AdBid>
{
    public void Configure(EntityTypeBuilder<AdBid> builder)
    {
        builder.ToTable("AdBids");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();

        // Duplicate bids with the same idempotency key are impossible per restaurant/auction.
        builder.HasIndex(x => new { x.AdAuctionId, x.RestaurantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.AdAuctionId, x.RestaurantId });
    }
}

public sealed class AdPlacementConfiguration : IEntityTypeConfiguration<AdPlacement>
{
    public void Configure(EntityTypeBuilder<AdPlacement> builder)
    {
        builder.ToTable("AdPlacements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TargetType).HasMaxLength(64);
        builder.Property(x => x.ReviewNotes).HasMaxLength(1000);
        builder.Property(x => x.StopReason).HasMaxLength(500);

        builder.HasOne(x => x.AdAuction)
            .WithOne()
            .HasForeignKey<AdPlacement>(x => x.AdAuctionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.AdSpaceId, x.Status });
        builder.HasIndex(x => x.RestaurantId);
    }
}

public sealed class AdPaymentTransactionConfiguration : IEntityTypeConfiguration<AdPaymentTransaction>
{
    public void Configure(EntityTypeBuilder<AdPaymentTransaction> builder)
    {
        builder.ToTable("AdPaymentTransactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PaymentProvider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired().IsConcurrencyToken();

        builder.HasIndex(x => new { x.PaymentProvider, x.ProviderTransactionId }).IsUnique();
        builder.HasIndex(x => x.AdAuctionId);
    }
}

public sealed class AdFinancialLedgerEntryConfiguration : IEntityTypeConfiguration<AdFinancialLedgerEntry>
{
    public void Configure(EntityTypeBuilder<AdFinancialLedgerEntry> builder)
    {
        builder.ToTable("AdFinancialLedgerEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(128);
        builder.Property(x => x.ExternalRefundIdentifier).HasMaxLength(128);
        builder.Property(x => x.ProviderRefundId).HasMaxLength(128);
        builder.Property(x => x.RefundReviewStatus).HasMaxLength(32);
        builder.Property(x => x.RefundReviewReason).HasMaxLength(500);

        // Unique refund identifiers prevent double settlement entries.
        builder.HasIndex(x => new { x.Source, x.ProviderTransactionId, x.Action }).IsUnique();
        builder.HasIndex(x => x.AdAuctionId);
    }
}

public sealed class AdPlacementEventConfiguration : IEntityTypeConfiguration<AdPlacementEvent>
{
    public void Configure(EntityTypeBuilder<AdPlacementEvent> builder)
    {
        builder.ToTable("AdPlacementEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(16).IsRequired();

        builder.HasIndex(x => new { x.PlacementId, x.EventId }).IsUnique();
        builder.HasIndex(x => x.OccurredAtUtc);
    }
}

public sealed class AdAuctionNextBidderOfferConfiguration : IEntityTypeConfiguration<AdAuctionNextBidderOffer>
{
    public void Configure(EntityTypeBuilder<AdAuctionNextBidderOffer> builder)
    {
        builder.ToTable("AdAuctionNextBidderOffers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ResponseReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.AdAuctionId, x.AdBidId }).IsUnique();
        builder.HasIndex(x => new { x.RestaurantId, x.Status });
    }
}
