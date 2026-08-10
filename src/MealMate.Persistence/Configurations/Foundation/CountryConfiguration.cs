using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Foundation;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(3).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.DialCode).HasMaxLength(8).HasDefaultValue("+965").IsRequired();
        builder.Property(x => x.FlagEmoji).HasMaxLength(16).HasDefaultValue("🇰🇼").IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);

        builder.HasMany(x => x.Regions)
            .WithOne(x => x.Country)
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
