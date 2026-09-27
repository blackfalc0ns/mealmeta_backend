using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

public sealed record GetCallHistoryForTripStopQuery(
    Guid TripStopId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<CallSessionDto>>;

public sealed class GetCallHistoryForTripStopQueryHandler : IQueryHandler<GetCallHistoryForTripStopQuery, PagedResult<CallSessionDto>>
{
    private readonly IAppDbContext _db;

    public GetCallHistoryForTripStopQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<CallSessionDto>>> Handle(GetCallHistoryForTripStopQuery request, CancellationToken cancellationToken)
    {
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
