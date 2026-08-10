using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.RestaurantOperations;

public sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("Menus");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.ProgramId);
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.RestaurantId, x.NameEn });
    }
}

public sealed class MealConfiguration : IEntityTypeConfiguration<Meal>
{
    public void Configure(EntityTypeBuilder<Meal> builder)
    {
        builder.ToTable("Meals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.Calories).HasPrecision(18, 3);
        builder.Property(x => x.ProteinG).HasPrecision(18, 3);
        builder.Property(x => x.CarbsG).HasPrecision(18, 3);
        builder.Property(x => x.FatG).HasPrecision(18, 3);
        builder.Property(x => x.ServingWeightG).HasPrecision(18, 3);
        builder.Property(x => x.FiberG).HasPrecision(18, 3);
        builder.Property(x => x.NetCarbsG).HasPrecision(18, 3);
        builder.Property(x => x.SugarAlcoholG).HasPrecision(18, 3);
        builder.Property(x => x.SaturatedFatG).HasPrecision(18, 3);
        builder.Property(x => x.TotalSugarsG).HasPrecision(18, 3);
        builder.Property(x => x.AddedSugarsG).HasPrecision(18, 3);
        builder.Property(x => x.SodiumMg).HasPrecision(18, 3);
        builder.Property(x => x.SugarAlcoholType).HasMaxLength(100);
        builder.Property(x => x.NutritionSource).HasMaxLength(500);
        builder.Property(x => x.AnalysisReference).HasMaxLength(500);
        builder.Property(x => x.VerifiedBy).HasMaxLength(200);
        builder.Property(x => x.RecipeVersion).HasMaxLength(100);
        builder.Property(x => x.RestaurantNotes).HasMaxLength(2000);
        builder.Property(x => x.PresentAllergenCodes).HasMaxLength(1000);
        builder.Property(x => x.MayContainAllergenCodes).HasMaxLength(1000);
        builder.Property(x => x.CrossContactNotes).HasMaxLength(2000);
        builder.Property(x => x.NutritionTagCodes).HasMaxLength(1000);
        builder.Property(x => x.SearchExclusionTagCodes).HasMaxLength(1000);
        builder.Property(x => x.ApprovalStatus).IsRequired();
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);

        builder.HasIndex(x => x.MenuId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.BundleId);
        builder.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.IsAvailableForNewOrders);
        builder.HasIndex(x => x.ApprovalStatus);

        builder.HasOne(x => x.Menu)
            .WithMany(x => x.Meals)
            .HasForeignKey(x => x.MenuId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MealAllergenConfiguration : IEntityTypeConfiguration<MealAllergen>
{
    public void Configure(EntityTypeBuilder<MealAllergen> builder)
    {
        builder.ToTable("MealAllergens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllergenCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AllergenNameAr).HasMaxLength(200);
        builder.Property(x => x.AllergenNameEn).HasMaxLength(200);

        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => new { x.MealId, x.AllergenCode }).IsUnique();
        builder.HasIndex(x => x.AllergenCode);

        builder.HasOne(x => x.Meal)
            .WithMany(x => x.Allergens)
            .HasForeignKey(x => x.MealId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantServiceRegionConfiguration : IEntityTypeConfiguration<RestaurantServiceRegion>
{
    public void Configure(EntityTypeBuilder<RestaurantServiceRegion> builder)
    {
        builder.ToTable("RestaurantServiceRegions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeliveryWindowStart).IsRequired();
        builder.Property(x => x.DeliveryWindowEnd).IsRequired();
        builder.Property(x => x.PreparationLeadTimeMinutes).IsRequired(false);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.RegionId);
        builder.HasIndex(x => new { x.RestaurantId, x.RegionId }).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class DailyCapacityConfiguration : IEntityTypeConfiguration<DailyCapacity>
{
    public void Configure(EntityTypeBuilder<DailyCapacity> builder)
    {
        builder.ToTable("DailyCapacities");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.ServiceDate, x.CapacityType, x.CampaignId }).IsUnique();
        builder.HasIndex(x => x.ServiceDate);
        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.CapacityType);
    }
}

public sealed class BusyStatusConfiguration : IEntityTypeConfiguration<BusyStatus>
{
    public void Configure(EntityTypeBuilder<BusyStatus> builder)
    {
        builder.ToTable("BusyStatuses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.IsBusy, x.StartsAtUtc });
        builder.HasIndex(x => x.EndsAtUtc);
    }
}

public sealed class RestaurantOperatingScheduleConfiguration : IEntityTypeConfiguration<RestaurantOperatingSchedule>
{
    public void Configure(EntityTypeBuilder<RestaurantOperatingSchedule> builder)
    {
        builder.ToTable("RestaurantOperatingSchedules");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.DayOfWeek }).IsUnique();
    }
}

public sealed class RestaurantOperatingDayExceptionConfiguration : IEntityTypeConfiguration<RestaurantOperatingDayException>
{
    public void Configure(EntityTypeBuilder<RestaurantOperatingDayException> builder)
    {
        builder.ToTable("RestaurantOperatingDayExceptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.Date }).IsUnique();
    }
}

public sealed class RestaurantDailyOrderConfiguration : IEntityTypeConfiguration<RestaurantDailyOrder>
{
    public void Configure(EntityTypeBuilder<RestaurantDailyOrder> builder)
    {
        builder.ToTable("RestaurantDailyOrders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RejectionReason).HasMaxLength(2000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.RestaurantId, x.ServiceDate });
        builder.HasIndex(x => new { x.RestaurantId, x.SubscriptionDayId }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ServiceDate);
    }
}

public sealed class RestaurantConfirmationConfiguration : IEntityTypeConfiguration<RestaurantConfirmation>
{
    public void Configure(EntityTypeBuilder<RestaurantConfirmation> builder)
    {
        builder.ToTable("RestaurantConfirmations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.DailyOrderId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ConfirmedByUserId);
        builder.HasIndex(x => x.RespondedAtUtc);

        builder.HasOne(x => x.DailyOrder)
            .WithMany(x => x.Confirmations)
            .HasForeignKey(x => x.DailyOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MealInvoiceConfiguration : IEntityTypeConfiguration<MealInvoice>
{
    public void Configure(EntityTypeBuilder<MealInvoice> builder)
    {
        builder.ToTable("MealInvoices");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => new { x.RestaurantId, x.ServiceDate });
        builder.HasIndex(x => x.ServiceDate);
    }
}

public sealed class MealBarcodeConfiguration : IEntityTypeConfiguration<MealBarcode>
{
    public void Configure(EntityTypeBuilder<MealBarcode> builder)
    {
        builder.ToTable("MealBarcodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BarcodeValue).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.BarcodeValue).IsUnique();
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.GeneratedAtUtc);
    }
}

public sealed class MealLabelConfiguration : IEntityTypeConfiguration<MealLabel>
{
    public void Configure(EntityTypeBuilder<MealLabel> builder)
    {
        builder.ToTable("MealLabels");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LabelPayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.GeneratedAtUtc);
    }
}

public sealed class OrderHandoverConfiguration : IEntityTypeConfiguration<OrderHandover>
{
    public void Configure(EntityTypeBuilder<OrderHandover> builder)
    {
        builder.ToTable("OrderHandovers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.DailyOrderId);
        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.HandedOverAtUtc);

        builder.HasOne(x => x.DailyOrder)
            .WithMany(x => x.Handovers)
            .HasForeignKey(x => x.DailyOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantDriverLinkConfiguration : IEntityTypeConfiguration<RestaurantDriverLink>
{
    public void Configure(EntityTypeBuilder<RestaurantDriverLink> builder)
    {
        builder.ToTable("RestaurantDriverLinks");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => new { x.RestaurantId, x.DriverProfileId }).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class RestaurantRatingConfiguration : IEntityTypeConfiguration<RestaurantRating>
{
    public void Configure(EntityTypeBuilder<RestaurantRating> builder)
    {
        builder.ToTable("RestaurantRatings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.ToTable(t => t.HasCheckConstraint("CK_RestaurantRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5"));

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.RestaurantId, x.CustomerUserId, x.SubscriptionDayId });
        builder.HasIndex(x => x.Score);
    }
}

public sealed class MealRatingConfiguration : IEntityTypeConfiguration<MealRating>
{
    public void Configure(EntityTypeBuilder<MealRating> builder)
    {
        builder.ToTable("MealRatings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.ToTable(t => t.HasCheckConstraint("CK_MealRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5"));

        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.MealId, x.CustomerUserId, x.SubscriptionDayId });
        builder.HasIndex(x => x.Score);

        builder.HasOne(x => x.Meal)
            .WithMany(x => x.Ratings)
            .HasForeignKey(x => x.MealId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantContractTerminationConfiguration : IEntityTypeConfiguration<RestaurantContractTermination>
{
    public void Configure(EntityTypeBuilder<RestaurantContractTermination> builder)
    {
        builder.ToTable("RestaurantContractTerminations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(2000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.ProgramId);
        builder.HasIndex(x => x.BundleId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAtUtc);
    }
}
