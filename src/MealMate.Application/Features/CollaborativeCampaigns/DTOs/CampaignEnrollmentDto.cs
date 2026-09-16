using MealMate.Domain.CollaborativeCampaigns.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

public sealed record CampaignEnrollmentDto(
    Guid Id,
    Guid CampaignId,
    Guid RestaurantId,
    CampaignRestaurantParticipationStatus ParticipationStatus,
    DateTime RequestedAtUtc,
    int? DedicatedDailyCapacity,
    string? DeclineReason);
