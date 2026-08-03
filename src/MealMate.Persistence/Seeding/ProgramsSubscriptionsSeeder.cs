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
            return; // Already seeded
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

        logger.LogInformation("Successfully seeded 3 Programs and 3 Bundles for M02.");
    }
}
