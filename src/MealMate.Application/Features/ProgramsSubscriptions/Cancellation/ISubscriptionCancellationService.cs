using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

/// <summary>
/// Domain service responsible for subscription cancellation workflows,
/// kitchen lead window calculation, refund quotes, and uncompleted draft cleanups.
/// </summary>
public interface ISubscriptionCancellationService
{
    Task<Result<CustomerCancellationQuoteDto>> GetQuoteAsync(
        Guid subscriptionId,
        Guid callerUserId,
        UserType callerUserType,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerCancellationResultDto>> RequestCancellationAsync(
        Guid subscriptionId,
        Guid callerUserId,
        UserType callerUserType,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerCancellationResultDto>> AdminCancelAsync(
        Guid subscriptionId,
        Guid adminUserId,
        string? reason,
        bool forceImmediate,
        CancellationToken cancellationToken = default);

    Task<Result> CancelUncompletedSubscriptionAsync(
        Guid subscriptionId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);
}
