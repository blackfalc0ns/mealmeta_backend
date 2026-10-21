using MealMate.Domain.ProgramsSubscriptions.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class PricingQuoteConfiguration : IEntityTypeConfiguration<PricingQuote>
{
    public void Configure(EntityTypeBuilder<PricingQuote> builder)
    {
        builder.ToTable("PricingQuotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SelectionSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FinancialSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SelectionHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RuleVersionContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceVersionsHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceVersionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FinancialSnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RiskBlockerCodes).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ReferencePrice26Kwd).HasPrecision(38, 18);
        builder.Property(x => x.CohortMeanReferencePrice26Kwd).HasPrecision(38, 18);
        builder.Property(x => x.ContractedSupplyPrice26Kwd).HasPrecision(38, 18);
        builder.Property(x => x.CustomerListPriceKwd).HasPrecision(18, 3);
        builder.Property(x => x.SelectedDiscountKwd).HasPrecision(18, 3);
        builder.Property(x => x.CustomerNetPriceKwd).HasPrecision(18, 3);
        builder.Property(x => x.ExternalCaptureAmountKwd).HasPrecision(18, 3);
        builder.Property(x => x.ExpectedGatewayCostKwd).HasPrecision(38, 18);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Route, x.Status, x.ExpiresAtUtc });
        builder.HasIndex(x => new { x.CustomerId, x.FinancialSnapshotHash });
        builder.HasOne<MealMate.Domain.Foundation.RuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MealMate.Domain.ProgramsSubscriptions.ClassificationRun>().WithMany().HasForeignKey(x => x.ClassificationRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PricingQuoteConsumptionConfiguration : IEntityTypeConfiguration<PricingQuoteConsumption>
{
    public void Configure(EntityTypeBuilder<PricingQuoteConsumption> builder)
    {
        builder.ToTable("PricingQuoteConsumptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConsumerType).HasMaxLength(32);
        builder.Property(x => x.AcceptedSnapshotHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.QuoteId).IsUnique();
        builder.HasOne(x => x.Quote).WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcquisitionBudgetConfiguration : IEntityTypeConfiguration<AcquisitionBudget>
{
    public void Configure(EntityTypeBuilder<AcquisitionBudget> builder)
    {
        builder.ToTable("PricingAcquisitionBudgets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorizedLimitKwd).HasPrecision(18, 3);
        builder.Property(x => x.ApprovalReason).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CampaignId, x.LegalEntityId, x.IsActive });
        builder.HasOne<MealMate.Domain.Foundation.RuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcquisitionBudgetReservationConfiguration : IEntityTypeConfiguration<AcquisitionBudgetReservation>
{
    public void Configure(EntityTypeBuilder<AcquisitionBudgetReservation> builder)
    {
        builder.ToTable("PricingAcquisitionBudgetReservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AmountKwd).HasPrecision(18, 3);
        builder.Property(x => x.QuoteSnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.QuoteId).IsUnique();
        builder.HasIndex(x => new { x.BudgetId, x.Status, x.ExpiresAtUtc });
        builder.HasOne(x => x.Budget).WithMany().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PricingQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PricingRiskExceptionConfiguration : IEntityTypeConfiguration<PricingRiskException>
{
    public void Configure(EntityTypeBuilder<PricingRiskException> builder)
    {
        builder.ToTable("PricingRiskExceptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorizedLossKwd).HasPrecision(18, 3);
        builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.QuoteSnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasOne<PricingQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcquisitionBudgetReservation>().WithMany().HasForeignKey(x => x.BudgetReservationId).OnDelete(DeleteBehavior.Restrict);
    }
}
