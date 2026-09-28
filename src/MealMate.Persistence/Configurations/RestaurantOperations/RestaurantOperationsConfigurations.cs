using MealMate.Domain.Enums;
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

public sealed class MenuPlanConfiguration : IEntityTypeConfiguration<MenuPlan>
{
    public void Configure(EntityTypeBuilder<MenuPlan> builder)
    {
        builder.ToTable("MenuPlans");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Mode).IsRequired();
        builder.HasIndex(x => new { x.RestaurantId, x.IsTemplate });
    }
}

public sealed class MenuPlanVersionConfiguration : IEntityTypeConfiguration<MenuPlanVersion>
{
    public void Configure(EntityTypeBuilder<MenuPlanVersion> builder)
    {
        builder.ToTable("MenuPlanVersions");
        builder.Property(x => x.ReviewNotes).HasMaxLength(2000);
        builder.Property(x => x.PendingSequenceJson);
        builder.HasIndex(x => new { x.MenuPlanId, x.VersionNumber }).IsUnique();
        builder.HasOne(x => x.MenuPlan).WithMany(x => x.Versions).HasForeignKey(x => x.MenuPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MenuDayConfiguration : IEntityTypeConfiguration<MenuDay>
{
    public void Configure(EntityTypeBuilder<MenuDay> builder)
    {
        builder.ToTable("MenuDays");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActiveInCycle).IsRequired().HasDefaultValue(true);
        builder.HasIndex(x => new { x.MenuPlanVersionId, x.DayNumber }).IsUnique();
        builder.HasOne(x => x.MenuPlanVersion).WithMany(x => x.Days).HasForeignKey(x => x.MenuPlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MenuDayMealConfiguration : IEntityTypeConfiguration<MenuDayMeal>
{
    public void Configure(EntityTypeBuilder<MenuDayMeal> builder)
    {
        builder.ToTable("MenuDayMeals");
        builder.Property(x => x.Slot).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.MenuDayId, x.ProgramId, x.MealId }).IsUnique();
        builder.HasIndex(x => new { x.MenuDayId, x.ProgramId, x.Slot });
        builder.HasIndex(x => new { x.MenuDayId, x.ProgramId, x.Slot, x.IsPrimary })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true");
        builder.HasOne(x => x.MenuDay).WithMany(x => x.Meals).HasForeignKey(x => x.MenuDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Meal).WithMany(x => x.MenuDayMeals).HasForeignKey(x => x.MealId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WeekdayMealAssignmentConfiguration : IEntityTypeConfiguration<WeekdayMealAssignment>
{
    public void Configure(EntityTypeBuilder<WeekdayMealAssignment> builder)
    {
        builder.ToTable("WeekdayMealAssignments");
        builder.Property(x => x.Slot).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.MenuPlanVersionId, x.DayOfWeek, x.ProgramId, x.MealId }).IsUnique();
        builder.HasIndex(x => new { x.MenuPlanVersionId, x.DayOfWeek, x.ProgramId, x.Slot, x.IsPrimary })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true");
        builder.HasOne(x => x.MenuPlanVersion).WithMany(x => x.WeekdayAssignments).HasForeignKey(x => x.MenuPlanVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Meal).WithMany(x => x.WeekdayAssignments).HasForeignKey(x => x.MealId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MenuPlanProgramConfiguration : IEntityTypeConfiguration<MenuPlanProgram>
{
    public void Configure(EntityTypeBuilder<MenuPlanProgram> builder)
    {
        builder.ToTable("MenuPlanPrograms");
        builder.HasIndex(x => new { x.MenuPlanId, x.ProgramId }).IsUnique();
        builder.HasIndex(x => new { x.ProgramId, x.EffectiveFrom, x.EffectiveTo });
        builder.HasOne(x => x.MenuPlan).WithMany(x => x.Programs).HasForeignKey(x => x.MenuPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProgramMealSlotRequirementConfiguration : IEntityTypeConfiguration<ProgramMealSlotRequirement>
{
    public void Configure(EntityTypeBuilder<ProgramMealSlotRequirement> builder)
    {
        builder.ToTable("ProgramMealSlotRequirements");
        builder.Property(x => x.Slot).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.ProgramId, x.Slot }).IsUnique();
    }
}

public sealed class ProgramNutritionStandardConfiguration : IEntityTypeConfiguration<ProgramNutritionStandard>
{
    public void Configure(EntityTypeBuilder<ProgramNutritionStandard> builder)
    {
        builder.ToTable("ProgramNutritionStandards");
        builder.Property(x => x.MealCategory).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CarbMode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.StandardVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.KcalMin).HasPrecision(18, 3);
        builder.Property(x => x.KcalMax).HasPrecision(18, 3);
        builder.Property(x => x.ProteinGMin).HasPrecision(18, 3);
        builder.Property(x => x.ProteinGMax).HasPrecision(18, 3);
        builder.Property(x => x.CarbsGMin).HasPrecision(18, 3);
        builder.Property(x => x.CarbsGMax).HasPrecision(18, 3);
        builder.Property(x => x.FatGMin).HasPrecision(18, 3);
        builder.Property(x => x.FatGMax).HasPrecision(18, 3);
        builder.Property(x => x.FiberGMin).HasPrecision(18, 3);
        builder.Property(x => x.CookedProteinGMin).HasPrecision(18, 3);
        builder.Property(x => x.CookedProteinGMax).HasPrecision(18, 3);
        builder.Property(x => x.CookedCarbsGMin).HasPrecision(18, 3);
        builder.Property(x => x.CookedCarbsGMax).HasPrecision(18, 3);
        builder.Property(x => x.KetoDayNetCarbsLimit).HasPrecision(18, 3);
        builder.HasIndex(x => new { x.ProgramId, x.MealCategory, x.StandardVersion }).IsUnique();
        builder.HasIndex(x => new { x.ProgramId, x.IsActive });
    }
}

public sealed class MenuPlanAuditConfiguration : IEntityTypeConfiguration<MenuPlanAudit>
{
    public void Configure(EntityTypeBuilder<MenuPlanAudit> builder)
    {
        builder.ToTable("MenuPlanAudits");
        builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => x.MenuPlanVersionId);
    }
}

public sealed class MealProgramAssignmentConfiguration : IEntityTypeConfiguration<MealProgramAssignment>
{
    public void Configure(EntityTypeBuilder<MealProgramAssignment> builder)
    {
        builder.ToTable("MealProgramAssignments");
        builder.HasIndex(x => new { x.MealId, x.ProgramId }).IsUnique();
        builder.HasOne(x => x.Meal).WithMany(x => x.ProgramAssignments).HasForeignKey(x => x.MealId).OnDelete(DeleteBehavior.Restrict);
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
        builder.HasIndex(x => x.ProgramId);
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

public sealed class CapacityHoldConfiguration : IEntityTypeConfiguration<CapacityHold>
{
    public void Configure(EntityTypeBuilder<CapacityHold> builder)
    {
        builder.ToTable("CapacityHolds");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RestaurantId, x.ServiceDate, x.Status });
        builder.HasIndex(x => new { x.SubscriptionId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);
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
        builder.HasIndex(x => x.ResolvedMenuPlanVersionId);
        builder.HasIndex(x => x.ResolvedMenuDayId);
        builder.HasIndex(x => x.ResolvedProgramId);
        builder.HasIndex(x => x.ScheduleMode);
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
        builder.Property(x => x.BarcodeValue).HasMaxLength(100);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => new { x.RestaurantId, x.ServiceDate });
        builder.HasIndex(x => x.ServiceDate);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.TemplateId);
    }
}

public sealed class MealBarcodeConfiguration : IEntityTypeConfiguration<MealBarcode>
{
    public void Configure(EntityTypeBuilder<MealBarcode> builder)
    {
        builder.ToTable("MealBarcodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BarcodeValue).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasDefaultValue(MealBarcodeStatus.Generated);
        builder.Property(x => x.RevocationReason).HasMaxLength(500);
        builder.Property(x => x.RevokedBy).HasMaxLength(100);

        builder.HasIndex(x => x.BarcodeValue).IsUnique();
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.SubscriptionDayId, x.Status });
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

        builder.Property(x => x.Kind).IsRequired().HasDefaultValue(MealLabelKind.Box);
        builder.Property(x => x.LabelPayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => x.TemplateId);
        builder.HasIndex(x => x.GeneratedAtUtc);
        builder.HasIndex(x => new { x.SubscriptionDayId, x.Kind, x.MealId });
    }
}

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("DocumentTemplates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.Language).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.PageSizeLabel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BackgroundColor).HasMaxLength(20).IsRequired();
        builder.Property(x => x.AccentColor).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LayoutJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.Kind, x.Status });
        builder.HasIndex(x => x.Kind)
            .IsUnique()
            .HasFilter($"\"{nameof(DocumentTemplate.Status)}\" = {(int)DocumentTemplateStatus.Published}");
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
