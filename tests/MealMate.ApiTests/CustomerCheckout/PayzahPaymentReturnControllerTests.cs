using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
namespace MealMate.ApiTests.CustomerCheckout;
public sealed class PayzahPaymentReturnControllerTests
{
    [Theory]
    [InlineData("Paid", false, "review")]
    [InlineData("Paid", true, "success")]
    [InlineData("Pending", false, "pending")]
    [InlineData("Failed", false, "error")]
    public async Task ReturnUsesBoundServerVerificationAndProcessorOutcome(string status, bool accepted, string expected)
    {
        var payments = Substitute.For<IPaymentGatewayService>();
        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        var reference = Guid.NewGuid();
        payments.GetPaymentStatusAsync("track-id", "TrackId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("provider-reference", status, 10m, "", "", "", "transaction", "Payzah", DateTime.UtcNow, reference.ToString(), "KWD", "creation-id"));
        processor.VerifyAndProcessAsync(reference, "creation-id", "provider-reference", 10m, "KWD", reference.ToString(), null, Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(accepted, false, accepted ? null : "payment.stale_attempt_review_required", null, accepted ? "Completed" : "ReviewRequired"));
        processor.VerifyAndReleaseFailedAsync(reference, "creation-id", "provider-reference", 10m, reference.ToString(), null, Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(true, false, null, null, "Released"));
        var controller = new PayzahPaymentReturnController(payments, processor) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.QueryString = new QueryString("?trackId=track-id");
        var result = Assert.IsType<ContentResult>(await controller.Return("https://attacker.invalid", null));
        Assert.Contains("mealmate://checkout/" + expected, result.Content);
        Assert.DoesNotContain("attacker.invalid", result.Content);
        if (expected != "success") Assert.DoesNotContain("mealmate://checkout/success", result.Content);
    }
    [Fact]
    public async Task CapturedWithMalformedReferenceShowsReviewWithoutActivating()
    {
        var payments = Substitute.For<IPaymentGatewayService>();
        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        payments.GetPaymentStatusAsync("track-id", "TrackId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("reference", "Paid", 10m, "", "", "", "transaction", "Payzah", DateTime.UtcNow, "invalid", "KWD", "creation-id"));
        var service = new PayzahPaymentReturnService(payments, processor);
        var html = await service.ProcessReturnAndBuildHtmlAsync(null, null, "track-id", null);
        Assert.Contains("mealmate://checkout/review", html);
        Assert.Empty(processor.ReceivedCalls());
    }
}
