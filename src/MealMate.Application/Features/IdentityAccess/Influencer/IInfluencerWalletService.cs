using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerWalletService
{
    Task<Result<InfluencerWalletDto>> GetWalletAsync(Guid userId, CancellationToken ct = default);
    Task<Result<PagedResultDto<WithdrawalRecordDto>>> GetWithdrawalsAsync(Guid userId, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<Result<WithdrawalRecordDto>> GetWithdrawalDetailsAsync(Guid userId, Guid withdrawalId, CancellationToken ct = default);
    Task<Result<WithdrawalRecordDto>> CancelWithdrawalAsync(Guid userId, Guid withdrawalId, CancellationToken ct = default);
    Task<Result<WithdrawalRecordDto>> SubmitWithdrawalAsync(Guid userId, SubmitInfluencerWithdrawalCommand command, CancellationToken ct = default);
    Task<Result<PagedResult<AdminWithdrawalItemDto>>> ListAdminWithdrawalsAsync(string? status, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<Result<AdminWithdrawalItemDto>> ApproveWithdrawalAsync(Guid withdrawalId, Guid adminUserId, string transferRef, DateTime? transferDate, string? notes, CancellationToken ct = default);
    Task<Result<AdminWithdrawalItemDto>> RejectWithdrawalAsync(Guid withdrawalId, Guid adminUserId, string rejectionReason, CancellationToken ct = default);
}
