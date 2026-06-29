namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Abstracts the system clock so time-sensitive rules (72h lock, 24h confirmation,
/// cancellation windows) are deterministic and testable. Always UTC.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }

    DateOnly TodayUtc { get; }
}
