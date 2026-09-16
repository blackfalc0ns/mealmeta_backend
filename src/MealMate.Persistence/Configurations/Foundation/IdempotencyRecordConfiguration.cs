using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Foundation;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Scope).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResponseBody).HasColumnType("text");
        builder.Property(x => x.ResourceUri).HasMaxLength(500);

        builder.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        builder.HasIndex(x => x.Key);
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => x.UserId);
    }
}
