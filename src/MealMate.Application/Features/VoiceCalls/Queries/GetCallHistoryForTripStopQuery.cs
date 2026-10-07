using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

public sealed record GetCallHistoryForTripStopQuery(
    Guid TripStopId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<CallSessionDto>>;

public sealed class GetCallHistoryForTripStopQueryHandler : IQueryHandler<GetCallHistoryForTripStopQuery, PagedResult<CallSessionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IVoiceCallAuthorizationService _authorizationService;

    public GetCallHistoryForTripStopQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IVoiceCallAuthorizationService authorizationService)
    {
        _db = db;
        _currentUser = currentUser;
        _authorizationService = authorizationService;
    }

    public async Task<Result<PagedResult<CallSessionDto>>> Handle(GetCallHistoryForTripStopQuery request, CancellationToken cancellationToken)
    {
        // Stop-scoped authorization (T01/F02): only the assigned driver or the stop's
        // customer may read call history; anyone else gets the same 404 as a missing
        // resource. Supervisor history access is a separate scoped permission (T17).
        var currentUserId = _currentUser.UserId;
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
        {
            return Result.Failure<PagedResult<CallSessionDto>>(VoiceCallErrors.Unauthorized());
        }

        var authorization = await _authorizationService.AuthorizeTripStopParticipantAsync(
            request.TripStopId, currentUserId.Value, cancellationToken);

        if (authorization.IsFailure)
        {
            return Result.Failure<PagedResult<CallSessionDto>>(authorization.Error);
        }

        var pageRequest = new PageRequest
        {
            Page = request.Page,
            PageSize = request.PageSize
        };

        var query = _db.CallSessions
            .AsNoTracking()
            .Where(c => c.TripStopId == request.TripStopId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.NormalizedPageSize)
            .Select(s => new CallSessionDto(
                s.Id,
                s.TripStopId,
                s.CallerUserId,
                s.CallerType.ToString(),
                s.ReceiverUserId,
                s.ReceiverType.ToString(),
                s.Status.ToString(),
                s.EndReason != null ? s.EndReason.ToString() : null,
                s.StartedAtUtc,
                s.RingingAtUtc,
                s.AcceptedAtUtc,
                s.ConnectedAtUtc,
                s.EndedAtUtc,
                s.DurationSeconds,
                s.ClientRequestId
            ))
            .ToListAsync(cancellationToken);

        var pagedResult = new PagedResult<CallSessionDto>(
            items,
            totalCount,
            pageRequest.NormalizedPage,
            pageRequest.NormalizedPageSize);

        return Result.Success(pagedResult);
    }
}
