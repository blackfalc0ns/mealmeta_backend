using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CustomerCampaignQuoteConfiguration : IEntityTypeConfiguration<CustomerCampaignQuote>
{
    public void Configure(EntityTypeBuilder<CustomerCampaignQuote> builder)
    {
        builder.ToTable("CustomerCampaignQuotes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DurationDays).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.ReferencePrice).HasPrecision(18, 3);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 3);
        builder.Property(x => x.CouponDiscountAmount).HasPrecision(18, 3);
        builder.Property(x => x.FinalPrice).HasPrecision(18, 3);
        builder.Property(x => x.CustomerDiscountPercent).HasPrecision(9, 3);
        builder.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PromoCode).HasMaxLength(100);
        builder.Property(x => x.PricingPolicyVersion).IsRequired();
        builder.Property(x => x.ServiceDates).HasColumnType("date[]").IsRequired();

        builder.HasIndex(x => new { x.CustomerId, x.CampaignId, x.CampaignBundleId, x.DurationDays, x.StartDate });
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
