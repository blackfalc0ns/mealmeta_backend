namespace MealMate.Application.Features.CustomerCheckout;

public interface IMyFatoorahPaymentReturnService
{
    Task<string> ProcessReturnAndBuildHtmlAsync(
        string? to,
        string? paymentId,
        string? queryId,
        string? queryInvoiceId,
        CancellationToken cancellationToken = default);
}
