using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class PaymentWebhookReceiptConfiguration : IEntityTypeConfiguration<PaymentWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookReceipt> builder)
    {
        builder.ToTable("PaymentWebhookReceipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Gateway).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EventKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(128);
        builder.Property(x => x.PaymentId).HasMaxLength(100);
        builder.Property(x => x.InvoiceId).HasMaxLength(100);
        builder.Property(x => x.Amount).HasPrecision(18, 3);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.CustomerReference).HasMaxLength(100);
        builder.Property(x => x.LastFulfillmentErrorCode).HasMaxLength(100);
        builder.HasIndex(x => new { x.Gateway, x.EventKey }).IsUnique();
        builder.HasIndex(x => new { x.Gateway, x.PaymentId }).IsUnique();
        builder.HasIndex(x => new { x.Gateway, x.InvoiceId });
        builder.HasIndex(x => x.SubscriptionId);
    }
}
