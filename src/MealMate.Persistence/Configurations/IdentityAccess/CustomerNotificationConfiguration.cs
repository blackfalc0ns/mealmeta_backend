using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.IdentityAccess;

public sealed class CustomerNotificationConfiguration : IEntityTypeConfiguration<CustomerNotification>
{
    public void Configure(EntityTypeBuilder<CustomerNotification> builder)
    {
        builder.ToTable("CustomerNotifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BodyAr).HasMaxLength(500).IsRequired();
        builder.Property(x => x.BodyEn).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DeepLink).HasMaxLength(300);
        // One inbox row per (user, event, subject): retries and concurrent writers stay idempotent.
        builder.HasIndex(x => new { x.UserId, x.EventKey, x.SubjectId }).IsUnique();
        // Serves the unread-count predicate (UserId, ReadAtUtc == null) and per-user list scans.
        builder.HasIndex(x => new { x.UserId, x.ReadAtUtc });
    }
}
