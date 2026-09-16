using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignFinancialLedgerConfiguration : IEntityTypeConfiguration<CampaignFinancialLedger>
{
    public void Configure(EntityTypeBuilder<CampaignFinancialLedger> builder)
    {
        builder.ToTable("CampaignFinancialLedgers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JournalId).IsRequired();
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.EntryType).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.JournalId);
        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.CheckoutSessionId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.EntryType);
        builder.HasIndex(x => x.CreatedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
