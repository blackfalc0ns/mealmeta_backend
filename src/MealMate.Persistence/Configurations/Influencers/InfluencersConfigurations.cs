using MealMate.Domain.Influencers;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Influencers;

public sealed class InfluencerCustomerLinkConfiguration : IEntityTypeConfiguration<InfluencerCustomerLink>
{
    public void Configure(EntityTypeBuilder<InfluencerCustomerLink> builder)
    {
        builder.ToTable("InfluencerCustomerLinks");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttributionSource).HasMaxLength(200);

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => new { x.InfluencerProfileId, x.CustomerUserId });
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.LinkedAtUtc);
    }
}

public sealed class InfluencerCodeConfiguration : IEntityTypeConfiguration<InfluencerCode>
{
    public void Configure(EntityTypeBuilder<InfluencerCode> builder)
    {
        builder.ToTable("InfluencerCodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DiscountPercent).HasNullablePercentageConversion();
        builder.OwnsMoney(x => x.DiscountAmount, "DiscountAmount", required: false);
        builder.Property(x => x.CommissionRate).HasPercentageConversion();
        builder.Property(x => x.EligibleDurationsJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
    }
}

public sealed class InfluencerCommissionCalculationConfiguration : IEntityTypeConfiguration<InfluencerCommissionCalculation>
{
    public void Configure(EntityTypeBuilder<InfluencerCommissionCalculation> builder)
    {
        builder.ToTable("InfluencerCommissionCalculations");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.PaidAmountAfterDiscount, "PaidAmountAfterDiscount");
        builder.Property(x => x.CommissionRate).HasPercentageConversion();
        builder.OwnsRequiredMoney(x => x.CommissionAmount, "CommissionAmount");

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.CodeId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CalculatedAtUtc);

        builder.HasOne(x => x.Code)
            .WithMany()
            .HasForeignKey(x => x.CodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InfluencerEligibilityRuleConfiguration : IEntityTypeConfiguration<InfluencerEligibilityRule>
{
    public void Configure(EntityTypeBuilder<InfluencerEligibilityRule> builder)
    {
        builder.ToTable("InfluencerEligibilityRules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.RuleVersionId);
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class InfluencerCancellationImpactConfiguration : IEntityTypeConfiguration<InfluencerCancellationImpact>
{
    public void Configure(EntityTypeBuilder<InfluencerCancellationImpact> builder)
    {
        builder.ToTable("InfluencerCancellationImpacts");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.ReversedAmount, "ReversedAmount");

        builder.HasIndex(x => x.CommissionCalculationId);
        builder.HasIndex(x => x.CancellationId);
        builder.HasIndex(x => x.RecordedAtUtc);

        builder.HasOne(x => x.CommissionCalculation)
            .WithMany(x => x.CancellationImpacts)
            .HasForeignKey(x => x.CommissionCalculationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InfluencerWithdrawalRequestConfiguration : IEntityTypeConfiguration<InfluencerWithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<InfluencerWithdrawalRequest> builder)
    {
        builder.ToTable("InfluencerWithdrawalRequests");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);
        builder.HasIndex(x => x.ReviewedByUserId);
    }
}

public sealed class InfluencerWithdrawalApprovalConfiguration : IEntityTypeConfiguration<InfluencerWithdrawalApproval>
{
    public void Configure(EntityTypeBuilder<InfluencerWithdrawalApproval> builder)
    {
        builder.ToTable("InfluencerWithdrawalApprovals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.WithdrawalRequestId);
        builder.HasIndex(x => x.ApprovedByUserId);
        builder.HasIndex(x => x.Decision);
        builder.HasIndex(x => x.DecidedAtUtc);

        builder.HasOne(x => x.WithdrawalRequest)
            .WithMany(x => x.Approvals)
            .HasForeignKey(x => x.WithdrawalRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InfluencerPayoutHistoryConfiguration : IEntityTypeConfiguration<InfluencerPayoutHistory>
{
    public void Configure(EntityTypeBuilder<InfluencerPayoutHistory> builder)
    {
        builder.ToTable("InfluencerPayoutHistories");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.Amount, "Amount");
        builder.Property(x => x.BankReference).HasMaxLength(200);
        builder.Property(x => x.ReceiptPath).HasMaxLength(1000);

        builder.HasIndex(x => x.InfluencerProfileId);
        builder.HasIndex(x => x.WithdrawalRequestId);
        builder.HasIndex(x => x.PaidAtUtc);

        builder.HasOne(x => x.WithdrawalRequest)
            .WithMany()
            .HasForeignKey(x => x.WithdrawalRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
