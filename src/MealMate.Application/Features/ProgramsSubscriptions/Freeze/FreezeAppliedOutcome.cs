namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public sealed record FreezeAppliedOutcome(IReadOnlyList<DateOnly> FrozenDates, IReadOnlyList<DateOnly> ReplacementDates, DateOnly EndDateAfter)
{
    public const string Prefix = "subscription.freeze.outcome.";
}
