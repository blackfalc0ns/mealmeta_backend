using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class OfferCheckoutQuoteConfiguration : IEntityTypeConfiguration<OfferCheckoutQuote>
{
    public void Configure(EntityTypeBuilder<OfferCheckoutQuote> builder)
    {
        builder.ToTable("OfferCheckoutQuotes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.OfferId).IsRequired();
        builder.Property(x => x.ProgramId).IsRequired();
        builder.Property(x => x.BundleId).IsRequired();
        builder.Property(x => x.Tier).IsRequired();
        builder.Property(x => x.DurationDays).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PromoCode).HasMaxLength(50);
        builder.Property(x => x.PayloadSnapshotHash).HasMaxLength(128);

        builder.Property(x => x.BasePrice).HasPrecision(18, 3);
        builder.Property(x => x.CampaignDiscount).HasPrecision(18, 3);
        builder.Property(x => x.PromoDiscount).HasPrecision(18, 3);
        builder.Property(x => x.WalletCredit).HasPrecision(18, 3);
        builder.Property(x => x.GatewayFee).HasPrecision(18, 3);
        builder.Property(x => x.FinalPrice).HasPrecision(18, 3);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.OfferId);
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
