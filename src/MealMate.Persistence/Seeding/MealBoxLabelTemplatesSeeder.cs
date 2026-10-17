using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class MealBoxLabelTemplatesSeeder
{
    public const string ClassicCleanKey = "classic-clean";
    public const string OrganicNatureKey = "organic-nature";
    public const string LuxuryDarkKey = "luxury-dark";
    public const string FreshModernKey = "fresh-modern";
    public const string EcoKraftKey = "eco-kraft";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = await db.MealBoxLabelTemplates
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        var defaults = new[]
        {
            new
            {
                Key = ClassicCleanKey,
                NameAr = "الكلاسيكي النقي",
                NameEn = "Classic Clean",
                DescriptionAr = "خلفية بيضاء فاخرة مع لمسات نباتية خضراء رقيقة، بطاقات التغذية الأربعة، ومكونات ومسببات حساسية وملاحظات شيف واضحة بدون باركود.",
                DescriptionEn = "Premium clean white background with subtle green botanical accents, 4 nutrition cards, ingredients, allergens and chef prep notes.",
                DefaultWidthMm = 70,
                DefaultHeightMm = 100,
                DefaultOrientation = "portrait",
                IsGlobalDefault = true,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"showPackagingDate\":true,\"showOrderReference\":true,\"showQuantity\":true,\"accentColor\":\"#15803d\"}"
            },
            new
            {
                Key = OrganicNatureKey,
                NameAr = "الطبيعي الحيوي",
                NameEn = "Organic Nature",
                DescriptionAr = "خلفية كريمية ناعمة بملمس ورقي نباتي، شريط عنوان كبسولي أخضر داكن، رسومات نباتية منعشة وليمون وبطاقات تغذية دائرية.",
                DescriptionEn = "Warm cream organic paper texture, emerald headline banner, botanical illustrations, tomato and lemon accents with soft metric pills.",
                DefaultWidthMm = 70,
                DefaultHeightMm = 100,
                DefaultOrientation = "portrait",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"showPackagingDate\":true,\"showOrderReference\":true,\"showQuantity\":true,\"accentColor\":\"#047857\"}"
            },
            new
            {
                Key = LuxuryDarkKey,
                NameAr = "الداكن الفاخر",
                NameEn = "Luxury Dark",
                DescriptionAr = "تصميم أسود عميق وفاخر بزخارف نباتية ذهبية ناعمة وهوية ذهبية راقية للشعارات وبطاقات التغذية الذهبية الفاخرة.",
                DescriptionEn = "Deep matte black background with fine gold filigree, opulent gold typography, gold-bordered metrics and luxury dark hierarchy.",
                DefaultWidthMm = 70,
                DefaultHeightMm = 100,
                DefaultOrientation = "portrait",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"showPackagingDate\":true,\"showOrderReference\":true,\"showQuantity\":true,\"accentColor\":\"#eab308\"}"
            },
            new
            {
                Key = FreshModernKey,
                NameAr = "العصري المنعش",
                NameEn = "Fresh Modern",
                DescriptionAr = "تصميم عصري بخلفية بيضاء ومنحنيات خضراء فستقية ناعمة، شارات تغذية ملونة حيوية، وتصميم متجاوب يدعم الطولي والعرضي.",
                DescriptionEn = "Fresh white card with pastel-green organic curves, vibrant colorful nutrition pills, rounded modern panels with reflow support.",
                DefaultWidthMm = 70,
                DefaultHeightMm = 100,
                DefaultOrientation = "portrait",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"showPackagingDate\":true,\"showOrderReference\":true,\"showQuantity\":true,\"accentColor\":\"#22c55e\"}"
            },
            new
            {
                Key = EcoKraftKey,
                NameAr = "الورق البيئي",
                NameEn = "Eco Kraft",
                DescriptionAr = "مظهر ورق كرافت طبيعي أصيل مع رسومات حفر يدوية (ثوم، ديك، طبق طعام) وخطوط حبر سوداء كلاسيكية تدعم هوية التغليف المستدام.",
                DescriptionEn = "Authentic kraft paper aesthetic with vintage hand-drawn engraving illustrations, black ink typography and sustainable packaging style.",
                DefaultWidthMm = 70,
                DefaultHeightMm = 100,
                DefaultOrientation = "portrait",
                IsGlobalDefault = false,
                ConfigJson = "{\"showAllergens\":true,\"showIngredients\":true,\"showPrepInstructions\":true,\"showPackagingDate\":true,\"showOrderReference\":true,\"showQuantity\":true,\"accentColor\":\"#78350f\"}"
            }
        };

        var modified = false;
        var now = DateTime.UtcNow;

        foreach (var def in defaults)
        {
            var match = existing.FirstOrDefault(x => x.Key == def.Key);
            if (match is null)
            {
                db.MealBoxLabelTemplates.Add(new MealBoxLabelTemplate
                {
                    Key = def.Key,
                    NameAr = def.NameAr,
                    NameEn = def.NameEn,
                    DescriptionAr = def.DescriptionAr,
                    DescriptionEn = def.DescriptionEn,
                    DefaultWidthMm = def.DefaultWidthMm,
                    DefaultHeightMm = def.DefaultHeightMm,
                    DefaultOrientation = def.DefaultOrientation,
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
            logger.LogInformation("Meal box label templates seeded successfully with 5 authoritative templates.");
        }
    }
}
