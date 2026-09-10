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

public sealed class UpdateInfluencerSocialLinksCommandHandler : ICommandHandler<UpdateInfluencerSocialLinksCommand>
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

    public async Task<Result> Handle(UpdateInfluencerSocialLinksCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _profileService.UpdateSocialLinksAsync(userId, request, cancellationToken);
    }
}
