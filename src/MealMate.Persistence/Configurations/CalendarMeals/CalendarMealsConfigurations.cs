using MealMate.Domain.CalendarMeals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CalendarMeals;

public sealed class SubscriptionCalendarConfiguration : IEntityTypeConfiguration<SubscriptionCalendar>
{
    public void Configure(EntityTypeBuilder<SubscriptionCalendar> builder)
    {
        builder.ToTable("SubscriptionCalendars");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.GeneratedAtUtc);
    }
}

public sealed class SubscriptionDayConfiguration : IEntityTypeConfiguration<SubscriptionDay>
{
    public void Configure(EntityTypeBuilder<SubscriptionDay> builder)
    {
        builder.ToTable("SubscriptionDays");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => new { x.SubscriptionId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ServiceDate);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.ResolvedMenuPlanVersionId);
        builder.HasIndex(x => x.LockAtUtc);
        builder.HasIndex(x => x.CalendarId);

        builder.HasOne(x => x.Calendar)
            .WithMany(x => x.Days)
            .HasForeignKey(x => x.CalendarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DailyRestaurantSelectionConfiguration : IEntityTypeConfiguration<DailyRestaurantSelection>
{
    public void Configure(EntityTypeBuilder<DailyRestaurantSelection> builder)
    {
        builder.ToTable("DailyRestaurantSelections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SelectionSource).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.SubscriptionDayId, x.RestaurantId, x.SelectedAtUtc });
        builder.HasIndex(x => x.SelectionSource);

        builder.HasOne(x => x.SubscriptionDay)
            .WithMany(x => x.RestaurantSelections)
            .HasForeignKey(x => x.SubscriptionDayId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DailyMealSelectionConfiguration : IEntityTypeConfiguration<DailyMealSelection>
{
    public void Configure(EntityTypeBuilder<DailyMealSelection> builder)
    {
        builder.ToTable("DailyMealSelections");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => x.ResolvedMenuPlanVersionId);
        builder.HasIndex(x => x.ResolvedMenuDayId);
        builder.HasIndex(x => x.ResolvedProgramId);
        builder.HasIndex(x => x.ScheduleMode);
        builder.HasIndex(x => new { x.SubscriptionDayId, x.SortOrder });
        builder.HasIndex(x => new { x.SubscriptionDayId, x.MealId }).IsUnique();

        builder.HasOne(x => x.SubscriptionDay)
            .WithMany(x => x.MealSelections)
            .HasForeignKey(x => x.SubscriptionDayId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerAllergyPreferenceConfiguration : IEntityTypeConfiguration<CustomerAllergyPreference>
{
    public void Configure(EntityTypeBuilder<CustomerAllergyPreference> builder)
    {
        builder.ToTable("CustomerAllergyPreferences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllergenCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AllergenNameAr).HasMaxLength(200);
        builder.Property(x => x.AllergenNameEn).HasMaxLength(200);
        builder.Property(x => x.Severity).HasMaxLength(50);

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => new { x.CustomerUserId, x.AllergenCode, x.IsDislike }).IsUnique();
        builder.HasIndex(x => x.AllergenCode);
    }
}

public sealed class CustomerDietaryPreferenceConfiguration : IEntityTypeConfiguration<CustomerDietaryPreference>
{
    public void Configure(EntityTypeBuilder<CustomerDietaryPreference> builder)
    {
        builder.ToTable("CustomerDietaryPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreferenceCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.CustomerUserId, x.PreferenceCode }).IsUnique();
        builder.HasIndex(x => x.PreferenceCode);
    }
}

public sealed class SubscriptionDayMealSnapshotConfiguration : IEntityTypeConfiguration<SubscriptionDayMealSnapshot>
{
    public void Configure(EntityTypeBuilder<SubscriptionDayMealSnapshot> builder)
    {
        builder.ToTable("SubscriptionDayMealSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequiredSlotsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MealsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => x.SubscriptionDayId).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.ServiceDate }).IsUnique();
        builder.HasIndex(x => x.MenuPlanVersionId);
    }
}

public sealed class MealChangeRequestConfiguration : IEntityTypeConfiguration<MealChangeRequest>
{
    public void Configure(EntityTypeBuilder<MealChangeRequest> builder)
    {
        builder.ToTable("MealChangeRequests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(2000);

        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedByUserId);
        builder.HasIndex(x => x.FromMealId);
        builder.HasIndex(x => x.ToMealId);

        builder.HasOne(x => x.SubscriptionDay)
            .WithMany(x => x.MealChangeRequests)
            .HasForeignKey(x => x.SubscriptionDayId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionFreezeConfiguration : IEntityTypeConfiguration<SubscriptionFreeze>
{
    public void Configure(EntityTypeBuilder<SubscriptionFreeze> builder)
    {
        builder.ToTable("SubscriptionFreezes");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.SubscriptionId, x.StartsOn, x.EndsOn });
        builder.HasIndex(x => x.RequestedAtUtc);
    }
}

public sealed class FreezeLedgerEntryConfiguration : IEntityTypeConfiguration<FreezeLedgerEntry>
{
    public void Configure(EntityTypeBuilder<FreezeLedgerEntry> builder)
    {
        builder.ToTable("FreezeLedgerEntries");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.FreezeId);
        builder.HasIndex(x => x.RecordedAtUtc);

        builder.HasOne(x => x.Freeze)
            .WithMany(x => x.LedgerEntries)
            .HasForeignKey(x => x.FreezeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DiversificationLimitSnapshotConfiguration : IEntityTypeConfiguration<DiversificationLimitSnapshot>
{
    public void Configure(EntityTypeBuilder<DiversificationLimitSnapshot> builder)
    {
        builder.ToTable("DiversificationLimitSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RawLimit).HasPrecision(18, 6);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.CapturedAtUtc);
    }
}
