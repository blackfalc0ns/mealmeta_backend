using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace MealMate.Persistence.Configurations;
public sealed class PaymentGatewayAttemptConfiguration : IEntityTypeConfiguration<PaymentGatewayAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayAttempt> builder)
    {
        builder.ToTable("PaymentGatewayAttempts");
        builder.Property(x => x.TrackId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PaymentId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 3);
        builder.HasIndex(x => x.TrackId).IsUnique();
        builder.HasIndex(x => new { x.Provider, x.IsSandbox, x.PaymentId }).IsUnique().HasFilter("\"PaymentId\" <> ''");
    }
}
