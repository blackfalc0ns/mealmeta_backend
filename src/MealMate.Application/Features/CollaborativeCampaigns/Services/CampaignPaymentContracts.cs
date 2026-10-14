using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record StartCampaignPaymentRequest(
    string IdempotencyKey, string? CallbackUrl = null, string? ErrorUrl = null, int PaymentMethodId = 3);

public sealed record CampaignPaymentStartedDto(
    Guid SessionId, string PaymentId, string PaymentUrl, decimal Amount, string Currency, string Status, DateTime ExpiresAtUtc);

public sealed record CampaignPaymentStatusDto(
    Guid SessionId, string? PaymentId, string Status, bool IsPaid, bool IsFailed,
    decimal Amount, string Currency, DateTime ExpiresAtUtc, Guid? SubscriptionId = null);

public interface ICampaignPaymentService
{
    Task<Result<CampaignPaymentStartedDto>> StartAsync(Guid sessionId, Guid customerId,
        StartCampaignPaymentRequest request, CancellationToken ct = default);
    Task<Result<CampaignPaymentStatusDto>> GetStatusAsync(Guid sessionId, Guid customerId, CancellationToken ct = default);
    Task<Result<ProcessPaymentCallbackResultDto>> ProcessCallbackAsync(ProcessPaymentCallbackCommand command, CancellationToken ct = default);
}
