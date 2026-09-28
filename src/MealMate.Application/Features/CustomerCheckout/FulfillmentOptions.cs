namespace MealMate.Application.Features.CustomerCheckout;

public sealed class FulfillmentOptions
{
    public const string SectionName = "Fulfillment";

    public int MaxAutomaticAttempts { get; set; } = 5;
}
