using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.VoiceCalls;

/// <summary>
/// EF Core configuration for <see cref="CallSession"/>.
/// Configures table mapping, foreign keys with audit preservation, concurrency token, and performance indexes.
/// </summary>
public sealed class CallSessionConfiguration : IEntityTypeConfiguration<CallSession>
{
    public void Configure(EntityTypeBuilder<CallSession> builder)
    {
        builder.ToTable("CallSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TripStopId)
            .IsRequired();

        builder.Property(x => x.CallerUserId)
            .IsRequired();

        builder.Property(x => x.CallerType)
            .IsRequired();

        builder.Property(x => x.ReceiverUserId)
            .IsRequired();

        builder.Property(x => x.ReceiverType)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.StartedAtUtc)
            .IsRequired();

        builder.Property(x => x.ClientRequestId)
            .IsRequired();

        // Concurrency token mapped to PostgreSQL xmin system column
        builder.Property(x => x.Version)
            .IsRowVersion();

        // Foreign key to delivery TripStop. Restrict ensures call audit history is preserved even if stops are manipulated.
        builder.HasOne(x => x.TripStop)
            .WithMany()
            .HasForeignKey(x => x.TripStopId)
            .OnDelete(DeleteBehavior.Restrict);

        // Foreign key index for trip stop call history lookup
        builder.HasIndex(x => x.TripStopId);

        // Composite indexes supporting live call verification (is user already in a live call?)
        builder.HasIndex(x => new { x.CallerUserId, x.Status });
        builder.HasIndex(x => new { x.ReceiverUserId, x.Status });

        // Chronological audit index
        builder.HasIndex(x => x.CreatedAtUtc);

        // Composite indexes for recovery workers
        builder.HasIndex(x => new { x.Status, x.RingingAtUtc });
        builder.HasIndex(x => new { x.Status, x.AcceptedAtUtc });
        builder.HasIndex(x => new { x.Status, x.ConnectingAtUtc });

        // Idempotency constraint: prevents duplicate call requests from the same caller on rapid retries
        builder.HasIndex(x => new { x.CallerUserId, x.ClientRequestId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");
    }
}
