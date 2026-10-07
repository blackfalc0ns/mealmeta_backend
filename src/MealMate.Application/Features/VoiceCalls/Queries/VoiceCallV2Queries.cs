using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

/// <summary>v2 surface: server-computed contact eligibility for one stop (T09).</summary>
public sealed record GetDeliveryContactEligibilityQuery(Guid TripStopId, Guid ActorUserId) : IQuery<DeliveryContactEligibilityDto>;

public sealed class GetDeliveryContactEligibilityQueryHandler : IQueryHandler<GetDeliveryContactEligibilityQuery, DeliveryContactEligibilityDto>
{
    private readonly IDeliveryContactEligibilityService _eligibility;

    public GetDeliveryContactEligibilityQueryHandler(IDeliveryContactEligibilityService eligibility) => _eligibility = eligibility;

    public Task<Result<DeliveryContactEligibilityDto>> Handle(GetDeliveryContactEligibilityQuery request, CancellationToken cancellationToken) =>
        _eligibility.EvaluateAsync(request.TripStopId, request.ActorUserId, cancellationToken);
}

/// <summary>
/// Call-bound TURN credentials (v2): nonterminal owned call required — no
/// random/unauthenticated credential fallback (spec §12 / T24 gate).
/// </summary>
public sealed record GetTurnCredentialsForCallV2Query(Guid CallId, Guid ActorUserId) : IQuery<TurnCredentialsResponseDto>;

public sealed class GetTurnCredentialsForCallV2QueryHandler : IQueryHandler<GetTurnCredentialsForCallV2Query, TurnCredentialsResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ITurnCredentialService _turn;

    public GetTurnCredentialsForCallV2QueryHandler(IAppDbContext db, ITurnCredentialService turn)
    {
        _db = db;
        _turn = turn;
    }

    public async Task<Result<TurnCredentialsResponseDto>> Handle(GetTurnCredentialsForCallV2Query request, CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (call is null || !VoiceCallV2Guard.IsParticipant(call, request.ActorUserId))
        {
            return Result.Failure<TurnCredentialsResponseDto>(VoiceCallErrors.NotFound());
        }

        if (call.IsTerminal)
        {
            return Result.Failure<TurnCredentialsResponseDto>(VoiceCallErrors.CallAlreadyEnded());
        }

        var actorId = call.CallerUserId == request.ActorUserId ? call.CallerUserId : call.ReceiverUserId;
        return Result.Success(_turn.GenerateCredentials(actorId.ToString()));
    }
}
