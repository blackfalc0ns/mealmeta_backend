using MealMate.Domain.DriverDelivery;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.VoiceCalls;

/// <summary>
/// v2 schema (T07): database-level guards for busy uniqueness, per-call receipts,
/// contact-cycle/attempt integrity, reveal-grant idempotency, and delivery dedup.
/// </summary>
public sealed class VoiceCallBusyParticipantConfiguration : IEntityTypeConfiguration<VoiceCallBusyParticipant>
{
    public void Configure(EntityTypeBuilder<VoiceCallBusyParticipant> builder)
    {
        builder.ToTable("VoiceCallBusyParticipants");
        builder.HasKey(x => x.UserId);

        builder.HasOne<CallSession>()
            .WithMany()
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VoiceCallDeviceReceiptConfiguration : IEntityTypeConfiguration<VoiceCallDeviceReceipt>
{
    public void Configure(EntityTypeBuilder<VoiceCallDeviceReceipt> builder)
    {
        builder.ToTable("VoiceCallDeviceReceipts");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.CallId, x.DeviceSessionId }).IsUnique();

        builder.HasOne<CallSession>()
            .WithMany()
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VoiceCallEventDeliveryConfiguration : IEntityTypeConfiguration<VoiceCallEventDelivery>
{
    public void Configure(EntityTypeBuilder<VoiceCallEventDelivery> builder)
    {
        builder.ToTable("VoiceCallEventDeliveries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Channel).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(256);
        builder.Property(x => x.ReasonCode).HasMaxLength(64);

        // Delivery dedup key: one record per (event, channel, device).
        builder.HasIndex(x => new { x.EventId, x.Channel, x.RecipientDeviceSessionId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });

        builder.HasOne<CallSession>()
            .WithMany()
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeliveryContactCaseConfiguration : IEntityTypeConfiguration<DeliveryContactCase>
{
    public void Configure(EntityTypeBuilder<DeliveryContactCase> builder)
    {
        builder.ToTable("DeliveryContactCases");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TripStopId, x.ActiveDriverAssignmentId, x.ContactCycleId }).IsUnique();
        // At most one OPEN cycle per stop+assignment (filtered unique index).
        builder.HasIndex(x => new { x.TripStopId, x.ActiveDriverAssignmentId })
            .HasFilter($"\"{nameof(DeliveryContactCase.State)}\" = 'Open'")
            .IsUnique();

        builder.HasIndex(x => new { x.TripStopId, x.State });
    }
}

public sealed class DeliveryContactAttemptConfiguration : IEntityTypeConfiguration<DeliveryContactAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryContactAttempt> builder)
    {
        builder.ToTable("DeliveryContactAttempts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired();

        // One attempt per finished call, ever.
        builder.HasIndex(x => x.CallId).IsUnique();
        builder.HasIndex(x => new { x.ContactCaseId, x.Ordinal });

        builder.HasOne<CallSession>()
            .WithOne()
            .HasForeignKey<DeliveryContactAttempt>(x => x.CallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerPhoneRevealGrantConfiguration : IEntityTypeConfiguration<CustomerPhoneRevealGrant>
{
    public void Configure(EntityTypeBuilder<CustomerPhoneRevealGrant> builder)
    {
        builder.ToTable("CustomerPhoneRevealGrants");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        // Reveal idempotency: one grant per (driver, clientRequestId) — replay with the
        // same key inside the validity window resolves to the same grant.
        builder.HasIndex(x => new { x.DriverUserId, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);

        builder.HasOne<DeliveryContactCase>()
            .WithMany()
            .HasForeignKey(x => x.ContactCaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
