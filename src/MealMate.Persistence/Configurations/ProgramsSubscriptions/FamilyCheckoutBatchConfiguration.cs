using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class FamilyCheckoutBatchConfiguration : IEntityTypeConfiguration<FamilyCheckoutBatch>
{
    public void Configure(EntityTypeBuilder<FamilyCheckoutBatch> builder)
    {
        builder.ToTable("FamilyCheckoutBatches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(20).HasDefaultValue("Initial").IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.FamilyDiscountPercent).HasPrecision(5, 4);
        builder.Property(x => x.Subtotal).HasPrecision(18, 3);
        builder.Property(x => x.FamilyDiscountAmount).HasPrecision(18, 3);
        builder.Property(x => x.FinalPrice).HasPrecision(18, 3);
        builder.Property(x => x.PackageNameAr).HasMaxLength(120);
        builder.Property(x => x.PackageNameEn).HasMaxLength(120);
        builder.Property(x => x.GatewayInvoiceId).HasMaxLength(128);
        builder.Property(x => x.GatewayPaymentId).HasMaxLength(128);
        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => x.FamilyGroupId);
        builder.HasIndex(x => x.PayerCustomerUserId);
        builder.HasIndex(x => x.Status);
        // One gateway payment can never be referenced by two batches.
        builder.HasIndex(x => x.GatewayPaymentId).IsUnique();

        // Initial checkout drafts remain unique per family. Renewal batches have a
        // separate immutable request identity and do not replace the initial draft.
        builder.HasIndex(x => x.FamilyGroupId, "IX_FamilyCheckoutBatches_FamilyGroupId")
            .IsUnique()
            .HasFilter($"\"{nameof(FamilyCheckoutBatch.Status)}\" IN ({(int)FamilyCheckoutBatchStatus.Draft}, {(int)FamilyCheckoutBatchStatus.PaymentPending}) AND \"{nameof(FamilyCheckoutBatch.Purpose)}\" = 'Initial'");
        builder.HasIndex(x => x.FamilyGroupId, "IX_FamilyCheckoutBatches_FamilyGroupId_PaymentPending").IsUnique()
            .HasFilter($"\"{nameof(FamilyCheckoutBatch.Status)}\" = {(int)FamilyCheckoutBatchStatus.PaymentPending}");
        builder.HasIndex(x => x.RenewalRequestId).IsUnique().HasFilter("\"RenewalRequestId\" IS NOT NULL");
        // Retry idempotency: same key inside one group resolves to the same batch.
        builder.HasIndex(x => new { x.FamilyGroupId, x.IdempotencyKey }).IsUnique();

        builder.HasOne(x => x.FamilyGroup)
            .WithMany()
            .HasForeignKey(x => x.FamilyGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Batch)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.PaymentAttempts)
            .WithOne(x => x.Batch)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FamilyCheckoutBatchLineConfiguration : IEntityTypeConfiguration<FamilyCheckoutBatchLine>
{
    public void Configure(EntityTypeBuilder<FamilyCheckoutBatchLine> builder)
    {
        builder.ToTable("FamilyCheckoutBatchLines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PriceBeforeFamilyDiscount).HasPrecision(18, 3);
        builder.Property(x => x.FamilyDiscountAllocated).HasPrecision(18, 3);
        builder.Property(x => x.PromoCodeDiscountAllocated).HasPrecision(18, 3);
        builder.Property(x => x.NetPaidAmount).HasPrecision(18, 3);
        builder.Property(x => x.AddressLine).HasMaxLength(300);
        builder.Property(x => x.AddressLabel).HasMaxLength(120);
        builder.Property(x => x.Governorate).HasMaxLength(80);
        builder.Property(x => x.Area).HasMaxLength(80);
        builder.Property(x => x.Block).HasMaxLength(80);
        builder.Property(x => x.Street).HasMaxLength(120);
        builder.Property(x => x.Building).HasMaxLength(120);
        builder.Property(x => x.Unit).HasMaxLength(80);
        builder.Property(x => x.Floor).HasMaxLength(80);
        builder.Property(x => x.MenuPlanVersionsJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.FamilyMemberId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.RenewalRequestId);
        builder.HasIndex(x => x.PreviousSubscriptionId);

        // Per-line allocation integrity (currency-unit safe sums are verified in code).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_FamilyCheckoutBatchLines_NetPaid",
            $"\"{nameof(FamilyCheckoutBatchLine.NetPaidAmount)}\" >= 0 AND "
            + $"\"{nameof(FamilyCheckoutBatchLine.FamilyDiscountAllocated)}\" >= 0 AND "
            + $"\"{nameof(FamilyCheckoutBatchLine.PromoCodeDiscountAllocated)}\" >= 0 AND "
            + $"\"{nameof(FamilyCheckoutBatchLine.PriceBeforeFamilyDiscount)}\" >= 0 AND "
            + $"\"{nameof(FamilyCheckoutBatchLine.NetPaidAmount)}\" = \"{nameof(FamilyCheckoutBatchLine.PriceBeforeFamilyDiscount)}\" - \"{nameof(FamilyCheckoutBatchLine.FamilyDiscountAllocated)}\" - \"{nameof(FamilyCheckoutBatchLine.PromoCodeDiscountAllocated)}\""));

        // Financial history survives member/subscription/batch removals.
        builder.HasOne(x => x.FamilyMember)
            .WithMany()
            .HasForeignKey(x => x.FamilyMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FamilyCheckoutPaymentAttemptConfiguration : IEntityTypeConfiguration<FamilyCheckoutPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<FamilyCheckoutPaymentAttempt> builder)
    {
        builder.ToTable("FamilyCheckoutPaymentAttempts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttemptKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.GatewayPaymentUrl).HasMaxLength(1024);
        builder.Property(x => x.GatewayInvoiceId).HasMaxLength(128);
        builder.Property(x => x.GatewayPaymentId).HasMaxLength(128);
        builder.Property(x => x.ResultCode).HasMaxLength(128);

        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => new { x.BatchId, x.AttemptKey }).IsUnique();

        // One gateway payment can ever belong to one batch/attempt.
        builder.HasIndex(x => x.GatewayPaymentId).IsUnique();

        builder.HasOne(x => x.Batch)
            .WithMany(x => x.PaymentAttempts)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
