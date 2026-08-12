using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record BundleDto(
    Guid Id,
    Guid ProgramId,
    string Code,
    string NameAr,
    string NameEn,
    int MealsPerDay,
    int BreakfastCount,
    int LunchCount,
    int DinnerCount,
    int SnackCount,
    string? Description,
    ProgramStatus Status,
    string? StatusReason,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAtUtc);

public sealed record ProgramDto(
    Guid Id,
    string Code,
    string? ShortCode,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    ProgramStatus Status,
    string? StatusReason,
    bool IsActive,
    int SortOrder,
    Guid? CurrentVersionId,
    DateTime CreatedAtUtc,
    IReadOnlyList<BundleDto> Bundles);

// Program Commands
public sealed record CreateProgramCommand(
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? ShortCode = null,
    int SortOrder = 0,
    IReadOnlyList<ProgramNutritionStandardInput>? NutritionStandards = null) : ICommand<ProgramDto>;

public sealed record UpdateProgramCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int SortOrder,
    string? ShortCode = null) : ICommand<ProgramDto>;

public sealed record ChangeProgramStatusCommand(
    Guid Id,
    ProgramStatus Status,
    string? Reason = null) : ICommand<ProgramDto>;

// Program Queries
public sealed record GetProgramByIdQuery(Guid Id) : IQuery<ProgramDto>;

public sealed record GetProgramsListQuery(
    ProgramStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ProgramDto>>;

// Bundle Commands
public sealed record CreateBundleCommand(
    Guid ProgramId,
    string Code,
    string NameAr,
    string NameEn,
    int BreakfastCount = 0,
    int LunchCount = 1,
    int DinnerCount = 0,
    int SnackCount = 0,
    string? Description = null,
    int SortOrder = 0,
    int? MealsPerDay = null) : ICommand<BundleDto>;

public sealed record UpdateBundleCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    int BreakfastCount,
    int LunchCount,
    int DinnerCount,
    int SnackCount,
    string? Description,
    int SortOrder,
    int? MealsPerDay = null) : ICommand<BundleDto>;

public sealed record ChangeBundleStatusCommand(
    Guid Id,
    ProgramStatus Status,
    string? Reason = null) : ICommand<BundleDto>;

// Bundle Queries
public sealed record GetBundleByIdQuery(Guid Id) : IQuery<BundleDto>;

public sealed record GetBundlesListQuery(
    Guid? ProgramId = null,
    ProgramStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<BundleDto>>;
