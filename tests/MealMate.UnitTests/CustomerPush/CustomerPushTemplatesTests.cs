using MealMate.Application.Features.CustomerPush;

namespace MealMate.UnitTests.CustomerPush;

public sealed class CustomerPushTemplatesTests
{
    [Fact]
    public void SubscriptionPaymentConfirmed_is_a_welcome_message()
    {
        var ar = CustomerPushTemplates.SubscriptionPaymentConfirmed(isRtl: true);
        Assert.Contains("أهلاً", ar.Title, StringComparison.Ordinal);
        Assert.Contains("اشتراك", ar.Body, StringComparison.Ordinal);
        Assert.Equal("mealmate://calendar", ar.DeepLink);

        var en = CustomerPushTemplates.SubscriptionPaymentConfirmed(isRtl: false);
        Assert.Contains("Welcome", en.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subscription", en.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mealmate://calendar", en.DeepLink);
    }
}
