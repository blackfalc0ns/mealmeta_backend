using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public sealed record CreateLoginChallengeRequest(
    Guid UserId,
    string NextStep,
    string? DeviceInfo,
    string? IpAddress);

public sealed record CreatedLoginChallenge(
    Guid ChallengeId,
    string RawToken,
    string NextStep,
    Guid UserId,
    DateTime ExpiresAtUtc);

public sealed record ResolvedLoginChallenge(
    Guid Id,
    Guid UserId,
    string NextStep,
    int AttemptCount,
    int MaxAttempts,
    DateTime ExpiresAtUtc);

public interface IAuthLoginChallengeService
{
    Task<CreatedLoginChallenge> CreateAsync(
        CreateLoginChallengeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ResolvedLoginChallenge>> ResolveAsync(
        string rawToken,
        string expectedNextStep,
        CancellationToken cancellationToken = default);

    Task<Result> RecordFailedAttemptAsync(
        ResolvedLoginChallenge challenge,
        CancellationToken cancellationToken = default);

    Task<Result> TryConsumeAsync(
        ResolvedLoginChallenge challenge,
        CancellationToken cancellationToken = default);

    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
