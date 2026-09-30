using MealMate.Domain.CustomerFinance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CustomerFinance;

public sealed class RefundOperationConfiguration : IEntityTypeConfiguration<RefundOperation>
{
    public void Configure(EntityTypeBuilder<RefundOperation> builder)
    {
        builder.ToTable("RefundOperations"); builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PaymentProvider).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PaymentId).HasMaxLength(150).IsRequired();
        builder.Property(x => x.InvoiceId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 3);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(100);
        builder.Property(x => x.ProviderRefundReference).HasMaxLength(150);
        builder.Property(x => x.Detail).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.OperationReference).IsUnique();
        builder.HasIndex(x => x.ProviderRefundId).IsUnique().HasFilter("\"ProviderRefundId\" IS NOT NULL");
        builder.HasIndex(x => new { x.PaymentProvider, x.PaymentId });
        builder.HasIndex(x => x.State);
    }
}
