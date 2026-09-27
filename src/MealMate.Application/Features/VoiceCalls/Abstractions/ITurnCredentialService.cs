using MealMate.Application.Features.VoiceCalls.Dtos;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

public interface ITurnCredentialService
{
    TurnCredentialsResponseDto GenerateCredentials(string userId);
}
