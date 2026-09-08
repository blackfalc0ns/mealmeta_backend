using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class MyFatoorahPaymentReturnControllerTests
{
    [Fact]
    public async Task Return_WithoutTo_WithPaymentId_VerifiesPaidFamilyInvoiceAndReturnsArabicHtml()
    {
        var familyGroupId = Guid.Parse("6f9b5e7b-3c3e-4fd1-8f4a-0d7c3f7e9f11");
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.GetPaymentStatusAsync("PAY-FAM-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-F", "Paid", 270m, "Customer", "c@example.com", "50000000", "TX-F", "VISA/MASTER",
                DateTime.UtcNow, familyGroupId.ToString(), "KWD", "PAY-FAM-1"));

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(
                familyGroupId, "PAY-FAM-1", "INV-F", 270m, "KWD", familyGroupId.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(true, false, null, MealMate.Domain.Enums.SubscriptionStatus.Paid, "Pending"));

        var controller = CreateController(payments, processor);

        var result = await controller.Return(to: null, paymentId: "PAY-FAM-1");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("text/html", content.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("تم الدفع", content.Content, StringComparison.Ordinal);
        Assert.Contains("mealmate://checkout/success", content.Content, StringComparison.Ordinal);
        await processor.Received(1).VerifyAndProcessAsync(
            familyGroupId, "PAY-FAM-1", "INV-F", 270m, "KWD", familyGroupId.ToString(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Return_WithMealmateTo_ReturnsHtmlNotBareRedirect()
    {
        var reference = Guid.NewGuid();
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.GetPaymentStatusAsync("PAY-1", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-1", "Paid", 40m, "Customer", "c@example.com", "50000000", "TX-1", "VISA/MASTER",
                DateTime.UtcNow, reference.ToString(), "KWD", "PAY-1"));
        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(
                reference, "PAY-1", "INV-1", 40m, "KWD", reference.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(true, false, null, MealMate.Domain.Enums.SubscriptionStatus.Paid, "Completed"));

        var controller = CreateController(payments, processor);

        var result = await controller.Return("mealmate://checkout/success", "PAY-1");

        Assert.IsNotType<RedirectResult>(result);
        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("text/html", content.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mealmate://checkout/success", content.Content, StringComparison.Ordinal);
        Assert.Contains("PAY-1", content.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_FailedInvoice_ReleasesAndReturnsArabicErrorHtml()
    {
        var familyGroupId = Guid.NewGuid();
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.GetPaymentStatusAsync("PAY-FAIL", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(
                "INV-X", "Failed", 270m, "Customer", "c@example.com", "50000000", "TX-X", "VISA/MASTER",
                DateTime.UtcNow, familyGroupId.ToString(), "KWD", "PAY-FAIL"));
        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndReleaseFailedAsync(
                familyGroupId, "PAY-FAIL", "INV-X", 270m, familyGroupId.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentProcessingResult(true, false, null, MealMate.Domain.Enums.SubscriptionStatus.PaymentFailed, "Released"));

        var controller = CreateController(payments, processor);

        var result = await controller.Return(to: null, paymentId: "PAY-FAIL");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("text/html", content.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("فشل", content.Content, StringComparison.Ordinal);
        Assert.Contains("mealmate://checkout/error", content.Content, StringComparison.Ordinal);
        await processor.Received(1).VerifyAndReleaseFailedAsync(
            familyGroupId, "PAY-FAIL", "INV-X", 270m, familyGroupId.ToString(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await processor.DidNotReceiveWithAnyArgs().VerifyAndProcessAsync(
            default, default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Return_WithoutPaymentIdAndWithoutTo_ReturnsHtmlNotJson()
    {
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        var controller = CreateController(payments, processor);

        var result = await controller.Return(to: null, paymentId: null);

        Assert.IsNotType<BadRequestObjectResult>(result);
        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("text/html", content.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkout.redirect_url_not_allowed", content.Content, StringComparison.Ordinal);
        await processor.DidNotReceiveWithAnyArgs().VerifyAndProcessAsync(
            default, default!, default, default, default, default, default, default);
        await processor.DidNotReceiveWithAnyArgs().VerifyAndReleaseFailedAsync(
            default, default!, default, default, default, default, default);
    }

    private static MyFatoorahPaymentReturnController CreateController(
        IMyFatoorahPaymentService payments,
        ISubscriptionPaymentProcessor processor)
        => new(payments, processor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
