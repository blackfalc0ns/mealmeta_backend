using MealMate.Domain.Enums;

namespace MealMate.ApiTests.VoiceCalls.Support;

/// <summary>
/// Seed knobs for the isolated voice-call test host. Defaults describe a driver
/// currently delivering an eligible picked-up stop to a confirmed subscription day.
/// Test-only: never referenced by production DI.
/// </summary>
public sealed record VoiceCallTestSeed(
    TripStopStatus StopStatus = TripStopStatus.PickedUp,
    TripStatus TripStatus = TripStatus.InProgress,
    bool ActiveDriverAssignment = true,
    double DistanceMeters = 2500,
    double? AccuracyMeters = 20,
    int LocationAgeSeconds = 5,
    bool EnableDatabaseRetries = true);
