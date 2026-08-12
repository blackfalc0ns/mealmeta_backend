namespace MealMate.Application.Features.ProgramNutrition;

/// <summary>
/// Default MAIN/SNACK envelopes from onboarding sheet «معايير البرامج» (NUTRITION-v1.0).
/// </summary>
public static class ProgramNutritionDefaults
{
    public const string StandardVersion = "NUTRITION-v1.0";

    public static readonly IReadOnlyList<string> ShortCodes = ["WL", "LS", "AT", "MG", "KT", "LC"];

    public static bool TryGet(string? shortCode, out IReadOnlyList<ProgramNutritionStandardInput> standards)
    {
        standards = Array.Empty<ProgramNutritionStandardInput>();
        if (string.IsNullOrWhiteSpace(shortCode)) return false;
        var key = shortCode.Trim().ToUpperInvariant();
        if (!Catalog.TryGetValue(key, out var row)) return false;
        standards =
        [
            ToInput("MAIN", row.Main, row.KetoDayLimit),
            ToInput("SNACK", row.Snack, row.KetoDayLimit),
        ];
        return true;
    }

    public static ProgramNutritionStandardInput ToInput(
        string category,
        MacroEnvelope envelope,
        decimal? ketoDayLimit) => new(
        MealCategory: category,
        KcalMin: envelope.KcalMin,
        KcalMax: envelope.KcalMax,
        ProteinGMin: envelope.ProteinMin,
        ProteinGMax: envelope.ProteinMax,
        CarbMode: envelope.CarbMode,
        CarbsGMin: envelope.CarbsMin,
        CarbsGMax: envelope.CarbsMax,
        FatGMin: envelope.FatMin,
        FatGMax: envelope.FatMax,
        FiberGMin: envelope.FiberMin,
        CookedProteinGMin: envelope.CookedProteinMin,
        CookedProteinGMax: envelope.CookedProteinMax,
        CookedCarbsGMin: envelope.CookedCarbsMin,
        CookedCarbsGMax: envelope.CookedCarbsMax,
        KetoDayNetCarbsLimit: ketoDayLimit,
        StandardVersion: StandardVersion,
        IsActive: true);

    private static readonly Dictionary<string, ProgramSpec> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WL"] = new(
            new(360, 480, 30, 42, "TOTAL", 30, 50, 10, 18, 5, 120, 170, 90, 150),
            new(140, 220, 10, 20, "TOTAL", 10, 28, 3, 9, 2, 0, 0, 0, 0),
            30),
        ["LS"] = new(
            new(450, 620, 30, 45, "TOTAL", 45, 70, 14, 24, 5, 140, 190, 130, 190),
            new(170, 300, 8, 20, "TOTAL", 18, 38, 5, 14, 2, 0, 0, 0, 0),
            30),
        ["AT"] = new(
            new(550, 760, 35, 52, "TOTAL", 65, 95, 14, 24, 5, 160, 220, 170, 250),
            new(220, 360, 15, 28, "TOTAL", 25, 50, 5, 13, 3, 0, 0, 0, 0),
            30),
        ["MG"] = new(
            new(650, 850, 40, 60, "TOTAL", 75, 110, 16, 28, 5, 180, 240, 200, 300),
            new(280, 450, 20, 35, "TOTAL", 30, 60, 7, 17, 3, 0, 0, 0, 0),
            30),
        ["KT"] = new(
            new(500, 700, 28, 45, "NET", 2, 7, 38, 58, 4, 140, 200, 0, 0),
            new(150, 280, 8, 20, "NET", 0, 4, 12, 24, 2, 0, 0, 0, 0),
            30),
        ["LC"] = new(
            new(430, 620, 32, 48, "TOTAL", 15, 35, 20, 34, 5, 150, 210, 0, 80),
            new(150, 280, 10, 22, "TOTAL", 5, 15, 8, 18, 2, 0, 0, 0, 0),
            30),
    };

    public sealed record MacroEnvelope(
        decimal KcalMin, decimal KcalMax,
        decimal ProteinMin, decimal ProteinMax,
        string CarbMode,
        decimal CarbsMin, decimal CarbsMax,
        decimal FatMin, decimal FatMax,
        decimal FiberMin,
        decimal CookedProteinMin, decimal CookedProteinMax,
        decimal CookedCarbsMin, decimal CookedCarbsMax);

    private sealed record ProgramSpec(MacroEnvelope Main, MacroEnvelope Snack, decimal? KetoDayLimit);
}
