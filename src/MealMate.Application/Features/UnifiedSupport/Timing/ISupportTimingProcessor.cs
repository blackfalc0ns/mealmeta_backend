namespace MealMate.Application.Features.UnifiedSupport.Timing;

public sealed record SupportTimingTickResult(
    int ProcessedCasesCount,
    int FirstResponseBreachesCount,
    int ResolutionBreachesCount,
    int EscalatedCount,
    int UnroutedCount);

public interface ISupportTimingProcessor
{
    Task<SupportTimingTickResult> ProcessTimingTickAsync(CancellationToken ct = default);
}
