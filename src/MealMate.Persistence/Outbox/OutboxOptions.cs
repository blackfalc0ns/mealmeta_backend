namespace MealMate.Persistence.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;
}
