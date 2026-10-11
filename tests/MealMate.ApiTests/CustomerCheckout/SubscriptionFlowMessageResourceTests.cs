using System.Globalization;
using System.Text.RegularExpressions;
using MealMate.Api.Resources;
using Xunit;

namespace MealMate.ApiTests.CustomerCheckout;

/// <summary>
/// The individual subscription flow's semantic availability codes resolve to product copy in
/// BOTH languages from the shared Messages resources. Keys are technical identifiers and never
/// renamed; this test guards key presence and placeholder parity between Arabic and English.
/// </summary>
public sealed class SubscriptionFlowMessageResourceTests
{
    private static readonly string[] FlowKeys =
    [
        "subscription_flow.no_restaurants_for_address",
        "subscription_flow.no_restaurants_for_selection",
        "subscription_flow.no_delivery_to_address",
        "subscription_flow.pricing_unavailable",
        "checkout.start_date_required",
        "checkout.address_required",
        "checkout.allergies_required",
        "checkout.dietary_preferences_required",
        "subscription_flow.no_prepared_restaurants",
        "calendar.selection_deadline_passed",
        "calendar.selection_locked",
        "RESTAURANT_CAPACITY_FULL",
    ];

    private static readonly Regex Placeholder = new(@"\{[^}]+\}", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(FlowKeyMembers))]
    public void Flow_reason_codes_resolve_in_english_and_arabic(string key)
    {
        var manager = new System.Resources.ResourceManager(
            "MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);

        var english = manager.GetString(key, CultureInfo.GetCultureInfo("en"));
        var arabic = manager.GetString(key, CultureInfo.GetCultureInfo("ar"));

        Assert.False(string.IsNullOrWhiteSpace(english), $"{key} is missing its English message.");
        Assert.False(string.IsNullOrWhiteSpace(arabic), $"{key} is missing its Arabic message.");
        Assert.NotEqual(key, english);
        Assert.NotEqual(key, arabic);

        // Placeholder parity: same slot names in both languages, order included.
        Assert.Equal(
            Placeholder.Matches(english).Select(m => m.Value),
            Placeholder.Matches(arabic).Select(m => m.Value));
    }

    public static TheoryData<string> FlowKeyMembers()
    {
        var data = new TheoryData<string>();
        foreach (var key in FlowKeys) data.Add(key);
        return data;
    }
}
