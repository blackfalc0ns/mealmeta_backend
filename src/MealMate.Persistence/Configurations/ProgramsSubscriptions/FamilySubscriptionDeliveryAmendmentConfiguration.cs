using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ProgramsSubscriptions;

public sealed class FamilySubscriptionDeliveryAmendmentConfiguration : IEntityTypeConfiguration<FamilySubscriptionDeliveryAmendment>
{
    public void Configure(EntityTypeBuilder<FamilySubscriptionDeliveryAmendment> builder)
    {
        builder.ToTable("FamilySubscriptionDeliveryAmendments"); builder.HasKey(x => x.Id);
        builder.Property(x => x.AddressLine).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AddressLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AffectedDayIdsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.SubscriptionId, x.Version }).IsUnique();
        builder.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
