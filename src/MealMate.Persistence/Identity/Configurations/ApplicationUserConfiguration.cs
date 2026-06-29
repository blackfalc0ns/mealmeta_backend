using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Identity.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(200);
        builder.Property(u => u.UserType).IsRequired();
        builder.Property(u => u.AccountStatus).IsRequired();
        builder.Property(u => u.CreatedAtUtc).IsRequired();

        builder.HasIndex(u => u.UserType);
        builder.HasIndex(u => u.AccountStatus);
        builder.HasIndex(u => u.RestaurantId);

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("\"PhoneNumber\" IS NOT NULL");
    }
}
