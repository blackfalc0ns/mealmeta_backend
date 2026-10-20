using MealMate.Domain.DriverDelivery;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.DriverDelivery;

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trips");
        builder.HasKey(x => x.Id);
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ShiftName).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.Code);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.DispatcherUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PlannedDate);
        builder.HasIndex(x => new { x.RestaurantId, x.PlannedDate });

        builder.HasMany(x => x.Stops)
            .WithOne(x => x.Trip)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("TripStops");
        builder.HasKey(x => x.Id);
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(x => x.OrderCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CustomerMaskedId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200);
        builder.Property(x => x.CustomerPhone).HasMaxLength(50);
        builder.Property(x => x.ZoneNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ZoneNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FullAddress).HasMaxLength(500);
        builder.Property(x => x.MealSummaryAr).HasMaxLength(500).IsRequired();
        builder.Property(x => x.MealSummaryEn).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SlotLabel).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.Latitude).HasPrecision(10, 7);
        builder.Property(x => x.Longitude).HasPrecision(10, 7);

        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.DailyOrderId);
        builder.HasIndex(x => x.SubscriptionDayId)
            .IsUnique()
            .HasFilter(@"""SubscriptionDayId"" IS NOT NULL AND ""Status"" IN (1, 2)")
            .HasDatabaseName("IX_TripStops_Active_SubscriptionDayId");
        builder.HasIndex(x => x.MealBarcodeId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.MealBarcode)
            .WithMany()
            .HasForeignKey(x => x.MealBarcodeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DriverAssignmentConfiguration : IEntityTypeConfiguration<DriverAssignment>
{
    public void Configure(EntityTypeBuilder<DriverAssignment> builder)
    {
        builder.ToTable("DriverAssignments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => new { x.TripId, x.DriverProfileId });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AssignedAtUtc);

        builder.HasOne(x => x.Trip)
            .WithMany(x => x.DriverAssignments)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PickupScanConfiguration : IEntityTypeConfiguration<PickupScan>
{
    public void Configure(EntityTypeBuilder<PickupScan> builder)
    {
        builder.ToTable("PickupScans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BarcodeValue).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PhotoPath).HasMaxLength(500);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.IsManualEntry).HasDefaultValue(false);
        builder.Property(x => x.ManualEntryReason).HasMaxLength(500);

        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.BarcodeValue);
        builder.HasIndex(x => x.ScannedAtUtc);

        builder.HasOne(x => x.Trip)
            .WithMany(x => x.PickupScans)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryTrackingPointConfiguration : IEntityTypeConfiguration<DeliveryTrackingPoint>
{
    public void Configure(EntityTypeBuilder<DeliveryTrackingPoint> builder)
    {
        builder.ToTable("DeliveryTrackingPoints");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.Heading);
        builder.Property(x => x.SpeedKmh);
        builder.Property(x => x.CapturedAtUtc);
        builder.Property(x => x.ReceivedAtUtc);
        builder.Property(x => x.AccuracyMeters);

        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.RecordedAtUtc);
        builder.HasIndex(x => new { x.TripId, x.RecordedAtUtc });

        builder.HasOne(x => x.Trip)
            .WithMany(x => x.TrackingPoints)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SafeContactLogConfiguration : IEntityTypeConfiguration<SafeContactLog>
{
    public void Configure(EntityTypeBuilder<SafeContactLog> builder)
    {
        builder.ToTable("SafeContactLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContactType).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.Within3Km);
    }
}

public sealed class DeliveryHoldConfiguration : IEntityTypeConfiguration<DeliveryHold>
{
    public void Configure(EntityTypeBuilder<DeliveryHold> builder)
    {
        builder.ToTable("DeliveryHolds");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.HeldAtUtc);
        builder.HasIndex(x => x.DriverSupportHandoverBoxId);

        builder.HasOne(x => x.Trip)
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SecondDeliveryAttemptConfiguration : IEntityTypeConfiguration<SecondDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<SecondDeliveryAttempt> builder)
    {
        builder.ToTable("SecondDeliveryAttempts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Outcome).HasMaxLength(200);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.ScheduledAtUtc);
        builder.HasIndex(x => new { x.OrderId, x.AttemptNumber });
        builder.HasIndex(x => new { x.SubscriptionDayId, x.AttemptNumber });
    }
}

public sealed class DeliveryConfirmationScanConfiguration : IEntityTypeConfiguration<DeliveryConfirmationScan>
{
    public void Configure(EntityTypeBuilder<DeliveryConfirmationScan> builder)
    {
        builder.ToTable("DeliveryConfirmationScans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BarcodeValue).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PhotoPath).HasMaxLength(500);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.BarcodeValue);
        builder.HasIndex(x => x.ScannedAtUtc);
    }
}

public sealed class DriverRatingConfiguration : IEntityTypeConfiguration<DriverRating>
{
    public void Configure(EntityTypeBuilder<DriverRating> builder)
    {
        builder.ToTable("DriverRatings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.ToTable(t => t.HasCheckConstraint("CK_DriverRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5"));

        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => new { x.DriverProfileId, x.CustomerUserId, x.SubscriptionDayId });
        builder.HasIndex(x => x.Score);
    }
}

public sealed class DriverPerformanceStatConfiguration : IEntityTypeConfiguration<DriverPerformanceStat>
{
    public void Configure(EntityTypeBuilder<DriverPerformanceStat> builder)
    {
        builder.ToTable("DriverPerformanceStats");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AvgRating).HasPrecision(4, 2);
        builder.Property(x => x.OnTimeRate).HasNullablePercentageConversion();

        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => new { x.DriverProfileId, x.PeriodStart, x.PeriodEnd }).IsUnique();
        builder.HasIndex(x => x.PeriodStart);
        builder.HasIndex(x => x.PeriodEnd);
    }
}

public sealed class PickupValidationConfiguration : IEntityTypeConfiguration<PickupValidation>
{
    public void Configure(EntityTypeBuilder<PickupValidation> builder)
    {
        builder.ToTable("PickupValidations");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.BarcodeValue).HasMaxLength(100).IsRequired();
        builder.Property(v => v.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(v => v.TokenHash).IsUnique();
        builder.HasIndex(v => new { v.TripId, v.BoxId, v.DriverProfileId, v.ValidatedAtUtc });
    }
}

public sealed class DriverAppSettingsConfiguration : IEntityTypeConfiguration<DriverAppSettings>
{
    public void Configure(EntityTypeBuilder<DriverAppSettings> builder)
    {
        builder.ToTable("DriverAppSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PreferredLanguage).HasMaxLength(10);
        builder.Property(x => x.PreferredNavigationApp).HasMaxLength(20);

        builder.ToTable(t => t.HasCheckConstraint("CK_DriverAppSettings_PreferredLanguage",
            "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN ('ar', 'en')"));
        builder.ToTable(t => t.HasCheckConstraint("CK_DriverAppSettings_PreferredNavigationApp",
            "\"PreferredNavigationApp\" IS NULL OR \"PreferredNavigationApp\" IN ('GoogleMaps', 'Waze', 'AppleMaps')"));

        builder.HasIndex(x => x.DriverProfileId).IsUnique();
    }
}

public sealed class PickupConditionPhotoConfiguration : IEntityTypeConfiguration<PickupConditionPhoto>
{
    public void Configure(EntityTypeBuilder<PickupConditionPhoto> builder)
    {
        builder.ToTable("PickupConditionPhotos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Checksum).HasMaxLength(128);
        builder.HasIndex(p => p.StorageKey).IsUnique();
        builder.HasIndex(p => new { p.TripId, p.BoxId, p.DriverProfileId, p.UploadedAtUtc });
    }

/// <summary>
/// Operational status is single-valued per driver: at most one active work
/// session may exist. Enforced in the database so concurrent supervisor
/// activations cannot open duplicate sessions.
/// </summary>
public sealed class DriverWorkSessionConfiguration : IEntityTypeConfiguration<DriverWorkSession>
{
    public void Configure(EntityTypeBuilder<DriverWorkSession> builder)
    {
        builder.HasIndex(x => x.DriverProfileId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName("IX_DriverWorkSessions_DriverProfileId_Active");
    }
}
}
