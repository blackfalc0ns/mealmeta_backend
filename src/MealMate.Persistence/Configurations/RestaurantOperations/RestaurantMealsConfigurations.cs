using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.RestaurantOperations;

public sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("Ingredients");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(1000);
        builder.Property(x => x.AllergenCodes).HasMaxLength(500);
        builder.Property(x => x.OriginCountryCode).HasMaxLength(3);
        builder.Property(x => x.SupplierName).HasMaxLength(200);
        builder.Property(x => x.TraceabilityNote).HasMaxLength(1000);

        builder.Property(x => x.DefaultCaloriesPer100g).HasPrecision(18, 3);
        builder.Property(x => x.DefaultProteinPer100g).HasPrecision(18, 3);
        builder.Property(x => x.DefaultCarbsPer100g).HasPrecision(18, 3);
        builder.Property(x => x.DefaultFatPer100g).HasPrecision(18, 3);
        builder.Property(x => x.DefaultWeightGrams).HasPrecision(18, 3);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => new { x.RestaurantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class MealIngredientConfiguration : IEntityTypeConfiguration<MealIngredient>
{
    public void Configure(EntityTypeBuilder<MealIngredient> builder)
    {
        builder.ToTable("MealIngredients");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PortionGrams).HasPrecision(18, 3).IsRequired();

        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => x.IngredientId);
        builder.HasIndex(x => new { x.MealId, x.IngredientId }).IsUnique();

        builder.HasOne(x => x.Meal)
            .WithMany(x => x.Ingredients)
            .HasForeignKey(x => x.MealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Ingredient)
            .WithMany(x => x.MealIngredients)
            .HasForeignKey(x => x.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MealImageConfiguration : IEntityTypeConfiguration<MealImage>
{
    public void Configure(EntityTypeBuilder<MealImage> builder)
    {
        builder.ToTable("MealImages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.AltTextAr).HasMaxLength(200);
        builder.Property(x => x.AltTextEn).HasMaxLength(200);

        builder.HasIndex(x => x.MealId);
        builder.HasIndex(x => new { x.MealId, x.DisplayOrder });

        builder.HasOne(x => x.Meal)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.MealId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AllergenDefinitionConfiguration : IEntityTypeConfiguration<AllergenDefinition>
{
    public void Configure(EntityTypeBuilder<AllergenDefinition> builder)
    {
        builder.ToTable("AllergenDefinitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

public sealed class FoodPreferenceDefinitionConfiguration : IEntityTypeConfiguration<FoodPreferenceDefinition>
{
    public void Configure(EntityTypeBuilder<FoodPreferenceDefinition> builder)
    {
        builder.ToTable("FoodPreferenceDefinitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

public sealed class IngredientFoodPreferenceConfiguration : IEntityTypeConfiguration<IngredientFoodPreference>
{
    public void Configure(EntityTypeBuilder<IngredientFoodPreference> builder)
    {
        builder.ToTable("IngredientFoodPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreferenceCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.IngredientId, x.PreferenceCode }).IsUnique();
    }
}

public sealed class MealFoodPreferenceConfiguration : IEntityTypeConfiguration<MealFoodPreference>
{
    public void Configure(EntityTypeBuilder<MealFoodPreference> builder)
    {
        builder.ToTable("MealFoodPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreferenceCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.MealId, x.PreferenceCode }).IsUnique();
    }
}
