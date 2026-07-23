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

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.DispatcherUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PlannedDate);
        builder.HasIndex(x => new { x.RestaurantId, x.PlannedDate });
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
