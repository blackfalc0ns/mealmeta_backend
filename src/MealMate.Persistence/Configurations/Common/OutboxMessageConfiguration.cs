using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Common;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(64);
        builder.Property(x => x.AggregateId).HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.DeduplicationKey).HasMaxLength(256);
        builder.Property(x => x.EventVersion).HasDefaultValue(1);
        builder.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.Property(x => x.LockedByWorkerId).HasMaxLength(128);
        builder.Property(x => x.LockExpiresAtUtc);
        builder.Property(x => x.LeaseToken);

        builder.HasIndex(x => new { x.ProcessedAtUtc, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.PoisonedAtUtc, x.LockExpiresAtUtc, x.NextAttemptAtUtc })
            .HasDatabaseName("IX_OutboxMessages_Lease_Pending");
        builder.HasIndex(x => x.DeduplicationKey).IsUnique();
    }
}
