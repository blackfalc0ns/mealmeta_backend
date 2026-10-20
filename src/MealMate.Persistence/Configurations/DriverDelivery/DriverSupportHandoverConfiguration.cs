using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.DriverDelivery;

public sealed class DriverSupportHandoverConfiguration : IEntityTypeConfiguration<DriverSupportHandover>
{
    public void Configure(EntityTypeBuilder<DriverSupportHandover> builder)
    {
        builder.ToTable("DriverSupportHandovers", table =>
        {
            table.HasCheckConstraint("CK_DriverSupportHandovers_Status", "\"Status\" IN ('Assigned','Arrived','Receiving','Completed','Cancelled')");
            table.HasCheckConstraint("CK_DriverSupportHandovers_Location", "\"PickupLatitude\" BETWEEN -90 AND 90 AND \"PickupLongitude\" BETWEEN -180 AND 180");
            table.HasCheckConstraint("CK_DriverSupportHandovers_DifferentDrivers", "\"OriginalDriverProfileId\" <> \"ReplacementDriverProfileId\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedIssueId).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.PickupLatitude).HasPrecision(10, 7);
        builder.Property(x => x.PickupLongitude).HasPrecision(10, 7);
        builder.Property(x => x.PickupLocationSource).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ArrivalLatitude).HasPrecision(10, 7);
        builder.Property(x => x.ArrivalLongitude).HasPrecision(10, 7);
        builder.HasIndex(x => new { x.RequestedByUserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.TripId, x.Status });
        builder.HasIndex(x => new { x.ComplaintId, x.Status });
        builder.HasIndex(x => x.TripId).IsUnique().HasFilter("\"Status\" IN ('Assigned','Arrived','Receiving')").HasDatabaseName("UX_DriverSupportHandovers_ActiveTrip");
        builder.HasIndex(x => x.ComplaintId).IsUnique().HasFilter("\"Status\" IN ('Assigned','Arrived','Receiving')").HasDatabaseName("UX_DriverSupportHandovers_ActiveComplaint");
        builder.HasIndex(x => x.ReplacementDriverProfileId).IsUnique().HasFilter("\"Status\" IN ('Assigned','Arrived','Receiving')").HasDatabaseName("UX_DriverSupportHandovers_ActiveReplacement");
        builder.HasOne<MealMate.Domain.ComplaintsSupport.Complaint>()
            .WithMany().HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MealMate.Domain.IdentityAccess.DriverProfile>().WithMany().HasForeignKey(x => x.OriginalDriverProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MealMate.Domain.IdentityAccess.DriverProfile>().WithMany().HasForeignKey(x => x.ReplacementDriverProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Boxes).WithOne(x => x.Handover)
            .HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DriverSupportHandoverBoxConfiguration : IEntityTypeConfiguration<DriverSupportHandoverBox>
{
    public void Configure(EntityTypeBuilder<DriverSupportHandoverBox> builder)
    {
        builder.ToTable("DriverSupportHandoverBoxes", table =>
            table.HasCheckConstraint("CK_DriverSupportHandoverBoxes_Status", "\"Status\" IN ('Pending','Received','Missing','Damaged')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ManifestCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ScanMethod).HasMaxLength(20);
        builder.Property(x => x.FollowUpNote).HasMaxLength(500);
        builder.HasIndex(x => new { x.HandoverId, x.TripStopId }).IsUnique();
        builder.HasIndex(x => x.TripStopId);
        builder.HasOne(x => x.FollowUp).WithMany().HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TripStop>().WithMany().HasForeignKey(x => x.TripStopId).OnDelete(DeleteBehavior.Restrict);
    }
}
