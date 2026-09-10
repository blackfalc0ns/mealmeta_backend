using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerWalletService
{
    Task<Result<InfluencerWalletDto>> GetWalletAsync(Guid userId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WithdrawalRecordDto>>> GetWithdrawalsAsync(Guid userId, string? status, CancellationToken ct = default);
    Task<Result<WithdrawalRecordDto>> GetWithdrawalDetailsAsync(Guid userId, Guid withdrawalId, CancellationToken ct = default);
    Task<Result<WithdrawalRecordDto>> SubmitWithdrawalAsync(Guid userId, SubmitInfluencerWithdrawalCommand command, CancellationToken ct = default);
}
