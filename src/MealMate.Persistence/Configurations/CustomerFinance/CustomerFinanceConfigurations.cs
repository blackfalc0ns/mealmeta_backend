using MealMate.Domain.CustomerFinance;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CustomerFinance;

public sealed class CustomerCashWalletConfiguration : IEntityTypeConfiguration<CustomerCashWallet>
{
    public void Configure(EntityTypeBuilder<CustomerCashWallet> builder)
    {
        builder.ToTable("CustomerCashWallets");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Balance, "Balance");

        builder.HasIndex(x => x.CustomerUserId).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class CustomerPromotionalWalletConfiguration : IEntityTypeConfiguration<CustomerPromotionalWallet>
{
    public void Configure(EntityTypeBuilder<CustomerPromotionalWallet> builder)
    {
        builder.ToTable("CustomerPromotionalWallets");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Balance, "Balance");

        builder.HasIndex(x => x.CustomerUserId).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.OwnsRequiredMoney(x => x.BalanceAfter, "BalanceAfter");
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(2000);

        builder.HasIndex(x => x.WalletId);
        builder.HasIndex(x => x.WalletType);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId, x.CustomerUserId })
            .IsUnique().HasFilter("\"ReferenceType\" = 'FamilyCancellationRefund'");
        builder.HasIndex(x => new { x.WalletId, x.WalletType, x.OccurredAtUtc });
    }
}

public sealed class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("SubscriptionPayments");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.Property(x => x.GatewayCode).HasMaxLength(50);
        builder.Property(x => x.GatewayTransactionId).HasMaxLength(200);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.GatewayCode);
        builder.HasIndex(x => x.GatewayTransactionId);
        builder.HasIndex(x => x.PaidAtUtc);
    }
}

public sealed class SubscriptionCancellationConfiguration : IEntityTypeConfiguration<SubscriptionCancellation>
{
    public void Configure(EntityTypeBuilder<SubscriptionCancellation> builder)
    {
        builder.ToTable("SubscriptionCancellations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SettlementKind).HasMaxLength(50);
        builder.Property(x => x.ContributionRefundsJson).HasColumnType("jsonb");
        builder.Property(x => x.ResultMetadataJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.SubscriptionId, x.SettlementKind }).IsUnique()
            .HasFilter("\"SettlementKind\" = 'FamilyCancellation'");

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);
    }
}

public sealed class RefundCalculationConfiguration : IEntityTypeConfiguration<RefundCalculation>
{
    public void Configure(EntityTypeBuilder<RefundCalculation> builder)
    {
        builder.ToTable("RefundCalculations");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.RefundEligiblePaid, "RefundEligiblePaid");
        builder.OwnsRequiredMoney(x => x.UnusedCashValue, "UnusedCashValue");
        builder.OwnsRequiredMoney(x => x.CancellationFee, "CancellationFee");
        builder.OwnsRequiredMoney(x => x.FinalCashRefund, "FinalCashRefund");
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.CancellationId);
        builder.HasIndex(x => x.CalculatedAtUtc);

        builder.HasOne(x => x.Cancellation)
            .WithMany(x => x.RefundCalculations)
            .HasForeignKey(x => x.CancellationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CancellationFeeRecordConfiguration : IEntityTypeConfiguration<CancellationFeeRecord>
{
    public void Configure(EntityTypeBuilder<CancellationFeeRecord> builder)
    {
        builder.ToTable("CancellationFeeRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FeeRate).HasPercentageConversion();
        builder.OwnsRequiredMoney(x => x.FeeAmount, "FeeAmount");

        builder.HasIndex(x => x.CancellationId);
        builder.HasIndex(x => x.RuleVersionId);

        builder.HasOne(x => x.Cancellation)
            .WithMany(x => x.FeeRecords)
            .HasForeignKey(x => x.CancellationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerWithdrawalRequestConfiguration : IEntityTypeConfiguration<CustomerWithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<CustomerWithdrawalRequest> builder)
    {
        builder.ToTable("CustomerWithdrawalRequests");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);
        builder.HasIndex(x => x.ReviewedByUserId);
    }
}

public sealed class CustomerBankDetailsConfiguration : IEntityTypeConfiguration<CustomerBankDetails>
{
    public void Configure(EntityTypeBuilder<CustomerBankDetails> builder)
    {
        builder.ToTable("CustomerBankDetails");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Iban).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BankName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AccountHolderName).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Iban);
        builder.HasIndex(x => x.IsVerified);
    }
}
