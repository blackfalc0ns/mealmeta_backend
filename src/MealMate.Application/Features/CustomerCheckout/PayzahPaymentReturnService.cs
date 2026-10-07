using System.Net;
using System.Text.Encodings.Web;
using MealMate.Application.Common.Abstractions;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed class PayzahPaymentReturnService : IPayzahPaymentReturnService
{
    private readonly IPaymentGatewayService _payments;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;

    public PayzahPaymentReturnService(
        IPaymentGatewayService payments,
        ISubscriptionPaymentProcessor paymentProcessor)
    {
        _payments = payments;
        _paymentProcessor = paymentProcessor;
    }

    public async Task<string> ProcessReturnAndBuildHtmlAsync(
        string? to,
        string? paymentId,
        string? queryId,
        string? queryInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var key = FirstNonEmpty(queryId, paymentId);

        PaymentGatewayStatusResponse? gateway = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            gateway = await _payments.GetPaymentStatusAsync(key, "TrackId", cancellationToken);

        }

        var captured = IsPaid(gateway);
        var paid = false;
        var review = captured;
        var failed = IsFailed(gateway);
        var resolvedPaymentId = FirstNonEmpty(gateway?.PaymentId, key) ?? string.Empty;

        if (gateway is not null && Guid.TryParse(gateway.CustomerReference, out var reference))
        {
            if (captured)
            {
                var processed = await _paymentProcessor.VerifyAndProcessAsync(
                    reference,
                    resolvedPaymentId,
                    gateway.InvoiceId,
                    gateway.InvoiceValue,
                    gateway.CurrencyCode,
                    gateway.CustomerReference,
                    cancellationToken: cancellationToken);
                paid = processed.IsAccepted && processed.FulfillmentStatus != "ReviewRequired";
                review = !paid;
            }
            else if (failed)
            {
                var released = await _paymentProcessor.VerifyAndReleaseFailedAsync(
                    reference,
                    resolvedPaymentId,
                    gateway.InvoiceId,
                    gateway.InvoiceValue,
                    gateway.CustomerReference,
                    cancellationToken: cancellationToken);
                failed = released.IsAccepted && released.FulfillmentStatus == "Released";
                review = !failed;
            }
        }

        var deepLink = ResolveDeepLink(paid ? "success" : review ? "review" : failed ? "error" : "pending", resolvedPaymentId);
        var title = paid ? PayzahPaymentReturnLocalization.PaymentSuccessTitle
            : review ? "الدفع يحتاج مراجعة" : failed ? PayzahPaymentReturnLocalization.PaymentFailedTitle : "جاري التحقق من الدفع";
        var message = paid ? PayzahPaymentReturnLocalization.PaymentSuccessMessage
            : review ? "تم تسجيل حالة الدفع للمراجعة. لا تبدأ دفعًا جديدًا قبل مراجعة العملية."
            : failed ? PayzahPaymentReturnLocalization.PaymentFailedMessage : "لم يتم تأكيد نتيجة الدفع بعد. ارجع إلى التطبيق للتحقق من الحالة.";

        return BuildHtml(title, message, deepLink);
    }

    private static bool IsPaid(PaymentGatewayStatusResponse? gateway)
        => gateway is not null
           && (gateway.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase));

    private static bool IsFailed(PaymentGatewayStatusResponse? gateway)
        => gateway is not null
           && (gateway.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));

    private static string ResolveDeepLink(string status, string paymentId)
    {
        var destination = "mealmate://checkout/" + status;
        if (!string.IsNullOrWhiteSpace(paymentId)
            && destination.IndexOf("paymentId=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            destination += (destination.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                + "paymentId=" + Uri.EscapeDataString(paymentId);
        }

        return destination;
    }

    private static string BuildHtml(string title, string message, string deepLink)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);
        var jsLink = JavaScriptEncoder.Default.Encode(deepLink);
        var safeLink = WebUtility.HtmlEncode(deepLink);
        var buttonText = WebUtility.HtmlEncode(PayzahPaymentReturnLocalization.ReturnToAppButton);

        return
            "<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>"
            + safeTitle
            + "</title></head><body style=\"font-family:Tahoma,Arial,sans-serif;text-align:center;padding:48px 20px;color:#1f2937;background:#fff\">"
            + "<h1 style=\"font-size:22px\">" + safeTitle + "</h1>"
            + "<p style=\"font-size:16px\">" + safeMessage + "</p>"
            + "<p><a href=\"" + safeLink + "\">" + buttonText + "</a></p>"
            + "<script>location.replace(\"" + jsLink + "\");</script>"
            + "</body></html>";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
