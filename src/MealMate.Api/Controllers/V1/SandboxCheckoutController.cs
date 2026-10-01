using System.Globalization;
using System.Net;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

// Access requires the short-lived encrypted capability issued for an authenticated customer's own checkout.
[AllowAnonymous]
[Route("api/v1/payments/sandbox")]
public sealed class SandboxCheckoutController(ISandboxCheckoutService sandbox) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Page([FromQuery] string token, CancellationToken ct)
    {
        SetPrivacyHeaders();
        var result = await sandbox.ReadPageAsync(token, ct);
        if (result.IsFailure) return Problem(statusCode: 409, title: "رابط التجربة غير متاح", detail: result.Error.Code);
        var page = result.Value;
        return Content(Html("إكمال الاشتراك — تجربة", $"<p>قيمة الاشتراك: {page.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {Encode(page.CurrencyCode)}</p>"
            + "<p>لن يتم خصم أي مبلغ. هذا المسار مخصص لبيئة التجربة فقط.</p>"
            + "<form method=\"post\" action=\"/api/v1/payments/sandbox/confirm\">"
            + "<input type=\"hidden\" name=\"token\" value=\"" + Encode(token) + "\">"
            + "<button type=\"submit\">تأكيد الاشتراك بدون دفع — تجربة</button></form>"), "text/html; charset=utf-8");
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromForm] string token, CancellationToken ct)
    {
        SetPrivacyHeaders();
        var result = await sandbox.CompleteAsync(token, ct);
        if (result.IsFailure) return Problem(statusCode: 409, title: "تعذّر إكمال الاشتراك التجريبي", detail: result.Error.Code);
        return Content(Html("تم تأكيد الاشتراك التجريبي", "<p>لم يتم تحصيل أي مبلغ.</p><p><a href=\""
            + Encode(result.Value.CallbackUrl) + "\">العودة إلى التطبيق</a></p>"), "text/html; charset=utf-8");
    }

    private void SetPrivacyHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
    private static string Html(string title, string body) => "<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">"
        + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + Encode(title)
        + "</title><style>body{font-family:Tahoma,Arial;text-align:center;margin:60px 20px;color:#1f2937}button{padding:16px;background:#2563eb;color:white;border:0;border-radius:8px;font-size:18px;cursor:pointer}</style>"
        + "</head><body><h1>" + Encode(title) + "</h1>" + body + "</body></html>";
}
