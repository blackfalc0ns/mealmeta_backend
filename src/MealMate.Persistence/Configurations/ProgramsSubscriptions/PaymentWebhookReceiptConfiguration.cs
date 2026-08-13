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
        builder.HasIndex(x => new { x.Gateway, x.EventKey }).IsUnique();
        builder.HasIndex(x => x.SubscriptionId);
    }
}
