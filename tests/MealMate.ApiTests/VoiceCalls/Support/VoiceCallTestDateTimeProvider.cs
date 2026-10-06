using MealMate.Application.Common.Abstractions;

namespace MealMate.ApiTests.VoiceCalls.Support;

/// <summary>
/// Test clock implementing the production abstraction. Test assemblies only —
/// never registered into production DI.
/// </summary>
public sealed class VoiceCallTestDateTimeProvider : IDateTimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    public DateTime UtcNow => _now.UtcDateTime;

    public DateOnly TodayUtc => DateOnly.FromDateTime(_now.UtcDateTime);

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public void Set(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
}
