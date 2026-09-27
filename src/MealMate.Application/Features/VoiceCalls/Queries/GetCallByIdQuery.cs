using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

public sealed record GetCallByIdQuery(Guid CallId) : IQuery<CallSessionDto>;

public sealed class GetCallByIdQueryHandler : IQueryHandler<GetCallByIdQuery, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetCallByIdQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<CallSessionDto>> Handle(GetCallByIdQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            return VoiceCallErrors.Unauthorized();
        }

        var currentUserId = _currentUser.UserId.Value;

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        // Hide resource existence from unauthorized users to prevent user enumeration
        if (session is null || (session.CallerUserId != currentUserId && session.ReceiverUserId != currentUserId))
        {
            return VoiceCallErrors.NotFound();
        }

        return CallSessionDto.FromEntity(session);
    }
}
