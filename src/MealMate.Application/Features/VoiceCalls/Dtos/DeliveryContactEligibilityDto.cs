namespace MealMate.Application.Features.VoiceCalls.Dtos;

/// <summary>
/// Server-computed contact eligibility for a delivery stop. The UI never decides
/// contact availability locally; it renders this verdict.
/// </summary>
public sealed record DeliveryContactEligibilityDto(
    bool CanInitiate,
    string ReasonCode,
    double? DistanceMeters,
    double? LocationAgeSeconds,
    bool CanHold,
    bool CanRetry,
    bool CanRevealPhone,
    Guid? ContactCaseId);
