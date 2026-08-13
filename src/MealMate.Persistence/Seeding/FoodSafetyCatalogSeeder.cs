using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Seeding;

public static class FoodSafetyCatalogSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var allergens = new (string Code, string Ar, string En)[]
        {
            ("GLUTEN", "القمح والجلوتين", "Wheat and gluten"), ("EGGS", "البيض", "Eggs"), ("MILK", "الحليب", "Milk"),
            ("PEANUTS", "الفول السوداني", "Peanuts"), ("TREE_NUTS", "المكسرات", "Tree nuts"), ("FISH", "السمك", "Fish"),
            ("CRUSTACEANS", "القشريات", "Crustaceans"), ("SOY", "الصويا", "Soy"), ("SESAME", "السمسم", "Sesame")
        };
        var existingAllergens = await db.AllergenDefinitions.IgnoreQueryFilters().Select(x => x.Code).ToListAsync(ct);
        db.AllergenDefinitions.AddRange(allergens.Where(x => !existingAllergens.Contains(x.Code)).Select((x, i) => new AllergenDefinition { Code = x.Code, NameAr = x.Ar, NameEn = x.En, IsActive = true, SortOrder = i + 1 }));

        var preferences = new (string Code, string Ar, string En)[]
        {
            ("VEGETARIAN", "نباتي", "Vegetarian"), ("KETO", "كيتو", "Keto"), ("LOW_CARB", "قليل الكربوهيدرات", "Low carb"),
            ("LACTOSE_FREE", "خالٍ من اللاكتوز", "Lactose free"), ("GLUTEN_FREE", "خالٍ من الجلوتين", "Gluten free")
        };
        var existingPreferences = await db.FoodPreferenceDefinitions.IgnoreQueryFilters().Select(x => x.Code).ToListAsync(ct);
        db.FoodPreferenceDefinitions.AddRange(preferences.Where(x => !existingPreferences.Contains(x.Code)).Select((x, i) => new FoodPreferenceDefinition { Code = x.Code, NameAr = x.Ar, NameEn = x.En, IsActive = true, SortOrder = i + 1 }));
        await db.SaveChangesAsync(ct);
    }
}
