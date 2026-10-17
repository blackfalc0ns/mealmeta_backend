using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.RestaurantOperations;

public sealed class MealBoxLabelTemplateConfiguration : IEntityTypeConfiguration<MealBoxLabelTemplate>
{
    public void Configure(EntityTypeBuilder<MealBoxLabelTemplate> builder)
    {
        builder.ToTable("MealBoxLabelTemplates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(500);
        builder.Property(x => x.DescriptionEn).HasMaxLength(500);
        builder.Property(x => x.DefaultOrientation).HasMaxLength(20).IsRequired().HasDefaultValue("portrait");
        builder.Property(x => x.ConfigurationJson).IsRequired().HasDefaultValue("{}");

        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => x.IsEnabled);
        builder.HasIndex(x => x.IsPublished);
        builder.HasIndex(x => x.IsRestaurantVisible);
        builder.HasIndex(x => x.IsGlobalDefault);
    }
}

public sealed class RestaurantMealLabelSettingsConfiguration : IEntityTypeConfiguration<RestaurantMealLabelSettings>
{
    public void Configure(EntityTypeBuilder<RestaurantMealLabelSettings> builder)
    {
        builder.ToTable("RestaurantMealLabelSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Orientation).HasMaxLength(20).IsRequired().HasDefaultValue("portrait");
        builder.HasIndex(x => x.RestaurantId).IsUnique();

        builder.HasOne(x => x.SelectedTemplate)
            .WithMany()
            .HasForeignKey(x => x.SelectedTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AdminAssignedTemplate)
            .WithMany()
            .HasForeignKey(x => x.AdminAssignedTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class MealLabelPrintLogConfiguration : IEntityTypeConfiguration<MealLabelPrintLog>
{
    public void Configure(EntityTypeBuilder<MealLabelPrintLog> builder)
    {
        builder.ToTable("MealLabelPrintLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PrintStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ClientIp).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => x.PrintedAtUtc);
    }
}
