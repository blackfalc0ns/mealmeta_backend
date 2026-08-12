using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

/// <summary>
/// Seeds admin nutrition envelopes from MealMate Nutrition Spec / onboarding sheet «معايير البرامج».
/// </summary>
public static class ProgramNutritionStandardsSeeder
{
    public const string StandardVersion = "NUTRITION-v1.0";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var programs = await db.Programs.AsNoTracking()
            .Where(x => x.ShortCode != null)
            .ToDictionaryAsync(x => x.ShortCode!, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (programs.Count == 0)
        {
            logger.LogWarning("No programs with ShortCode found; skipped nutrition standards seed.");
            return;
        }

        var existing = await db.ProgramNutritionStandards.AsNoTracking()
            .Where(x => x.StandardVersion == StandardVersion)
            .Select(x => new { x.ProgramId, x.MealCategory })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(x => (x.ProgramId, x.MealCategory)).ToHashSet();

        var rows = SpecRows()
            .Where(x => programs.ContainsKey(x.ShortCode))
            .SelectMany(x => new[]
            {
                ToEntity(programs[x.ShortCode], "MAIN", x.Main, x.KetoDayLimit),
                ToEntity(programs[x.ShortCode], "SNACK", x.Snack, x.KetoDayLimit),
            })
            .Where(x => !existingKeys.Contains((x.ProgramId, x.MealCategory)))
            .ToList();

        if (rows.Count == 0) return;

        db.ProgramNutritionStandards.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} program nutrition standards ({Version}).", rows.Count, StandardVersion);
    }

    private static ProgramNutritionStandard ToEntity(
        Guid programId,
        string category,
        MacroEnvelope envelope,
        decimal? ketoDayLimit) => new()
    {
        ProgramId = programId,
        MealCategory = category,
        KcalMin = envelope.KcalMin,
        KcalMax = envelope.KcalMax,
        ProteinGMin = envelope.ProteinMin,
        ProteinGMax = envelope.ProteinMax,
        CarbMode = envelope.CarbMode,
        CarbsGMin = envelope.CarbsMin,
        CarbsGMax = envelope.CarbsMax,
        FatGMin = envelope.FatMin,
        FatGMax = envelope.FatMax,
        FiberGMin = envelope.FiberMin,
        CookedProteinGMin = envelope.CookedProteinMin,
        CookedProteinGMax = envelope.CookedProteinMax,
        CookedCarbsGMin = envelope.CookedCarbsMin,
        CookedCarbsGMax = envelope.CookedCarbsMax,
        KetoDayNetCarbsLimit = ketoDayLimit,
        StandardVersion = StandardVersion,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static IEnumerable<ProgramSpec> SpecRows() =>
    [
        new("WL",
            new(360, 480, 30, 42, "TOTAL", 30, 50, 10, 18, 5, 120, 170, 90, 150),
            new(140, 220, 10, 20, "TOTAL", 10, 28, 3, 9, 2, 0, 0, 0, 0),
            30),
        new("LS",
            new(450, 620, 30, 45, "TOTAL", 45, 70, 14, 24, 5, 140, 190, 130, 190),
            new(170, 300, 8, 20, "TOTAL", 18, 38, 5, 14, 2, 0, 0, 0, 0),
            30),
        new("AT",
            new(550, 760, 35, 52, "TOTAL", 65, 95, 14, 24, 5, 160, 220, 170, 250),
            new(220, 360, 15, 28, "TOTAL", 25, 50, 5, 13, 3, 0, 0, 0, 0),
            30),
        new("MG",
            new(650, 850, 40, 60, "TOTAL", 75, 110, 16, 28, 5, 180, 240, 200, 300),
            new(280, 450, 20, 35, "TOTAL", 30, 60, 7, 17, 3, 0, 0, 0, 0),
            30),
        new("KT",
            new(500, 700, 28, 45, "NET", 2, 7, 38, 58, 4, 140, 200, 0, 0),
            new(150, 280, 8, 20, "NET", 0, 4, 12, 24, 2, 0, 0, 0, 0),
            30),
        new("LC",
            new(430, 620, 32, 48, "TOTAL", 15, 35, 20, 34, 5, 150, 210, 0, 80),
            new(150, 280, 10, 22, "TOTAL", 5, 15, 8, 18, 2, 0, 0, 0, 0),
            30),
    ];

    private sealed record MacroEnvelope(
        decimal KcalMin, decimal KcalMax,
        decimal ProteinMin, decimal ProteinMax,
        string CarbMode,
        decimal CarbsMin, decimal CarbsMax,
        decimal FatMin, decimal FatMax,
        decimal FiberMin,
        decimal CookedProteinMin, decimal CookedProteinMax,
        decimal CookedCarbsMin, decimal CookedCarbsMax);

    private sealed record ProgramSpec(string ShortCode, MacroEnvelope Main, MacroEnvelope Snack, decimal? KetoDayLimit);
}
