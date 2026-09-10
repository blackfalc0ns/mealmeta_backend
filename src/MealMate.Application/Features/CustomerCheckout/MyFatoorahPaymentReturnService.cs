using System.Net;
using System.Text.Encodings.Web;
using MealMate.Application.Common.Abstractions;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed class MyFatoorahPaymentReturnService : IMyFatoorahPaymentReturnService
{
    private readonly IMyFatoorahPaymentService _payments;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;

    public MyFatoorahPaymentReturnService(
        IMyFatoorahPaymentService payments,
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
        var key = FirstNonEmpty(paymentId, queryId, queryInvoiceId);

        MyFatoorahPaymentStatusResponse? gateway = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            gateway = await _payments.GetPaymentStatusAsync(key, "PaymentId", cancellationToken);
            if (gateway is null)
                gateway = await _payments.GetPaymentStatusAsync(key, "InvoiceId", cancellationToken);
        }

        var paid = IsPaid(gateway);
        var failed = IsFailed(gateway);
        var resolvedPaymentId = FirstNonEmpty(gateway?.PaymentId, key) ?? string.Empty;

        if (gateway is not null && Guid.TryParse(gateway.CustomerReference, out var reference))
        {
            if (paid)
            {
                await _paymentProcessor.VerifyAndProcessAsync(
                    reference,
                    resolvedPaymentId,
                    gateway.InvoiceId,
                    gateway.InvoiceValue,
                    gateway.CurrencyCode,
                    gateway.CustomerReference,
                    cancellationToken: cancellationToken);
            }
            else if (failed)
            {
                await _paymentProcessor.VerifyAndReleaseFailedAsync(
                    reference,
                    resolvedPaymentId,
                    gateway.InvoiceId,
                    gateway.InvoiceValue,
                    gateway.CustomerReference,
                    cancellationToken: cancellationToken);
            }
        }

        var deepLink = ResolveDeepLink(to, paid, resolvedPaymentId);
        var title = paid 
            ? MyFatoorahPaymentReturnLocalization.PaymentSuccessTitle 
            : failed 
                ? MyFatoorahPaymentReturnLocalization.PaymentFailedTitle 
                : MyFatoorahPaymentReturnLocalization.PaymentIncompleteTitle;
        var message = paid
            ? MyFatoorahPaymentReturnLocalization.PaymentSuccessMessage
            : MyFatoorahPaymentReturnLocalization.PaymentFailedMessage;

        return BuildHtml(title, message, deepLink);
    }

    private static bool IsPaid(MyFatoorahPaymentStatusResponse? gateway)
        => gateway is not null
           && (gateway.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase));

    private static bool IsFailed(MyFatoorahPaymentStatusResponse? gateway)
        => gateway is not null
           && (gateway.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
               || gateway.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));

    private static string ResolveDeepLink(string? to, bool paid, string paymentId)
    {
        var destination = paid
            ? "mealmate://checkout/success"
            : "mealmate://checkout/error";
        if (Uri.TryCreate(to, UriKind.Absolute, out var target)
            && (target.Scheme.Equals("mealmate", StringComparison.OrdinalIgnoreCase)
                || target.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            destination = target.ToString();
        }

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
        var buttonText = WebUtility.HtmlEncode(MyFatoorahPaymentReturnLocalization.ReturnToAppButton);

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
