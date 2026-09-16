using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class GetInfluencerMeQueryHandler : IQueryHandler<GetInfluencerMeQuery, InfluencerPartnerProfileDto>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerMeQueryHandler(IInfluencerProfileService profileService, ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerPartnerProfileDto>> Handle(GetInfluencerMeQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.GetProfileAsync(userId, cancellationToken);
    }
}

public sealed class GetInfluencerLinksQueryHandler : IQueryHandler<GetInfluencerLinksQuery, InfluencerLinksDto>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerLinksQueryHandler(IInfluencerProfileService profileService, ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerLinksDto>> Handle(GetInfluencerLinksQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.GetLinksAsync(userId, cancellationToken);
    }
}

public sealed class GetInfluencerBankAccountQueryHandler : IQueryHandler<GetInfluencerBankAccountQuery, InfluencerBankInfoDto>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerBankAccountQueryHandler(IInfluencerProfileService profileService, ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerBankInfoDto>> Handle(GetInfluencerBankAccountQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.GetBankAccountAsync(userId, cancellationToken);
    }
}

public sealed class UpdateInfluencerBankAccountCommandHandler : ICommandHandler<UpdateInfluencerBankAccountCommand, InfluencerBankInfoDto>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public UpdateInfluencerBankAccountCommandHandler(IInfluencerProfileService profileService, ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerBankInfoDto>> Handle(UpdateInfluencerBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.UpdateBankAccountAsync(userId, request, cancellationToken);
    }
}

public sealed class DeleteInfluencerBankAccountCommandHandler : ICommandHandler<DeleteInfluencerBankAccountCommand>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public DeleteInfluencerBankAccountCommandHandler(IInfluencerProfileService profileService, ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteInfluencerBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.DeleteBankAccountAsync(userId, cancellationToken);
    }
}

public sealed class GetInfluencerBanksQueryHandler : IQueryHandler<GetInfluencerBanksQuery, IReadOnlyList<BankCatalogItemDto>>
{
    private readonly IInfluencerProfileService _profileService;

    public GetInfluencerBanksQueryHandler(IInfluencerProfileService profileService)
    {
        _profileService = profileService;
    }

    public async Task<Result<IReadOnlyList<BankCatalogItemDto>>> Handle(GetInfluencerBanksQuery request, CancellationToken cancellationToken)
    {
        return await _profileService.GetBanksAsync(cancellationToken);
    }
}

public sealed class UpdateInfluencerSocialLinksCommandHandler : ICommandHandler<UpdateInfluencerSocialLinksCommand, InfluencerSocialLinksResponseDto>
{
    private readonly IInfluencerProfileService _profileService;
    private readonly ICurrentUser _currentUser;

    public UpdateInfluencerSocialLinksCommandHandler(
        IInfluencerProfileService profileService,
        ICurrentUser currentUser)
    {
        _profileService = profileService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerSocialLinksResponseDto>> Handle(UpdateInfluencerSocialLinksCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.UpdateSocialLinksAsync(userId, request, cancellationToken);
    }
}
