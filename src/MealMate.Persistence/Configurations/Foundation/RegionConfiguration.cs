using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Foundation;

public sealed class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("Regions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CentroidLatitude).HasPrecision(9, 6);
        builder.Property(x => x.CentroidLongitude).HasPrecision(9, 6);

        builder.HasIndex(x => new { x.CountryId, x.Code }).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.CountryId, x.ParentRegionId, x.IsActive });

        builder.HasOne(x => x.ParentRegion)
            .WithMany(x => x.ChildRegions)
            .HasForeignKey(x => x.ParentRegionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
