using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignPaymentTransactionConfiguration : IEntityTypeConfiguration<CampaignPaymentTransaction>
{
    public void Configure(EntityTypeBuilder<CampaignPaymentTransaction> builder)
    {
        builder.ToTable("CampaignPaymentTransactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CheckoutSessionId).IsRequired();
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.PaymentProvider).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProviderPaymentReference).HasMaxLength(200);
        builder.Property(x => x.Amount).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.HasIndex(x => new { x.PaymentProvider, x.ProviderTransactionId }).IsUnique();
        builder.HasIndex(x => x.CheckoutSessionId);
        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.ProcessingLeaseExpiresAtUtc);
        builder.HasIndex(x => x.Status);
    }
}
