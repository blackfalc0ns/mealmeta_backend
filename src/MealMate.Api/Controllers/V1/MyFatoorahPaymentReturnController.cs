using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/payments/myfatoorah")]
public sealed class MyFatoorahPaymentReturnController : ControllerBase
{
    [HttpGet("return")]
    public IActionResult Return([FromQuery] string? to, [FromQuery] string? paymentId)
    {
        if (!Uri.TryCreate(to, UriKind.Absolute, out var target))
            return BadRequest(new { error = "checkout.redirect_url_not_allowed" });
        if (!target.Scheme.Equals("mealmate", StringComparison.OrdinalIgnoreCase)
            && !target.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "checkout.redirect_url_not_allowed" });

        var destination = target.ToString();
        if (!string.IsNullOrWhiteSpace(paymentId))
            destination += (target.Query.Length > 0 ? "&" : "?") + "paymentId=" + Uri.EscapeDataString(paymentId);
        return Redirect(destination);
    }
}
