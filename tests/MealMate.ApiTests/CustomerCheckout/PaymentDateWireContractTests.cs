using System.Text.Json;
using MealMate.Api.Localization;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class PaymentDateWireContractTests
{
    [Theory]
    [InlineData(typeof(SubscriptionOrderSummaryDto))]
    [InlineData(typeof(CheckoutStartedDto))]
    public void Individual_responses_expose_payment_date_without_the_old_chosen_start_date(Type responseType)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MealMateJsonSerializer.Configure(options);
        const string payload = """
            { "paymentDate": "2026-10-10", "startDate": "2026-10-14" }
            """;

        var response = JsonSerializer.Deserialize(payload, responseType, options);
        var json = JsonSerializer.SerializeToElement(response, responseType, options);

        Assert.True(json.TryGetProperty("paymentDate", out var paymentDate));
        Assert.Equal("2026-10-10", paymentDate.GetString());
        Assert.Equal("2026-10-14", json.GetProperty("startDate").GetString());
        Assert.False(json.TryGetProperty("chosenStartDate", out _));
    }
}
