namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public readonly record struct ReviewWindow(DateTime StartUtc, DateTime EndUtc)
{
    public bool IsOpen(DateTime nowUtc) => nowUtc >= StartUtc && nowUtc < EndUtc;
}
