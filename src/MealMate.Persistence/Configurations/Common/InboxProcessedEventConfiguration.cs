using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Common;

public sealed class InboxProcessedEventConfiguration : IEntityTypeConfiguration<InboxProcessedEvent>
{
    public void Configure(EntityTypeBuilder<InboxProcessedEvent> builder)
    {
        builder.ToTable("InboxProcessedEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConsumerName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.ProcessedAtUtc).IsRequired();
        builder.Property(x => x.FinancePayloadHash).HasMaxLength(64);

        builder.HasIndex(x => new { x.ConsumerName, x.EventId }).IsUnique();

        builder.HasOne<MealMate.Domain.CentralAccounting.JournalEntry>()
            .WithMany()
            .HasForeignKey(x => x.ResultJournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
