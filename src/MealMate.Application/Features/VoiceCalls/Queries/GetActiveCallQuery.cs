using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.VoiceCalls.Queries;

public sealed record GetActiveCallQuery : IQuery<CallSessionDto?>;

public sealed class GetActiveCallQueryHandler : IQueryHandler<GetActiveCallQuery, CallSessionDto?>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<GetActiveCallQueryHandler> _logger;

    public GetActiveCallQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILogger<GetActiveCallQueryHandler>? logger = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GetActiveCallQueryHandler>.Instance;
    }

    public async Task<Result<CallSessionDto?>> Handle(GetActiveCallQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            return VoiceCallErrors.Unauthorized();
        }

        var currentUserId = _currentUser.UserId.Value;

        var activeCalls = await _db.CallSessions
            .AsNoTracking()
            .Where(c => (c.CallerUserId == currentUserId || c.ReceiverUserId == currentUserId)
                        && CallStatePolicy.OngoingStatuses.Contains(c.Status))
            .OrderByDescending(c => c.StartedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (activeCalls.Count == 0)
        {
            return Result.Success<CallSessionDto?>(null);
        }

        if (activeCalls.Count > 1)
        {
            _logger.LogError(
                "INVARIANT VIOLATION: User {UserId} has {Count} concurrent ongoing calls. Returning most recent call {CallId}.",
                currentUserId, activeCalls.Count, activeCalls[0].Id);
        }

        return Result.Success<CallSessionDto?>(CallSessionDto.FromEntity(activeCalls[0]));
    }
}
