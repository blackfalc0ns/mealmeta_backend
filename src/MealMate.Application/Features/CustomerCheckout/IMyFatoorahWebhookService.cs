using System.Text.Json;

namespace MealMate.Application.Features.CustomerCheckout;

public interface IMyFatoorahWebhookService
{
    Task<MyFatoorahWebhookExecutionResult> ProcessWebhookAsync(
        JsonElement payload,
        string? sigHeader,
        CancellationToken cancellationToken = default);
}
