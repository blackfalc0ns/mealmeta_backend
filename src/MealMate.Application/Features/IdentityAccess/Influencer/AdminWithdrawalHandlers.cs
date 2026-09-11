using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class ListAdminWithdrawalsQueryHandler
    : IQueryHandler<ListAdminWithdrawalsQuery, PagedResult<AdminWithdrawalItemDto>>
{
    private readonly IInfluencerWalletService _walletService;

    public ListAdminWithdrawalsQueryHandler(IInfluencerWalletService walletService)
    {
        _walletService = walletService;
    }

    public Task<Result<PagedResult<AdminWithdrawalItemDto>>> Handle(
        ListAdminWithdrawalsQuery request,
        CancellationToken cancellationToken)
        => _walletService.ListAdminWithdrawalsAsync(request.Status, request.Search, request.Page, request.PageSize, cancellationToken);
}

public sealed class ApproveInfluencerWithdrawalCommandHandler
    : ICommandHandler<ApproveInfluencerWithdrawalCommand, AdminWithdrawalItemDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public ApproveInfluencerWithdrawalCommandHandler(
        IInfluencerWalletService walletService,
        ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public Task<Result<AdminWithdrawalItemDto>> Handle(
        ApproveInfluencerWithdrawalCommand request,
        CancellationToken cancellationToken)
        => _walletService.ApproveWithdrawalAsync(
            request.WithdrawalId,
            _currentUser.UserId ?? Guid.Empty,
            request.TransferReferenceNumber,
            request.TransferDateUtc,
            request.Notes,
            cancellationToken);
}

public sealed class RejectInfluencerWithdrawalCommandHandler
    : ICommandHandler<RejectInfluencerWithdrawalCommand, AdminWithdrawalItemDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public RejectInfluencerWithdrawalCommandHandler(
        IInfluencerWalletService walletService,
        ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public Task<Result<AdminWithdrawalItemDto>> Handle(
        RejectInfluencerWithdrawalCommand request,
        CancellationToken cancellationToken)
        => _walletService.RejectWithdrawalAsync(
            request.WithdrawalId,
            _currentUser.UserId ?? Guid.Empty,
            request.RejectionReason,
            cancellationToken);
}
