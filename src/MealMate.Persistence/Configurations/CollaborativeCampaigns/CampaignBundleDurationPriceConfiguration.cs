using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignBundleDurationPriceConfiguration : IEntityTypeConfiguration<CampaignBundleDurationPrice>
{
    public void Configure(EntityTypeBuilder<CampaignBundleDurationPrice> builder)
    {
        builder.ToTable("CampaignBundleDurationPrices");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DurationDays).IsRequired();
        builder.OwnsRequiredMoney(x => x.ReferenceCustomerPrice, "ReferenceCustomerPrice");
        builder.Property(x => x.Source).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.CapturedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.CampaignBundleId, x.DurationDays }).IsUnique();
        builder.HasIndex(x => x.CampaignBundleId);

        builder.HasOne(x => x.CampaignBundle)
            .WithMany(x => x.DurationPrices)
            .HasForeignKey(x => x.CampaignBundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
