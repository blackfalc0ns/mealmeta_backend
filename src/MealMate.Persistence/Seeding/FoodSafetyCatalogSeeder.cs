using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Seeding;

public static class FoodSafetyCatalogSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var allergens = new (string Code, string Ar, string En)[]
        {
            ("GLUTEN", "جلوتين", "Gluten"),
            ("EGGS", "بيض", "Eggs"),
            ("MILK", "لاكتوز", "Lactose"),
            ("PEANUTS", "فول سوداني", "Peanuts"),
            ("TREE_NUTS", "مكسرات", "Nuts"),
            ("FISH", "أسماك", "Fish"),
            ("CRUSTACEANS", "قشريات", "Crustaceans"),
            ("SOY", "صويا", "Soy"),
            ("SESAME", "سمسم", "Sesame"),
            ("CELERY", "كرفس", "Celery"),
            ("MUSTARD", "خردل", "Mustard"),
            ("SULPHITES", "كبريتيت", "Sulphites"),
            ("LUPIN", "ترمس", "Lupin"),
            ("MOLLUSCS", "رخويات", "Molluscs"),
        };
        var existingAllergens = await db.AllergenDefinitions.IgnoreQueryFilters().ToListAsync(ct);
        for (var i = 0; i < allergens.Length; i++)
        {
            var item = allergens[i];
            var row = existingAllergens.FirstOrDefault(x => x.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                db.AllergenDefinitions.Add(new AllergenDefinition
                {
                    Code = item.Code, NameAr = item.Ar, NameEn = item.En, IsActive = true, SortOrder = i + 1,
                });
                continue;
            }

            row.NameAr = item.Ar;
            row.NameEn = item.En;
            row.IsActive = true;
            row.IsDeleted = false;
            row.SortOrder = i + 1;
        }

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
