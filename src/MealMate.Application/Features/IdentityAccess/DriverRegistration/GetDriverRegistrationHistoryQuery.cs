using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record DriverRegistrationHistoryDto(
    Guid Id,
    Guid? ActorUserId,
    string? ActorFullName,
    string? ActorEmail,
    string Action,
    string? BeforeJson,
    string? AfterJson,
    string? Reason,
    string? SourceChannel,
    DateTime OccurredAtUtc);

public sealed record GetDriverRegistrationHistoryQuery(
    Guid RegistrationId,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<DriverRegistrationHistoryDto>>;

public sealed class GetDriverRegistrationHistoryQueryHandler
    : IQueryHandler<GetDriverRegistrationHistoryQuery, PagedResult<DriverRegistrationHistoryDto>>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;

    public GetDriverRegistrationHistoryQueryHandler(IAppDbContext db, IUserAccountService users)
    {
        _db = db;
        _users = users;
    }

    public async Task<Result<PagedResult<DriverRegistrationHistoryDto>>> Handle(
        GetDriverRegistrationHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var registrationExists = await _db.DriverRegistrations
            .AsNoTracking()
            .AnyAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (!registrationExists)
        {
            return Error.NotFound("DriverRegistration.NotFound", "Driver registration not found.");
        }

        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var query = _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "DriverRegistration" && a.EntityId == request.RegistrationId)
            .OrderByDescending(a => a.OccurredAtUtc);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var actorUserIds = items
            .Where(i => i.ActorUserId.HasValue)
            .Select(i => i.ActorUserId!.Value)
            .Distinct()
            .ToArray();

        var identities = actorUserIds.Length > 0
            ? await _users.GetUserIdentitiesAsync(actorUserIds, cancellationToken)
            : Array.Empty<UserIdentityDto>();

        var userMap = identities.ToDictionary(u => u.Id);

        var dtos = items.Select(item =>
        {
            UserIdentityDto? actor = null;
            if (item.ActorUserId.HasValue)
            {
                userMap.TryGetValue(item.ActorUserId.Value, out actor);
            }

            return new DriverRegistrationHistoryDto(
                item.Id,
                item.ActorUserId,
                actor?.FullName,
                actor?.Email,
                item.Action,
                item.BeforeJson,
                item.AfterJson,
                item.Reason,
                item.SourceChannel,
                item.OccurredAtUtc);
        }).ToList();

        return new PagedResult<DriverRegistrationHistoryDto>(
            dtos,
            total,
            page.NormalizedPage,
            page.NormalizedPageSize);
    }
}
