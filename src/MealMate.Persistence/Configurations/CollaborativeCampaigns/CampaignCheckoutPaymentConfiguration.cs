using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignCheckoutPaymentConfiguration : IEntityTypeConfiguration<CampaignCheckoutSession>
{
    public void Configure(EntityTypeBuilder<CampaignCheckoutSession> builder)
    {
        builder.Property(s => s.PaymentId).HasMaxLength(200);
        builder.Property(s => s.PaymentUrl).HasMaxLength(2048);
        builder.Property(s => s.PaymentIdempotencyKey).HasMaxLength(200);
        builder.Property(s => s.PaymentRequestHash).HasMaxLength(128);
        builder.Property(s => s.PaymentCallbackUrl).HasMaxLength(2048);
        builder.Property(s => s.PaymentErrorUrl).HasMaxLength(2048);
        builder.Property(s => s.PaymentInitializationErrorCode).HasMaxLength(100);
        builder.HasIndex(s => new { s.CustomerId, s.PaymentIdempotencyKey }).IsUnique()
            .HasFilter("\"PaymentIdempotencyKey\" IS NOT NULL");
        builder.HasIndex(s => s.PaymentId).IsUnique().HasFilter("\"PaymentId\" IS NOT NULL");
    }
}
