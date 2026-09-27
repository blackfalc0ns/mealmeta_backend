using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;

namespace MealMate.Application.Features.VoiceCalls.Queries;

public sealed record GetTurnCredentialsQuery : IQuery<TurnCredentialsResponseDto>;

public sealed class GetTurnCredentialsQueryHandler : IQueryHandler<GetTurnCredentialsQuery, TurnCredentialsResponseDto>
{
    private readonly ITurnCredentialService _turnCredentialService;
    private readonly ICurrentUser _currentUser;

    public GetTurnCredentialsQueryHandler(
        ITurnCredentialService turnCredentialService,
        ICurrentUser currentUser)
    {
        _turnCredentialService = turnCredentialService;
        _currentUser = currentUser;
    }

    public Task<Result<TurnCredentialsResponseDto>> Handle(GetTurnCredentialsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId != Guid.Empty
            ? _currentUser.UserId.ToString()
            : Guid.NewGuid().ToString("N");

        var credentials = _turnCredentialService.GenerateCredentials(userId);
        return Task.FromResult(Result.Success(credentials));
    }
}
