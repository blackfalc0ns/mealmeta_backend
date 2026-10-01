using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class SandboxCheckoutControllerTests
{
    [Fact]
    public async Task OpeningPage_RequiresExplicitPostAndDoesNotCompleteCheckout()
    {
        var sandbox = Substitute.For<ISandboxCheckoutService>();
        sandbox.ReadPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result.Success(new SandboxCheckoutPage(Guid.NewGuid(), 118.25m, "KWD", "mealmate://checkout/success")));
        var controller = Create(sandbox);
        var response = Assert.IsType<ContentResult>(await controller.Page("ticket\"><script>alert(1)</script>", default));
        Assert.Contains("method=\"post\"", response.Content);
        Assert.Contains("تأكيد الاشتراك بدون دفع", response.Content);
        Assert.DoesNotContain("<script>", response.Content);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        await sandbox.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidTicket_DoesNotRenderSuccess()
    {
        var sandbox = Substitute.For<ISandboxCheckoutService>();
        sandbox.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result.Failure<SandboxCheckoutPage>(Error.Conflict("checkout.sandbox_disabled")));
        var response = Assert.IsType<ObjectResult>(await Create(sandbox).Confirm("expired", default));
        Assert.Equal(409, response.StatusCode);
    }

    private static SandboxCheckoutController Create(ISandboxCheckoutService service) => new(service) {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
}
