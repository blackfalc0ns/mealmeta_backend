using MealMate.Domain.RestaurantFinance;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.RestaurantFinance;

public sealed class MealPayableConfiguration : IEntityTypeConfiguration<MealPayable>
{
    public void Configure(EntityTypeBuilder<MealPayable> builder)
    {
        builder.ToTable("MealPayables");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.GrossSupply, "GrossSupply");
        builder.OwnsRequiredMoney(x => x.Commission, "Commission");
        builder.OwnsMoney(x => x.CampaignContributionNet, "CampaignContributionNet", required: false);
        builder.OwnsRequiredMoney(x => x.NetPayable, "NetPayable");
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.RestaurantId, x.SubscriptionDayId });
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.RecognizedAtUtc);
    }
}

public sealed class RestaurantCommissionRecordConfiguration : IEntityTypeConfiguration<RestaurantCommissionRecord>
{
    public void Configure(EntityTypeBuilder<RestaurantCommissionRecord> builder)
    {
        builder.ToTable("RestaurantCommissionRecords");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.GrossSupply, "GrossSupply");
        builder.Property(x => x.CommissionRate).HasPercentageConversion();
        builder.OwnsRequiredMoney(x => x.CommissionAmount, "CommissionAmount");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.RecognizedAtUtc);
    }
}

public sealed class ComplaintDeductionRecordConfiguration : IEntityTypeConfiguration<ComplaintDeductionRecord>
{
    public void Configure(EntityTypeBuilder<ComplaintDeductionRecord> builder)
    {
        builder.ToTable("ComplaintDeductionRecords");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AppliedAtUtc);
    }
}

public sealed class RestaurantMonthlyInvoiceConfiguration : IEntityTypeConfiguration<RestaurantMonthlyInvoice>
{
    public void Configure(EntityTypeBuilder<RestaurantMonthlyInvoice> builder)
    {
        builder.ToTable("RestaurantMonthlyInvoices");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
        builder.OwnsRequiredMoney(x => x.GrossTotal, "GrossTotal");
        builder.OwnsRequiredMoney(x => x.CommissionTotal, "CommissionTotal");
        builder.OwnsRequiredMoney(x => x.DeductionsTotal, "DeductionsTotal");
        builder.OwnsRequiredMoney(x => x.NetTotal, "NetTotal");

        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.PeriodYear, x.PeriodMonth });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IssuedAtUtc);
    }
}

public sealed class RestaurantPartialPaymentConfiguration : IEntityTypeConfiguration<RestaurantPartialPayment>
{
    public void Configure(EntityTypeBuilder<RestaurantPartialPayment> builder)
    {
        builder.ToTable("RestaurantPartialPayments");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.Property(x => x.Reference).HasMaxLength(200);

        builder.HasIndex(x => x.MonthlyInvoiceId);
        builder.HasIndex(x => x.PaidAtUtc);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.MonthlyInvoice)
            .WithMany(x => x.PartialPayments)
            .HasForeignKey(x => x.MonthlyInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantPayoutConfiguration : IEntityTypeConfiguration<RestaurantPayout>
{
    public void Configure(EntityTypeBuilder<RestaurantPayout> builder)
    {
        builder.ToTable("RestaurantPayouts");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.Property(x => x.BankReference).HasMaxLength(200);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.PeriodYear, x.PeriodMonth });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaidAtUtc);
    }
}

public sealed class TransferReceiptConfiguration : IEntityTypeConfiguration<TransferReceipt>
{
    public void Configure(EntityTypeBuilder<TransferReceipt> builder)
    {
        builder.ToTable("TransferReceipts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.PayoutId);
        builder.HasIndex(x => x.UploadedAtUtc);
        builder.HasIndex(x => x.UploadedByUserId);

        builder.HasOne(x => x.Payout)
            .WithMany(x => x.TransferReceipts)
            .HasForeignKey(x => x.PayoutId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantAccountStatementConfiguration : IEntityTypeConfiguration<RestaurantAccountStatement>
{
    public void Configure(EntityTypeBuilder<RestaurantAccountStatement> builder)
    {
        builder.ToTable("RestaurantAccountStatements");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.OpeningBalance, "OpeningBalance");
        builder.OwnsRequiredMoney(x => x.ClosingBalance, "ClosingBalance");
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.PeriodYear, x.PeriodMonth });
        builder.HasIndex(x => x.GeneratedAtUtc);
    }
}
