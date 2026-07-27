using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public sealed record TotpSetupDto(
    string SharedKey,
    string AuthenticatorUri,
    string Issuer,
    string AccountName);

public interface ITotpAuthenticatorService
{
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<TotpSetupDto> GetOrCreateSetupAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> VerifyCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<string>>> CompleteEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode, CancellationToken cancellationToken = default);

    Task ResetAsync(Guid userId, CancellationToken cancellationToken = default);
}
