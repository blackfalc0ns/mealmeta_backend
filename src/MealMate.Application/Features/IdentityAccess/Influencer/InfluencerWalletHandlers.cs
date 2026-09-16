using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class GetInfluencerWalletQueryHandler : IQueryHandler<GetInfluencerWalletQuery, InfluencerWalletDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerWalletQueryHandler(IInfluencerWalletService walletService, ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerWalletDto>> Handle(GetInfluencerWalletQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _walletService.GetWalletAsync(userId, cancellationToken);
    }
}

public sealed class GetInfluencerWithdrawalsQueryHandler : IQueryHandler<GetInfluencerWithdrawalsQuery, PagedResultDto<WithdrawalRecordDto>>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerWithdrawalsQueryHandler(IInfluencerWalletService walletService, ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResultDto<WithdrawalRecordDto>>> Handle(GetInfluencerWithdrawalsQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _walletService.GetWithdrawalsAsync(userId, request.Status, request.Page, request.PageSize, cancellationToken);
    }
}

public sealed class GetInfluencerWithdrawalDetailsQueryHandler : IQueryHandler<GetInfluencerWithdrawalDetailsQuery, WithdrawalRecordDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerWithdrawalDetailsQueryHandler(IInfluencerWalletService walletService, ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public async Task<Result<WithdrawalRecordDto>> Handle(GetInfluencerWithdrawalDetailsQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _walletService.GetWithdrawalDetailsAsync(userId, request.Id, cancellationToken);
    }
}

public sealed class CancelInfluencerWithdrawalCommandHandler : ICommandHandler<CancelInfluencerWithdrawalCommand, WithdrawalRecordDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public CancelInfluencerWithdrawalCommandHandler(
        IInfluencerWalletService walletService,
        ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public async Task<Result<WithdrawalRecordDto>> Handle(CancelInfluencerWithdrawalCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _walletService.CancelWithdrawalAsync(userId, request.Id, cancellationToken);
    }
}

public sealed class SubmitInfluencerWithdrawalCommandHandler : ICommandHandler<SubmitInfluencerWithdrawalCommand, WithdrawalRecordDto>
{
    private readonly IInfluencerWalletService _walletService;
    private readonly ICurrentUser _currentUser;

    public SubmitInfluencerWithdrawalCommandHandler(
        IInfluencerWalletService walletService,
        ICurrentUser currentUser)
    {
        _walletService = walletService;
        _currentUser = currentUser;
    }

    public async Task<Result<WithdrawalRecordDto>> Handle(SubmitInfluencerWithdrawalCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _walletService.SubmitWithdrawalAsync(userId, request, cancellationToken);
    }
}
