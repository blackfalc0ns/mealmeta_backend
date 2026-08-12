using System.Text.Json;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record SubscriptionFreezePolicyDocument(
    int DefaultDurationDays = 7,
    int StartDelayDays = 3,
    bool ExtendSubscriptionEnd = true);

public static class SubscriptionFreezePolicyDefaults
{
    public const string Key = "programs_subscriptions.freeze_policy";
    public static readonly SubscriptionFreezePolicyDocument Document = new();
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SubscriptionFreezePolicyDocument Deserialize(string? json)
    {
        try { return JsonSerializer.Deserialize<SubscriptionFreezePolicyDocument>(json ?? string.Empty, JsonOptions) ?? Document; }
        catch (JsonException) { return Document; }
    }

    public static string? Validate(SubscriptionFreezePolicyDocument document) =>
        document.DefaultDurationDays is < 1 or > 30 ? "Default freeze duration must be between 1 and 30 days."
        : document.StartDelayDays is < 0 or > 14 ? "Freeze start delay must be between 0 and 14 days."
        : null;
}
