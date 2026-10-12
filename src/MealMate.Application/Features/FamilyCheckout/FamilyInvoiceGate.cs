using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class FamilyInvoiceOptions
{
    public const string SectionName = "FamilyInvoices";

    public bool StopNewFamilyInvoices { get; set; }

    public int MinimumContractVersion { get; set; } = FamilyInvoiceGate.SupportedContractVersion;
}

public static class FamilyInvoiceGate
{
    public const int SupportedContractVersion = 1;

    public static Error? ValidateNewInvoice(int? clientContractVersion, FamilyInvoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (clientContractVersion is null || clientContractVersion < options.MinimumContractVersion)
        {
            return Error.Conflict("family.client_update_required");
        }

        if (options.StopNewFamilyInvoices)
        {
            return Error.Conflict("family.invoice_creation_stopped");
        }

        return null;
    }

    public static async Task<Result<PaymentGatewayMethod>> ResolvePaymentMethodAsync(
        IPaymentGatewayService payments,
        decimal invoiceAmount,
        int paymentMethodId,
        string quoteCurrency,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PaymentGatewayMethod> methods;
        try
        {
            methods = await payments.GetPaymentMethodsAsync(invoiceAmount, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error.Failure("family.payment_method_unavailable");
        }

        var selected = methods.FirstOrDefault(method => method.PaymentMethodId == paymentMethodId);
        if (selected is null)
        {
            return Error.Conflict("family.payment_method_unavailable");
        }

        if (string.IsNullOrWhiteSpace(selected.CurrencyCode)
            || !string.Equals(selected.CurrencyCode, quoteCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Conflict("family.payment_currency_unsupported");
        }

        return selected;
    }
}
