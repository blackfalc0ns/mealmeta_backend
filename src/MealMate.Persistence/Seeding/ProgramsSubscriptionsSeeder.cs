using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class ProgramsSubscriptionsSeeder
{
    public static async Task SeedProgramsAndBundlesAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await db.Programs.AnyAsync(cancellationToken))
        {
            await EnsureDefaultBundleVariantsAsync(db, logger, cancellationToken);
            return;
        }

        logger.LogInformation("Seeding default M02 Programs & Bundles...");

        // 1. Programs
        var ketoProgram = new Program
        {
            Code = "PROG_KETO",
            NameAr = "برنامج الكيتو المتكامل",
            NameEn = "Keto Complete Program",
            DescriptionAr = "برنامج خالي من الكربوهيدرات وغني بالدهون الصحية والبروتين للرشاقة والتحكم بالوزن.",
            DescriptionEn = "Low-carb, high-fat nutrition program tailored for weight control and keto lifestyle.",
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 1,
        };

        var balancedProgram = new Program
        {
            Code = "PROG_BALANCED",
            NameAr = "البرنامج المتوازن",
            NameEn = "Balanced Nutrition Program",
            DescriptionAr = "وجبات صحية متكاملة العناصر تناسب أسلوب الحياة اليومي والصحة العامة.",
            DescriptionEn = "Balanced daily meals with controlled macros for healthy everyday lifestyle.",
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 2,
        };

        var athleteProgram = new Program
        {
            Code = "PROG_ATHLETE",
            NameAr = "برنامج الرياضيين واللياقة",
            NameEn = "Athlete & Fitness Program",
            DescriptionAr = "برنامج عالي البروتين ومحسوب السعرات لبناء العضلات والأداء الرياضي.",
            DescriptionEn = "High-protein muscle building program engineered for athletes.",
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 3,
        };

        db.Programs.AddRange(ketoProgram, balancedProgram, athleteProgram);
        await db.SaveChangesAsync(cancellationToken);

        // 2. Bundles
        var bndKetoFull = new Bundle
        {
            ProgramId = ketoProgram.Id,
            Code = "BND_KETO_FULL",
            NameAr = "باقة الكيتو الشاملة (3 وجبات + سناك)",
            NameEn = "Full Keto Bundle (3 Meals + Snack)",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 1,
        };

        var bndBalancedStd = new Bundle
        {
            ProgramId = balancedProgram.Id,
            Code = "BND_BALANCED_STD",
            NameAr = "الباقة المتوازنة (وجبتين + سناك)",
            NameEn = "Standard Balanced Bundle (2 Meals + Snack)",
            MealsPerDay = 2,
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 2,
        };

        var bndAthleteMax = new Bundle
        {
            ProgramId = athleteProgram.Id,
            Code = "BND_ATHLETE_MAX",
            NameAr = "باقة الرياضيين القصوى (3 وجبات رئيسية)",
            NameEn = "Max Athlete Bundle (3 High Protein Meals)",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true,
            SortOrder = 3,
        };

        db.Bundles.AddRange(bndKetoFull, bndBalancedStd, bndAthleteMax);
        await db.SaveChangesAsync(cancellationToken);

        await EnsureDefaultBundleVariantsAsync(db, logger, cancellationToken);

        logger.LogInformation("Successfully seeded 3 Programs and 3 Bundles for M02.");
    }

    private static async Task EnsureDefaultBundleVariantsAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var programs = await db.Programs.AsNoTracking()
            .Where(x => x.Code == "PROG_KETO" || x.Code == "PROG_BALANCED" || x.Code == "PROG_ATHLETE")
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);

        var existingCodes = await db.Bundles.AsNoTracking()
            .Select(x => x.Code)
            .ToHashSetAsync(cancellationToken);

        var variants = new[]
        {
            ("PROG_KETO", "BND_KETO_LITE", "باقة كيتو خفيفة (وجبة رئيسية + سناك)", "Lite Keto Bundle (1 Meal + Snack)", 1, 2),
            ("PROG_KETO", "BND_KETO_STANDARD", "باقة كيتو متوازنة (وجبتان + سناك)", "Standard Keto Bundle (2 Meals + Snack)", 2, 3),
            ("PROG_BALANCED", "BND_BALANCED_LITE", "باقة متوازنة خفيفة (وجبة رئيسية + سناك)", "Lite Balanced Bundle (1 Meal + Snack)", 1, 1),
            ("PROG_BALANCED", "BND_BALANCED_FULL", "باقة متوازنة كاملة (3 وجبات + سناك)", "Full Balanced Bundle (3 Meals + Snack)", 3, 3),
            ("PROG_ATHLETE", "BND_ATHLETE_STANDARD", "باقة رياضية متوازنة (وجبتان + سناك)", "Standard Athlete Bundle (2 Meals + Snack)", 2, 1),
            ("PROG_ATHLETE", "BND_ATHLETE_ELITE", "باقة رياضية مكثفة (3 وجبات + سناك)", "Elite Athlete Bundle (3 Meals + Snack)", 3, 2),
        };

        var additions = variants
            .Where(x => programs.TryGetValue(x.Item1, out _) && !existingCodes.Contains(x.Item2))
            .Select(x => new Bundle
            {
                ProgramId = programs[x.Item1],
                Code = x.Item2,
                NameAr = x.Item3,
                NameEn = x.Item4,
                MealsPerDay = x.Item5,
                Status = ProgramStatus.Active,
                IsActive = true,
                SortOrder = x.Item6,
            })
            .ToList();

        if (additions.Count == 0) return;

        db.Bundles.AddRange(additions);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Added {Count} missing default program bundle variants.", additions.Count);
    }
}
