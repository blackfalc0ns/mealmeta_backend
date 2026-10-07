namespace MealMate.Application.Features.CustomerCheckout;

public interface IPayzahPaymentReturnService
{
    Task<string> ProcessReturnAndBuildHtmlAsync(
        string? to,
        string? paymentId,
        string? queryId,
        string? queryInvoiceId,
        CancellationToken cancellationToken = default);
}
