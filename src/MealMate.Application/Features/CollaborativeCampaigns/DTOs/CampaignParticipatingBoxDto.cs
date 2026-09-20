namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

public sealed record CampaignProgramDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? Code);

public sealed record CampaignParticipatingBoxDto(
    Guid BundleId,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    string BoxNameAr,
    string BoxNameEn,
    int MealsPerDay,
    int BreakfastCount,
    int LunchCount,
    int DinnerCount,
    int SnackCount);
