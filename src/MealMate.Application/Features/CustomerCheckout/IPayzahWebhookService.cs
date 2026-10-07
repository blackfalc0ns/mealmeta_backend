using System.Text.Json;

namespace MealMate.Application.Features.CustomerCheckout;

public interface IPayzahWebhookService
{
    Task<PayzahWebhookExecutionResult> ProcessWebhookAsync(
        JsonElement payload,
        CancellationToken cancellationToken = default);
}
