using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.VoiceCalls;

public sealed class VoiceCallDeviceSessionConfiguration : IEntityTypeConfiguration<VoiceCallDeviceSession>
{
    public void Configure(EntityTypeBuilder<VoiceCallDeviceSession> builder)
    {
        builder.ToTable("VoiceCallDeviceSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstallationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ControlProofHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ApnsVoipToken).HasMaxLength(256);

        // Rotation lookup per installation; hash uniqueness hardens against proof collisions.
        builder.HasIndex(x => new { x.UserId, x.InstallationId });

        // G06: database-level rotation invariant — at most ONE active session per
        // (user, installation). Revoked sessions (RevokedAtUtc set) are excluded.
        builder.HasIndex(x => new { x.UserId, x.InstallationId })
            .IsUnique()
            .HasFilter("\"RevokedAtUtc\" IS NULL");
        builder.HasIndex(x => x.ControlProofHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);

        // An expired/rotated session keeps its row for audit; deletion is a retention
        // concern (T25), not part of the control path.
        builder.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
    }
}
