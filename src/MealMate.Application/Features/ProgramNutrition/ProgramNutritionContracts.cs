using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.ProgramNutrition;

public sealed record ProgramNutritionStandardDto(
    Guid Id,
    Guid ProgramId,
    string ProgramCode,
    string? ProgramShortCode,
    string ProgramNameAr,
    string ProgramNameEn,
    string MealCategory,
    decimal KcalMin,
    decimal KcalMax,
    decimal ProteinGMin,
    decimal ProteinGMax,
    string CarbMode,
    decimal CarbsGMin,
    decimal CarbsGMax,
    decimal FatGMin,
    decimal FatGMax,
    decimal FiberGMin,
    decimal CookedProteinGMin,
    decimal CookedProteinGMax,
    decimal CookedCarbsGMin,
    decimal CookedCarbsGMax,
    decimal? KetoDayNetCarbsLimit,
    string StandardVersion,
    bool IsActive);

public sealed record ProgramNutritionStandardInput(
    string MealCategory,
    decimal KcalMin,
    decimal KcalMax,
    decimal ProteinGMin,
    decimal ProteinGMax,
    string CarbMode,
    decimal CarbsGMin,
    decimal CarbsGMax,
    decimal FatGMin,
    decimal FatGMax,
    decimal FiberGMin,
    decimal CookedProteinGMin,
    decimal CookedProteinGMax,
    decimal CookedCarbsGMin,
    decimal CookedCarbsGMax,
    decimal? KetoDayNetCarbsLimit,
    string? StandardVersion = null,
    bool IsActive = true);

public sealed record ProgramDailyNutritionTargetDto(
    Guid ProgramId,
    string? ProgramShortCode,
    Guid BundleId,
    string BundleCode,
    int BreakfastCount,
    int LunchCount,
    int DinnerCount,
    int SnackCount,
    int MainCount,
    decimal SuggestedKcal,
    decimal SuggestedProteinG,
    decimal SuggestedCarbsG,
    decimal SuggestedFatG,
    decimal SuggestedFiberG,
    decimal KcalMin,
    decimal KcalMax,
    decimal ProteinGMin,
    decimal ProteinGMax,
    decimal CarbsGMin,
    decimal CarbsGMax,
    decimal FatGMin,
    decimal FatGMax,
    string CarbMode,
    decimal? KetoDayNetCarbsLimit,
    string StandardVersion);

public sealed record GetProgramNutritionStandardsQuery(Guid? ProgramId = null)
    : IQuery<IReadOnlyList<ProgramNutritionStandardDto>>;

public sealed record UpsertProgramNutritionStandardsCommand(
    Guid ProgramId,
    IReadOnlyList<ProgramNutritionStandardInput> Standards)
    : ICommand<IReadOnlyList<ProgramNutritionStandardDto>>;

public sealed record GetProgramDailyNutritionTargetQuery(Guid ProgramId, Guid BundleId)
    : IQuery<ProgramDailyNutritionTargetDto>;
