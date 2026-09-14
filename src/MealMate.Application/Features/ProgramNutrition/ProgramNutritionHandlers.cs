using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramNutrition;

public static class NutritionMealCategories
{
    public const string Main = "MAIN";
    public const string Snack = "SNACK";

    public static string FromSlot(string? slot)
    {
        var normalized = (slot ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "snack" or "سناك" ? Snack : Main;
    }

    public static bool IsValid(string? value)
        => string.Equals(value, Main, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Snack, StringComparison.OrdinalIgnoreCase);
}

public sealed class UpsertProgramNutritionStandardsCommandValidator : AbstractValidator<UpsertProgramNutritionStandardsCommand>
{
    public UpsertProgramNutritionStandardsCommandValidator()
    {
        RuleFor(x => x.ProgramId).NotEmpty();
        RuleFor(x => x.Standards).NotEmpty();
        RuleForEach(x => x.Standards).ChildRules(std =>
        {
            std.RuleFor(x => x.MealCategory).Must(NutritionMealCategories.IsValid)
                .WithMessage("MealCategory must be MAIN or SNACK.");
            std.RuleFor(x => x.CarbMode).Must(x =>
                    string.Equals(x, "TOTAL", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, "NET", StringComparison.OrdinalIgnoreCase))
                .WithMessage("CarbMode must be TOTAL or NET.");
            std.RuleFor(x => x.KcalMax).GreaterThanOrEqualTo(x => x.KcalMin);
            std.RuleFor(x => x.ProteinGMax).GreaterThanOrEqualTo(x => x.ProteinGMin);
            std.RuleFor(x => x.CarbsGMax).GreaterThanOrEqualTo(x => x.CarbsGMin);
            std.RuleFor(x => x.FatGMax).GreaterThanOrEqualTo(x => x.FatGMin);
        });
    }
}

public sealed class ProgramNutritionHandlers :
    IQueryHandler<GetProgramNutritionStandardsQuery, IReadOnlyList<ProgramNutritionStandardDto>>,
    ICommandHandler<UpsertProgramNutritionStandardsCommand, IReadOnlyList<ProgramNutritionStandardDto>>,
    IQueryHandler<GetProgramDailyNutritionTargetQuery, ProgramDailyNutritionTargetDto>
{
    private readonly IAppDbContext _db;

    public ProgramNutritionHandlers(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ProgramNutritionStandardDto>>> Handle(
        GetProgramNutritionStandardsQuery request,
        CancellationToken cancellationToken)
    {
        var query =
            from std in _db.ProgramNutritionStandards.AsNoTracking()
            join program in _db.Programs.AsNoTracking() on std.ProgramId equals program.Id
            where std.IsActive
            select new { std, program };

        if (request.ProgramId.HasValue)
            query = query.Where(x => x.std.ProgramId == request.ProgramId.Value);

        var rows = await query
            .OrderBy(x => x.program.SortOrder)
            .ThenBy(x => x.std.MealCategory)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => Map(x.std, x.program.Code, x.program.ShortCode, x.program.NameAr, x.program.NameEn))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<ProgramNutritionStandardDto>>> Handle(
        UpsertProgramNutritionStandardsCommand request,
        CancellationToken cancellationToken)
    {
        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ProgramId, cancellationToken);
        if (program is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program '{request.ProgramId}' was not found.");

        var version = request.Standards
            .Select(x => x.StandardVersion?.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? ProgramNutritionStandardsVersion.Current;

        var existing = await _db.ProgramNutritionStandards
            .Where(x => x.ProgramId == request.ProgramId && x.StandardVersion == version)
            .ToListAsync(cancellationToken);

        foreach (var input in request.Standards)
        {
            var category = input.MealCategory.Trim().ToUpperInvariant();
            var row = existing.FirstOrDefault(x =>
                string.Equals(x.MealCategory, category, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new ProgramNutritionStandard
                {
                    ProgramId = request.ProgramId,
                    MealCategory = category,
                    StandardVersion = version,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _db.ProgramNutritionStandards.Add(row);
                existing.Add(row);
            }

            row.MealCategory = category;
            row.KcalMin = input.KcalMin;
            row.KcalMax = input.KcalMax;
            row.ProteinGMin = input.ProteinGMin;
            row.ProteinGMax = input.ProteinGMax;
            row.CarbMode = input.CarbMode.Trim().ToUpperInvariant();
            row.CarbsGMin = input.CarbsGMin;
            row.CarbsGMax = input.CarbsGMax;
            row.FatGMin = input.FatGMin;
            row.FatGMax = input.FatGMax;
            row.FiberGMin = input.FiberGMin;
            row.CookedProteinGMin = input.CookedProteinGMin;
            row.CookedProteinGMax = input.CookedProteinGMax;
            row.CookedCarbsGMin = input.CookedCarbsGMin;
            row.CookedCarbsGMax = input.CookedCarbsGMax;
            row.KetoDayNetCarbsLimit = input.KetoDayNetCarbsLimit;
            row.StandardVersion = version;
            row.IsActive = input.IsActive;
            row.ModifiedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await Handle(new GetProgramNutritionStandardsQuery(request.ProgramId), cancellationToken);
    }

    public async Task<Result<ProgramDailyNutritionTargetDto>> Handle(
        GetProgramDailyNutritionTargetQuery request,
        CancellationToken cancellationToken)
    {
        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ProgramId, cancellationToken);
        if (program is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program '{request.ProgramId}' was not found.");

        var bundle = await _db.Bundles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.BundleId && x.ProgramId == request.ProgramId, cancellationToken);
        if (bundle is null)
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle '{request.BundleId}' was not found for this program.");

        var standards = await _db.ProgramNutritionStandards.AsNoTracking()
            .Where(x => x.ProgramId == request.ProgramId && x.IsActive)
            .ToListAsync(cancellationToken);

        var target = ComputeDailyTarget(program, bundle, standards);
        if (target is null)
            return Error.Validation("nutrition.standards_missing", "Active MAIN/SNACK nutrition standards are required for this program.");

        return target;
    }

    public static ProgramDailyNutritionTargetDto? ComputeDailyTarget(
        Program program,
        Bundle bundle,
        IReadOnlyList<ProgramNutritionStandard> standards)
    {
        var main = standards.FirstOrDefault(x => x.MealCategory == NutritionMealCategories.Main);
        var snack = standards.FirstOrDefault(x => x.MealCategory == NutritionMealCategories.Snack);
        if (main is null || snack is null)
            return null;

        var mainCount = Math.Max(0, bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount);
        if (mainCount == 0 && bundle.MealsPerDay > 0)
            mainCount = bundle.MealsPerDay;
        var snackCount = Math.Max(0, bundle.SnackCount);

        static decimal Mid(decimal min, decimal max) => Math.Round((min + max) / 2m, 1);

        var suggestedKcal = Mid(main.KcalMin, main.KcalMax) * mainCount + Mid(snack.KcalMin, snack.KcalMax) * snackCount;
        var suggestedProtein = Mid(main.ProteinGMin, main.ProteinGMax) * mainCount + Mid(snack.ProteinGMin, snack.ProteinGMax) * snackCount;
        var suggestedCarbs = Mid(main.CarbsGMin, main.CarbsGMax) * mainCount + Mid(snack.CarbsGMin, snack.CarbsGMax) * snackCount;
        var suggestedFat = Mid(main.FatGMin, main.FatGMax) * mainCount + Mid(snack.FatGMin, snack.FatGMax) * snackCount;
        var suggestedFiber = main.FiberGMin * mainCount + snack.FiberGMin * snackCount;

        return new ProgramDailyNutritionTargetDto(
            program.Id,
            program.ShortCode,
            bundle.Id,
            bundle.Code,
            bundle.BreakfastCount,
            bundle.LunchCount,
            bundle.DinnerCount,
            snackCount,
            mainCount,
            suggestedKcal,
            suggestedProtein,
            suggestedCarbs,
            suggestedFat,
            suggestedFiber,
            main.KcalMin * mainCount + snack.KcalMin * snackCount,
            main.KcalMax * mainCount + snack.KcalMax * snackCount,
            main.ProteinGMin * mainCount + snack.ProteinGMin * snackCount,
            main.ProteinGMax * mainCount + snack.ProteinGMax * snackCount,
            main.CarbsGMin * mainCount + snack.CarbsGMin * snackCount,
            main.CarbsGMax * mainCount + snack.CarbsGMax * snackCount,
            main.FatGMin * mainCount + snack.FatGMin * snackCount,
            main.FatGMax * mainCount + snack.FatGMax * snackCount,
            main.CarbMode,
            main.KetoDayNetCarbsLimit ?? snack.KetoDayNetCarbsLimit,
            main.StandardVersion);
    }

    private static ProgramNutritionStandardDto Map(
        ProgramNutritionStandard std,
        string code,
        string? shortCode,
        string nameAr,
        string nameEn) => new(
        std.Id,
        std.ProgramId,
        code,
        shortCode,
        nameAr,
        nameEn,
        std.MealCategory,
        std.KcalMin,
        std.KcalMax,
        std.ProteinGMin,
        std.ProteinGMax,
        std.CarbMode,
        std.CarbsGMin,
        std.CarbsGMax,
        std.FatGMin,
        std.FatGMax,
        std.FiberGMin,
        std.CookedProteinGMin,
        std.CookedProteinGMax,
        std.CookedCarbsGMin,
        std.CookedCarbsGMax,
        std.KetoDayNetCarbsLimit,
        std.StandardVersion,
        std.IsActive);
}

public static class ProgramNutritionStandardsVersion
{
    public const string Current = "NUTRITION-v1.0";
}

/// <summary>Shared meal-vs-standard checker used by meal create/update/approve flows.</summary>
public static class MealNutritionStandardValidator
{
    public static IReadOnlyList<string> Evaluate(
        ProgramNutritionStandard standard,
        string? programShortCode,
        decimal? calories,
        decimal? proteinG,
        decimal? carbsG,
        decimal? netCarbsG,
        decimal? fatG,
        decimal? fiberG)
    {
        var key = $"{programShortCode ?? "PROG"}|{standard.MealCategory}";
        var issues = new List<string>();

        void Range(string label, decimal? value, decimal min, decimal max)
        {
            if (value is null)
            {
                issues.Add($"{key}: {label} is required.");
                return;
            }

            if (value < min || value > max)
                issues.Add($"{key}: {label} {value} is outside {min}-{max}.");
        }

        Range("kcal", calories, standard.KcalMin, standard.KcalMax);
        Range("protein_g", proteinG, standard.ProteinGMin, standard.ProteinGMax);
        Range("fat_g", fatG, standard.FatGMin, standard.FatGMax);

        if (string.Equals(standard.CarbMode, "NET", StringComparison.OrdinalIgnoreCase))
            Range("net_carbs_g", netCarbsG ?? carbsG, standard.CarbsGMin, standard.CarbsGMax);
        else
            Range("carbs_g", carbsG, standard.CarbsGMin, standard.CarbsGMax);

        if (fiberG is null)
            issues.Add($"{key}: fiber_g is required.");
        else if (fiberG < standard.FiberGMin)
            issues.Add($"{key}: fiber_g {fiberG} is below minimum {standard.FiberGMin}.");

        return issues;
    }
}
