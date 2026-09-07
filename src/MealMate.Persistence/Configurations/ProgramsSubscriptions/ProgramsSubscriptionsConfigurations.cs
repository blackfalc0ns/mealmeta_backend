using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class ProgramConfiguration : IEntityTypeConfiguration<Program>
{
    public void Configure(EntityTypeBuilder<Program> builder)
    {
        builder.ToTable("Programs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ShortCode).HasMaxLength(10);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.KeywordsAr).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.KeywordsEn).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.StatusReason).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.ShortCode);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.AvailableForFamily);
        builder.HasIndex(x => x.SortOrder);
    }
}

public sealed class BundleConfiguration : IEntityTypeConfiguration<Bundle>
{
    public void Configure(EntityTypeBuilder<Bundle> builder)
    {
        builder.ToTable("Bundles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.ImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.StatusReason).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.ProgramId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.AvailableForFamily);

        builder.HasOne(x => x.Program)
            .WithMany(x => x.Bundles)
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantBundlePricingConfiguration : IEntityTypeConfiguration<RestaurantBundlePricing>
{
    public void Configure(EntityTypeBuilder<RestaurantBundlePricing> builder)
    {
        builder.ToTable("RestaurantBundlePricings");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.ReferencePrice26, "ReferencePrice26");
        builder.OwnsRequiredMoney(x => x.ContractedSupplyPrice26, "ContractedSupplyPrice26");
        builder.Property(x => x.ChangeRequestReason).HasMaxLength(2000);

        builder.HasIndex(x => new { x.RestaurantId, x.ProgramId, x.BundleId, x.EffectiveFromUtc });
        builder.HasIndex(x => x.LockStatus);
        builder.HasIndex(x => x.ProgramId);
        builder.HasIndex(x => x.BundleId);

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Bundle)
            .WithMany()
            .HasForeignKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PreviousPricing)
            .WithMany()
            .HasForeignKey(x => x.PreviousPricingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantProgramBundleOfferConfiguration : IEntityTypeConfiguration<RestaurantProgramBundleOffer>
{
    public void Configure(EntityTypeBuilder<RestaurantProgramBundleOffer> builder)
    {
        builder.ToTable("RestaurantProgramBundleOffers");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RestaurantId, x.ProgramId, x.BundleId }).IsUnique();
        builder.HasIndex(x => new { x.RestaurantId, x.IsActive });
        builder.HasOne<Program>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bundle>().WithMany().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClassificationRunConfiguration : IEntityTypeConfiguration<ClassificationRun>
{
    public void Configure(EntityTypeBuilder<ClassificationRun> builder)
    {
        builder.ToTable("ClassificationRuns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MeanReferenceDaily).HasPrecision(18, 3);
        builder.Property(x => x.SdReferenceDaily).HasPrecision(18, 3);
        builder.Property(x => x.LowerBound).HasPrecision(18, 3);
        builder.Property(x => x.UpperBound).HasPrecision(18, 3);
        builder.Property(x => x.ConfidenceLevel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => new { x.ProgramId, x.BundleId, x.RunAtUtc });
        builder.HasIndex(x => x.RuleVersionId);

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Bundle)
            .WithMany()
            .HasForeignKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantClassificationConfiguration : IEntityTypeConfiguration<RestaurantClassification>
{
    public void Configure(EntityTypeBuilder<RestaurantClassification> builder)
    {
        builder.ToTable("RestaurantClassifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceDailyBox).HasPrecision(18, 3);
        builder.Property(x => x.OutlierReason).HasMaxLength(1000);

        builder.HasIndex(x => new { x.ClassificationRunId, x.RestaurantId }).IsUnique();
        builder.HasIndex(x => new { x.ProgramId, x.BundleId, x.Tier });
        builder.HasIndex(x => x.RestaurantId);

        builder.HasOne(x => x.ClassificationRun)
            .WithMany(x => x.Classifications)
            .HasForeignKey(x => x.ClassificationRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Bundle)
            .WithMany()
            .HasForeignKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PricingRuleVersionConfiguration : IEntityTypeConfiguration<PricingRuleVersion>
{
    public void Configure(EntityTypeBuilder<PricingRuleVersion> builder)
    {
        builder.ToTable("PricingRuleVersions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RMax).HasPercentageConversion();
        builder.Property(x => x.RMin).HasPercentageConversion();
        builder.Property(x => x.Gamma).HasPrecision(18, 6);
        builder.Property(x => x.RoundingStepKwd).HasPrecision(18, 3);

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.RuleVersionId);

        builder.HasOne(x => x.RuleVersion)
            .WithMany()
            .HasForeignKey(x => x.RuleVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeliveryAddress).HasMaxLength(500);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.PaymentId).HasMaxLength(100);
        builder.Property(x => x.PaymentInvoiceId).HasMaxLength(100);
        builder.Property(x => x.LastFulfillmentErrorCode).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.OwnsRequiredMoney(x => x.PaidAmount, "PaidAmount");

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.ProgramId, x.BundleId, x.SelectedTier });
        builder.HasIndex(x => x.StartDate);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.PaymentId).IsUnique();
        builder.HasIndex(x => x.PaymentInvoiceId);
        builder.HasIndex(x => x.PricingRuleVersionId);
        builder.HasIndex(x => x.PackageContentVersionId);
        builder.HasIndex(x => x.PromoCodeId);
        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.FamilyGroupId);
        builder.HasIndex(x => x.FamilyMemberId);

        builder.HasOne(x => x.Program)
            .WithMany()
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Bundle)
            .WithMany()
            .HasForeignKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PackageContentVersion)
            .WithMany()
            .HasForeignKey(x => x.PackageContentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PricingRuleVersion)
            .WithMany()
            .HasForeignKey(x => x.PricingRuleVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PromoCode)
            .WithMany()
            .HasForeignKey(x => x.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FamilyGroup>()
            .WithMany()
            .HasForeignKey(x => x.FamilyGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(x => x.FamilyMemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FamilyGroupConfiguration : IEntityTypeConfiguration<FamilyGroup>
{
    public void Configure(EntityTypeBuilder<FamilyGroup> builder)
    {
        builder.ToTable("FamilyGroups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.DiscountPercentSnapshot).HasPrecision(5, 4);
        builder.Property(x => x.ConvertedFromSubscriptionId);
        builder.HasIndex(x => x.ManagerCustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.SelectedFamilyPackageId);
        builder.HasIndex(x => x.ConvertedFromSubscriptionId);
        builder.HasMany(x => x.Members).WithOne(x => x.FamilyGroup).HasForeignKey(x => x.FamilyGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("FamilyMembers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RelationshipCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NutritionalGoalCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.Occupation).HasMaxLength(80);
        builder.HasIndex(x => x.FamilyGroupId);
        builder.HasIndex(x => x.MemberSubscriptionId);
        builder.HasIndex(x => x.PhoneNumber);
        builder.HasOne(x => x.MemberSubscription).WithMany().HasForeignKey(x => x.MemberSubscriptionId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FamilyMemberAccessConfiguration : IEntityTypeConfiguration<FamilyMemberAccess>
{
    public void Configure(EntityTypeBuilder<FamilyMemberAccess> builder)
    {
        builder.ToTable("FamilyMemberAccesses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InviteTokenHash).HasMaxLength(128);
        builder.HasIndex(x => x.FamilyMemberId).IsUnique();
        builder.HasIndex(x => x.LinkedCustomerUserId);
        builder.HasIndex(x => x.InviteTokenHash);
        builder.HasOne(x => x.FamilyMember)
            .WithOne()
            .HasForeignKey<FamilyMemberAccess>(x => x.FamilyMemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FamilyMemberPermissionsConfiguration : IEntityTypeConfiguration<FamilyMemberPermissions>
{
    public void Configure(EntityTypeBuilder<FamilyMemberPermissions> builder)
    {
        builder.ToTable("FamilyMemberPermissions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.FamilyMemberId).IsUnique();
        builder.HasOne(x => x.FamilyMember)
            .WithOne()
            .HasForeignKey<FamilyMemberPermissions>(x => x.FamilyMemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SubscriptionSnapshotConfiguration : IEntityTypeConfiguration<SubscriptionSnapshot>
{
    public void Configure(EntityTypeBuilder<SubscriptionSnapshot> builder)
    {
        builder.ToTable("SubscriptionSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.A_t).HasPrecision(18, 6);
        builder.Property(x => x.ROfD).HasPercentageConversion();
        builder.Property(x => x.ReferencePricesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SupplyPricesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.RuleVersionsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.CapturedAtUtc);

        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.Snapshots)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FamilySubscriptionConfiguration : IEntityTypeConfiguration<FamilySubscription>
{
    public void Configure(EntityTypeBuilder<FamilySubscription> builder)
    {
        builder.ToTable("FamilySubscriptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.Relationship).HasMaxLength(100);

        builder.HasIndex(x => new { x.PrimarySubscriptionId, x.MemberCustomerUserId }).IsUnique();
        builder.HasIndex(x => x.MemberCustomerUserId);
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.PrimarySubscription)
            .WithMany()
            .HasForeignKey(x => x.PrimarySubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("PromoCodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DiscountPercent).HasNullablePercentageConversion();
        builder.OwnsMoney(x => x.DiscountAmount, "DiscountAmount", required: false);
        builder.Property(x => x.EligibleDurationDaysJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
    }
}

public sealed class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> builder)
    {
        builder.ToTable("ReferralCodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RewardPercent).HasNullablePercentageConversion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class SubscriptionTierUpgradeConfiguration : IEntityTypeConfiguration<SubscriptionTierUpgrade>
{
    public void Configure(EntityTypeBuilder<SubscriptionTierUpgrade> builder)
    {
        builder.ToTable("SubscriptionTierUpgrades");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.PriceDifference, "PriceDifference");
        builder.OwnsRequiredMoney(x => x.QuotedTotalDue, "QuotedTotalDue");
        builder.Property(x => x.PaymentId).HasMaxLength(100);
        builder.Property(x => x.QuoteJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionRenewalConfiguration : IEntityTypeConfiguration<SubscriptionRenewal>
{
    public void Configure(EntityTypeBuilder<SubscriptionRenewal> builder)
    {
        builder.ToTable("SubscriptionRenewals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.PreviousSubscriptionId);
        builder.HasIndex(x => x.NewSubscriptionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);

        builder.HasOne(x => x.PreviousSubscription)
            .WithMany()
            .HasForeignKey(x => x.PreviousSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.NewSubscription)
            .WithMany()
            .HasForeignKey(x => x.NewSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PackageContentVersionConfiguration : IEntityTypeConfiguration<PackageContentVersion>
{
    public void Configure(EntityTypeBuilder<PackageContentVersion> builder)
    {
        builder.ToTable("PackageContentVersions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContentSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChangeSummary).HasMaxLength(1000);

        builder.HasIndex(x => new { x.ProgramId, x.VersionNumber });
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.Program)
            .WithMany(x => x.ContentVersions)
            .HasForeignKey(x => x.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Bundle)
            .WithMany(x => x.ContentVersions)
            .HasForeignKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionOperationConfiguration : IEntityTypeConfiguration<SubscriptionOperation>
{
    public void Configure(EntityTypeBuilder<SubscriptionOperation> builder)
    {
        builder.ToTable("SubscriptionOperations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OperationType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.TimestampUtc);
        builder.HasIndex(x => x.CorrelationId);

        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.Operations)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DiscountsAndReferralLogConfiguration : IEntityTypeConfiguration<DiscountsAndReferralLog>
{
    public void Configure(EntityTypeBuilder<DiscountsAndReferralLog> builder)
    {
        builder.ToTable("DiscountsAndReferralLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DiscountType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Code);
        builder.HasIndex(x => x.AppliedAtUtc);

        builder.HasOne(x => x.Subscription)
            .WithMany(x => x.DiscountsAndReferralLogs)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionFreezeRequestConfiguration : IEntityTypeConfiguration<SubscriptionFreezeRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionFreezeRequest> builder)
    {
        builder.ToTable("SubscriptionFreezeRequests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

