using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Audit;

public sealed record AuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid? EntityId,
    string? BeforeJson,
    string? AfterJson,
    string? Reason,
    string? CorrelationId,
    string? SourceChannel,
    DateTime OccurredAtUtc);

public sealed record ListAuditLogsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Action = null,
    string? EntityType = null,
    Guid? ActorUserId = null,
    Guid? EntityId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null) : IQuery<PagedResult<AuditLogDto>>;

public sealed class ListAuditLogsQueryHandler : IQueryHandler<ListAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IAppDbContext _db;

    public ListAuditLogsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            query = query.Where(x => x.Action == request.Action);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            query = query.Where(x => x.EntityType == request.EntityType);
        }

        if (request.ActorUserId is { } actor)
        {
            query = query.Where(x => x.ActorUserId == actor);
        }

        if (request.EntityId is { } entityId)
        {
            query = query.Where(x => x.EntityId == entityId);
        }

        if (request.FromUtc is { } from)
        {
            query = query.Where(x => x.OccurredAtUtc >= from);
        }

        if (request.ToUtc is { } to)
        {
            query = query.Where(x => x.OccurredAtUtc <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(x => new AuditLogDto(
                x.Id, x.ActorUserId, x.Action, x.EntityType, x.EntityId,
                x.BeforeJson, x.AfterJson, x.Reason, x.CorrelationId, x.SourceChannel, x.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetAuditLogQuery(Guid Id) : IQuery<AuditLogDto>;

public sealed class GetAuditLogQueryHandler : IQueryHandler<GetAuditLogQuery, AuditLogDto>
{
    private readonly IAppDbContext _db;

    public GetAuditLogQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<AuditLogDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new AuditLogDto(
                x.Id, x.ActorUserId, x.Action, x.EntityType, x.EntityId,
                x.BeforeJson, x.AfterJson, x.Reason, x.CorrelationId, x.SourceChannel, x.OccurredAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? Error.NotFound("audit.not_found", "Audit log not found.")
            : item;
    }
}

public sealed record ListEntityAuditLogsQuery(string EntityType, Guid EntityId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<AuditLogDto>>;

public sealed class ListEntityAuditLogsQueryHandler : IQueryHandler<ListEntityAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IAppDbContext _db;

    public ListEntityAuditLogsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(ListEntityAuditLogsQuery request, CancellationToken cancellationToken)
    {
        return await new ListAuditLogsQueryHandler(_db).Handle(
            new ListAuditLogsQuery(request.Page, request.PageSize, EntityType: request.EntityType, EntityId: request.EntityId),
            cancellationToken);
    }
}
