using MealMate.Domain.CentralAccounting;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CentralAccounting;

public sealed class ChartOfAccountConfiguration : IEntityTypeConfiguration<ChartOfAccount>
{
    public void Configure(EntityTypeBuilder<ChartOfAccount> builder)
    {
        builder.ToTable("ChartOfAccounts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.AccountType);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FinancialSettingConfiguration : IEntityTypeConfiguration<FinancialSetting>
{
    public void Configure(EntityTypeBuilder<FinancialSetting> builder)
    {
        builder.ToTable("FinancialSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SettingKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ValueJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.SettingKey);
        builder.HasIndex(x => x.RuleVersionId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.SettingKey, x.IsActive });
    }
}

public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntryNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.SourceReference).HasMaxLength(200);

        builder.HasIndex(x => x.EntryNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PostedAtUtc);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.SourceReference);
    }
}

public sealed class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        builder.ToTable("JournalEntryLines");
        builder.HasKey(x => x.Id);

        builder.OwnsMoney(x => x.Debit, "Debit", required: false);
        builder.OwnsMoney(x => x.Credit, "Credit", required: false);
        builder.Property(x => x.Memo).HasMaxLength(1000);

        builder.HasIndex(x => x.JournalEntryId);
        builder.HasIndex(x => x.AccountId);

        builder.HasOne(x => x.JournalEntry)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Account)
            .WithMany(x => x.JournalEntryLines)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeferredRevenueConfiguration : IEntityTypeConfiguration<DeferredRevenue>
{
    public void Configure(EntityTypeBuilder<DeferredRevenue> builder)
    {
        builder.ToTable("DeferredRevenues");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.OriginalAmount, "OriginalAmount");
        builder.OwnsRequiredMoney(x => x.RemainingAmount, "RemainingAmount");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);
    }
}

public sealed class DailyRevenueRecognitionConfiguration : IEntityTypeConfiguration<DailyRevenueRecognition>
{
    public void Configure(EntityTypeBuilder<DailyRevenueRecognition> builder)
    {
        builder.ToTable("DailyRevenueRecognitions");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.RecognizedAtUtc);
        builder.HasIndex(x => x.JournalEntryId);
        builder.HasIndex(x => new { x.SubscriptionDayId, x.SubscriptionId });

        builder.HasOne(x => x.JournalEntry)
            .WithMany()
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantPayableLedgerConfiguration : IEntityTypeConfiguration<RestaurantPayableLedger>
{
    public void Configure(EntityTypeBuilder<RestaurantPayableLedger> builder)
    {
        builder.ToTable("RestaurantPayableLedgers");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.GrossAmount, "GrossAmount");
        builder.OwnsRequiredMoney(x => x.CommissionAmount, "CommissionAmount");
        builder.OwnsRequiredMoney(x => x.NetAmount, "NetAmount");
        builder.OwnsMoney(x => x.CampaignContribution, "CampaignContribution", required: false);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.RecognizedAtUtc);
        builder.HasIndex(x => new { x.RestaurantId, x.RecognizedAtUtc });
    }
}

public sealed class InfluencerPayableLedgerConfiguration : IEntityTypeConfiguration<InfluencerPayableLedger>
{
    public void Configure(EntityTypeBuilder<InfluencerPayableLedger> builder)
    {
        builder.ToTable("InfluencerPayableLedgers");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.CommissionAmount, "CommissionAmount");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RecognizedAtUtc);
    }
}

public sealed class PaymentGatewayConfigConfiguration : IEntityTypeConfiguration<PaymentGatewayConfig>
{
    public void Configure(EntityTypeBuilder<PaymentGatewayConfig> builder)
    {
        builder.ToTable("PaymentGatewayConfigs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RelativeFee).HasNullablePercentageConversion();
        builder.OwnsMoney(x => x.FixedFee, "FixedFee", required: false);
        builder.OwnsMoney(x => x.MinFee, "MinFee", required: false);
        builder.OwnsMoney(x => x.MaxFee, "MaxFee", required: false);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.Weight);
        builder.HasIndex(x => x.RuleVersionId);
    }
}

public sealed class TaxRecordConfiguration : IEntityTypeConfiguration<TaxRecord>
{
    public void Configure(EntityTypeBuilder<TaxRecord> builder)
    {
        builder.ToTable("TaxRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Rate).HasPercentageConversion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class OperatingExpenseConfiguration : IEntityTypeConfiguration<OperatingExpense>
{
    public void Configure(EntityTypeBuilder<OperatingExpense> builder)
    {
        builder.ToTable("OperatingExpenses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.IncurredOn);
        builder.HasIndex(x => x.Status);
    }
}

public sealed class FinancialSettlementConfiguration : IEntityTypeConfiguration<FinancialSettlement>
{
    public void Configure(EntityTypeBuilder<FinancialSettlement> builder)
    {
        builder.ToTable("FinancialSettlements");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.GrossTotal, "GrossTotal");
        builder.OwnsRequiredMoney(x => x.NetTotal, "NetTotal");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.PeriodYear, x.PeriodMonth });
        builder.HasIndex(x => new { x.RestaurantId, x.PeriodYear, x.PeriodMonth });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.SettledAtUtc);
    }
}

public sealed class FinancialAuditTrailConfiguration : IEntityTypeConfiguration<FinancialAuditTrail>
{
    public void Configure(EntityTypeBuilder<FinancialAuditTrail> builder)
    {
        builder.ToTable("FinancialAuditTrails");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb");
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => x.Action);
        builder.HasIndex(x => x.EntityType);
        builder.HasIndex(x => x.EntityId);
        builder.HasIndex(x => x.ActorUserId);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.CorrelationId);
    }
}
