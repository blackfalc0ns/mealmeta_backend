using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class ReceiptTemplatesSeeder
{
    public const string ClassicMinimalKey = "classic-minimal";
    public const string OrganicElegantKey = "organic-elegant";
    public const string PremiumDarkKey = "premium-dark";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = await db.ReceiptTemplates
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        var defaults = new[]
        {
            new
            {
                Key = ClassicMinimalKey,
                NameAr = "الكلاسيكي البسيط",
                NameEn = "Classic Minimal",
                DescriptionAr = "تصميم حراري أحادي اللون بخطوط واضحة، تقسيمات منقطة وشعارين في الهيدر، مثالي للطباعة السريعة.",
                DescriptionEn = "Clean monochrome thermal layout with dashed dividers, dual-brand header and compact nutrition metrics.",
                IsGlobalDefault = true,
                ConfigJson = "{\"showAllergens\":false,\"showIngredients\":false,\"showPrepInstructions\":true,\"compactNutrition\":true,\"highContrastMode\":false}"
            },
            new
            {
                Key = OrganicElegantKey,
                NameAr = "الأنيق الطبيعي",
                NameEn = "Organic Elegant",
                DescriptionAr = "تصميم نباتي مميز بلمسات طبيعية وصندوق أنيق داكن لتفاصيل الطلب وبطاقات وجبات ناعمة.",
                DescriptionEn = "Botanical decorative accents, dark rounded order panel, soft meal cards and elegant typography.",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":false,\"showPrepInstructions\":true,\"compactNutrition\":true,\"highContrastMode\":false}"
            },
            new
            {
                Key = PremiumDarkKey,
                NameAr = "الداكن الفاخر",
                NameEn = "Premium Dark",
                DescriptionAr = "تصميم عصري جريء بشريط كبسولي داكن لرقم الطلب ورسومات نباتية أنيقة مع وضع تباين حراري آمن.",
                DescriptionEn = "Bold dark header banner, high-contrast typography, line-art flourishes and thermal-safe print mode.",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"compactNutrition\":false,\"highContrastMode\":true}"
            }
        };

        var modified = false;
        var now = DateTime.UtcNow;

        foreach (var def in defaults)
        {
            var match = existing.FirstOrDefault(x => x.Key == def.Key);
            if (match is null)
            {
                db.ReceiptTemplates.Add(new ReceiptTemplate
                {
                    Key = def.Key,
                    NameAr = def.NameAr,
                    NameEn = def.NameEn,
                    DescriptionAr = def.DescriptionAr,
                    DescriptionEn = def.DescriptionEn,
                    IsEnabled = true,
                    IsPublished = true,
                    IsRestaurantVisible = true,
                    IsGlobalDefault = def.IsGlobalDefault,
                    Version = 1,
                    ConfigurationJson = def.ConfigJson,
                    PublishedAtUtc = now,
                    CreatedAtUtc = now,
                });
                modified = true;
            }
            else
            {
                // Ensure active and published
                if (match.IsDeleted)
                {
                    match.IsDeleted = false;
                    modified = true;
                }
            }
        }

        if (modified)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Receipt template defaults verified and seeded.");
        }
    }
}
