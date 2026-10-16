using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class ProgramsSubscriptionsSeeder
{
    public static async Task SeedProgramsAndBundlesAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!await db.Programs.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seeding default M02 Programs & Bundles...");
            await SeedFreshCatalogAsync(db, cancellationToken);
            logger.LogInformation("Successfully seeded core programs and bundles for M02.");
        }

        await EnsureProgramShortCodesAndExtrasAsync(db, logger, cancellationToken);
        await EnsureProgramDisplayContentAsync(db, logger, cancellationToken);
        await EnsureDefaultBundleVariantsAsync(db, logger, cancellationToken);
        await EnsureBundleSlotCountsAsync(db, logger, cancellationToken);
        await ProgramNutritionStandardsSeeder.SeedAsync(db, logger, cancellationToken);
        await FoodSafetyCatalogSeeder.SeedAsync(db, cancellationToken);
        await ReceiptTemplatesSeeder.SeedAsync(db, logger, cancellationToken);
    }

    private static async Task EnsureProgramDisplayContentAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var defaults = new Dictionary<string, (string DescriptionAr, string DescriptionEn, string[] KeywordsAr, string[] KeywordsEn)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROG_KETO"] = (
                "برنامج منخفض الكربوهيدرات يعتمد على الدهون الصحية لمساعدتك على الالتزام بنمط الكيتو.",
                "A low-carbohydrate program built around healthy fats to help you follow a keto lifestyle.",
                ["كربوهيدرات منخفضة", "دهون صحية"],
                ["Low carb", "Healthy fats"]),
            ["PROG_BALANCED"] = (
                "وجبات يومية متوازنة تجمع البروتين والكربوهيدرات والدهون بنسب مناسبة لنمط حياة صحي.",
                "Balanced daily meals combining protein, carbohydrates and fats for a healthy lifestyle.",
                ["تغذية متوازنة", "طاقة يومية"],
                ["Balanced nutrition", "Daily energy"]),
            ["PROG_ATHLETE"] = (
                "وجبات محسوبة لدعم النشاط الرياضي والطاقة والتعافي بعد التمرين.",
                "Calculated meals designed to support athletic performance, energy and workout recovery.",
                ["أداء رياضي", "دعم التعافي"],
                ["Athletic performance", "Recovery support"]),
            ["PROG_WEIGHT_LOSS"] = (
                "وجبات محسوبة السعرات تساعدك على خسارة الوزن تدريجيًا مع الحفاظ على تغذية متوازنة.",
                "Calorie-controlled meals that support gradual weight loss while maintaining balanced nutrition.",
                ["سعرات محسوبة", "نتائج مستدامة"],
                ["Calorie controlled", "Sustainable results"]),
            ["PROG_MUSCLE_GAIN"] = (
                "وجبات غنية بالبروتين وموزعة لدعم بناء العضلات والتعافي وتحقيق أهداف التمرين.",
                "Protein-rich meals distributed to support muscle growth, recovery and training goals.",
                ["بروتين أعلى", "بناء العضلات"],
                ["Higher protein", "Muscle growth"]),
            ["PROG_LOW_CARB"] = (
                "خطة مرنة تقلل الكربوهيدرات مع الحفاظ على وجبات متنوعة ومتوازنة وسهلة الالتزام.",
                "A flexible lower-carbohydrate plan with varied, balanced meals that are easy to follow.",
                ["كربوهيدرات أقل", "خيارات مرنة"],
                ["Lower carb", "Flexible choices"]),
        };

        var programs = await db.Programs.Where(x => defaults.Keys.Contains(x.Code)).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var program in programs)
        {
            var content = defaults[program.Code];
            if (string.IsNullOrWhiteSpace(program.DescriptionAr)) { program.DescriptionAr = content.DescriptionAr; changed = true; }
            if (string.IsNullOrWhiteSpace(program.DescriptionEn)) { program.DescriptionEn = content.DescriptionEn; changed = true; }
            if (program.KeywordsAr is not { Length: > 0 }) { program.KeywordsAr = content.KeywordsAr; changed = true; }
            if (program.KeywordsEn is not { Length: > 0 }) { program.KeywordsEn = content.KeywordsEn; changed = true; }
        }

        if (!changed) return;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Backfilled customer-facing descriptions and keywords for nutrition programs.");
    }

    private static async Task SeedFreshCatalogAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var keto = NewProgram("PROG_KETO", "KT", "كيتو", "Keto", 1);
        var balanced = NewProgram("PROG_BALANCED", "LS", "نمط حياة متوازن", "Lifestyle", 2);
        var athlete = NewProgram("PROG_ATHLETE", "AT", "الأداء الرياضي", "Athletic", 3);
        var weightLoss = NewProgram("PROG_WEIGHT_LOSS", "WL", "خسارة الوزن", "Weight Loss", 4);
        var muscle = NewProgram("PROG_MUSCLE_GAIN", "MG", "بناء العضلات", "Muscle Gain (Bodybuilding)", 5);
        var lowCarb = NewProgram("PROG_LOW_CARB", "LC", "قليل الكربوهيدرات", "Low Carb", 6);

        db.Programs.AddRange(keto, balanced, athlete, weightLoss, muscle, lowCarb);
        await db.SaveChangesAsync(cancellationToken);

        db.Bundles.AddRange(
            NewBundle(keto.Id, "BND_KETO_FULL", "باقة الكيتو الشاملة", "Full Keto Bundle", 3, 1, 1, 1, 1, 1),
            NewBundle(balanced.Id, "BND_BALANCED_STD", "الباقة المتوازنة", "Standard Balanced Bundle", 2, 0, 1, 1, 1, 2),
            NewBundle(athlete.Id, "BND_ATHLETE_MAX", "باقة الرياضيين القصوى", "Max Athlete Bundle", 3, 1, 1, 1, 0, 3),
            NewBundle(weightLoss.Id, "PKG-BL1S", "ريوق + غداء + سناك", "Breakfast + Lunch + Snack", 2, 1, 1, 0, 1, 1),
            NewBundle(weightLoss.Id, "PKG-LD2S", "غداء + عشاء + ٢ سناك", "Lunch + Dinner + 2 Snacks", 2, 0, 1, 1, 2, 2),
            NewBundle(weightLoss.Id, "PKG-BLD2S", "ريوق + غداء + عشاء + ٢ سناك", "Breakfast + Lunch + Dinner + 2 Snacks", 3, 1, 1, 1, 2, 3));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureProgramShortCodesAndExtrasAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var programs = await db.Programs.ToListAsync(cancellationToken);
        var byCode = programs.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        void SetShort(string code, string shortCode)
        {
            if (!byCode.TryGetValue(code, out var program)) return;
            if (string.Equals(program.ShortCode, shortCode, StringComparison.OrdinalIgnoreCase)) return;
            program.ShortCode = shortCode;
            changed = true;
        }

        SetShort("PROG_KETO", "KT");
        SetShort("PROG_BALANCED", "LS");
        SetShort("PROG_ATHLETE", "AT");
        SetShort("PROG_WEIGHT_LOSS", "WL");
        SetShort("PROG_MUSCLE_GAIN", "MG");
        SetShort("PROG_LOW_CARB", "LC");

        var extras = new (string Code, string Short, string NameAr, string NameEn, int Sort)[]
        {
            ("PROG_WEIGHT_LOSS", "WL", "خسارة الوزن", "Weight Loss", 4),
            ("PROG_MUSCLE_GAIN", "MG", "بناء العضلات", "Muscle Gain (Bodybuilding)", 5),
            ("PROG_LOW_CARB", "LC", "قليل الكربوهيدرات", "Low Carb", 6),
        };

        foreach (var extra in extras)
        {
            if (byCode.ContainsKey(extra.Code)) continue;
            var program = NewProgram(extra.Code, extra.Short, extra.NameAr, extra.NameEn, extra.Sort);
            db.Programs.Add(program);
            byCode[extra.Code] = program;
            changed = true;
        }

        if (!changed) return;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Ensured program short codes and extra nutrition programs.");
    }

    private static async Task EnsureDefaultBundleVariantsAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var programs = await db.Programs.AsNoTracking()
            .Where(x => x.Code == "PROG_KETO" || x.Code == "PROG_BALANCED" || x.Code == "PROG_ATHLETE"
                || x.Code == "PROG_WEIGHT_LOSS" || x.Code == "PROG_MUSCLE_GAIN" || x.Code == "PROG_LOW_CARB")
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);

        var existingCodes = await db.Bundles.AsNoTracking()
            .Select(x => x.Code)
            .ToHashSetAsync(cancellationToken);

        var variants = new (string ProgramCode, string BundleCode, string NameAr, string NameEn, int Meals, int B, int L, int D, int S, int Sort)[]
        {
            ("PROG_KETO", "BND_KETO_LITE", "باقة كيتو خفيفة (وجبة رئيسية + سناك)", "Lite Keto Bundle (1 Meal + Snack)", 1, 0, 1, 0, 1, 2),
            ("PROG_KETO", "BND_KETO_STANDARD", "باقة كيتو متوازنة (وجبتان + سناك)", "Standard Keto Bundle (2 Meals + Snack)", 2, 0, 1, 1, 1, 3),
            ("PROG_BALANCED", "BND_BALANCED_LITE", "باقة متوازنة خفيفة (وجبة رئيسية + سناك)", "Lite Balanced Bundle (1 Meal + Snack)", 1, 0, 1, 0, 1, 1),
            ("PROG_BALANCED", "BND_BALANCED_FULL", "باقة متوازنة كاملة (3 وجبات + سناك)", "Full Balanced Bundle (3 Meals + Snack)", 3, 1, 1, 1, 1, 3),
            ("PROG_ATHLETE", "BND_ATHLETE_STANDARD", "باقة رياضية متوازنة (وجبتان + سناك)", "Standard Athlete Bundle (2 Meals + Snack)", 2, 0, 1, 1, 1, 1),
            ("PROG_ATHLETE", "BND_ATHLETE_ELITE", "باقة رياضية مكثفة (3 وجبات + سناك)", "Elite Athlete Bundle (3 Meals + Snack)", 3, 1, 1, 1, 1, 2),
            ("PROG_WEIGHT_LOSS", "PKG-BL1S", "ريوق + غداء + سناك", "Breakfast + Lunch + Snack", 2, 1, 1, 0, 1, 1),
            ("PROG_WEIGHT_LOSS", "PKG-LD2S", "غداء + عشاء + ٢ سناك", "Lunch + Dinner + 2 Snacks", 2, 0, 1, 1, 2, 2),
            ("PROG_WEIGHT_LOSS", "PKG-BLD2S", "ريوق + غداء + عشاء + ٢ سناك", "Breakfast + Lunch + Dinner + 2 Snacks", 3, 1, 1, 1, 2, 3),
            ("PROG_MUSCLE_GAIN", "PKG-BLD2S-MG", "ريوق + غداء + عشاء + ٢ سناك", "Breakfast + Lunch + Dinner + 2 Snacks", 3, 1, 1, 1, 2, 1),
            ("PROG_LOW_CARB", "PKG-BL1S-LC", "ريوق + غداء + سناك", "Breakfast + Lunch + Snack", 2, 1, 1, 0, 1, 1),
        };

        var additions = variants
            .Where(x => programs.ContainsKey(x.ProgramCode) && !existingCodes.Contains(x.BundleCode))
            .Select(x => NewBundle(programs[x.ProgramCode], x.BundleCode, x.NameAr, x.NameEn, x.Meals, x.B, x.L, x.D, x.S, x.Sort))
            .ToList();

        if (additions.Count == 0) return;

        db.Bundles.AddRange(additions);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Added {Count} missing default program bundle variants.", additions.Count);
    }

    private static async Task EnsureBundleSlotCountsAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var bundles = await db.Bundles.Where(x =>
            x.BreakfastCount == 0 && x.LunchCount == 0 && x.DinnerCount == 0 && x.SnackCount == 0).ToListAsync(cancellationToken);
        if (bundles.Count == 0) return;

        foreach (var bundle in bundles)
        {
            // Heuristic for legacy rows that only had MealsPerDay.
            bundle.LunchCount = Math.Max(bundle.MealsPerDay, 1);
            bundle.SnackCount = bundle.Code.Contains("SNACK", StringComparison.OrdinalIgnoreCase)
                || bundle.NameEn.Contains("Snack", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            if (bundle.MealsPerDay >= 3)
            {
                bundle.BreakfastCount = 1;
                bundle.LunchCount = 1;
                bundle.DinnerCount = 1;
            }
            else if (bundle.MealsPerDay == 2)
            {
                bundle.LunchCount = 1;
                bundle.DinnerCount = 1;
                bundle.BreakfastCount = 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Backfilled slot counts on {Count} bundles.", bundles.Count);
    }

    private static Program NewProgram(string code, string shortCode, string nameAr, string nameEn, int sort) => new()
    {
        Code = code,
        ShortCode = shortCode,
        NameAr = nameAr,
        NameEn = nameEn,
        Status = ProgramStatus.Active,
        IsActive = true,
        SortOrder = sort,
    };

    private static Bundle NewBundle(
        Guid programId,
        string code,
        string nameAr,
        string nameEn,
        int mealsPerDay,
        int breakfast,
        int lunch,
        int dinner,
        int snack,
        int sort) => new()
    {
        ProgramId = programId,
        Code = code,
        NameAr = nameAr,
        NameEn = nameEn,
        DescriptionAr = nameAr,
        DescriptionEn = nameEn,
        MealsPerDay = mealsPerDay,
        BreakfastCount = breakfast,
        LunchCount = lunch,
        DinnerCount = dinner,
        SnackCount = snack,
        Status = ProgramStatus.Active,
        IsActive = true,
        SortOrder = sort,
    };
}
