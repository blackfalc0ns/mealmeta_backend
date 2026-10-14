using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record GetCampaignsListQuery(
    CampaignStatus? Status = null,
    PageRequest? Page = null,
    string? Search = null
) : IQuery<PagedResult<CampaignDraftDto>>;

public sealed class GetCampaignsListQueryHandler : IQueryHandler<GetCampaignsListQuery, PagedResult<CampaignDraftDto>>
{
    private readonly IAppDbContext _context;

    public GetCampaignsListQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PagedResult<CampaignDraftDto>>> Handle(GetCampaignsListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Campaigns
            .AsNoTracking()
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(c => c.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(c => c.NameAr.ToLower().Contains(search) || c.NameEn.ToLower().Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageReq = request.Page ?? new PageRequest();
        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapDto).ToList();
        return new PagedResult<CampaignDraftDto>(dtos, total, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    public static CampaignDraftDto MapDto(Campaign c) => MapDto(c, DateTime.UtcNow);

    public static CampaignDraftDto MapDto(Campaign c, DateTime utcNow)
    {
        var (reviewOverdue, approvalStatus) = CampaignApprovalState.Derive(
            c.Status, c.ReviewStartsAtUtc, c.ReviewEndsAtUtc, c.CurrentSnapshotVersion, c.ApprovedSnapshotVersion, utcNow);

        return new CampaignDraftDto(
            c.Id,
            c.NameAr,
            c.NameEn,
            c.Status,
            c.StartsAtUtc,
            c.EndsAtUtc,
            c.TotalDiscountRate.Value,
            c.MealMateShareRate.Value,
            c.RestaurantShareRate.Value,
            c.FundingMode,
            c.MaxSubscribers,
            c.EnrollmentOpensAtUtc,
            c.EnrollmentClosesAtUtc,
            c.Programs.Select(p => p.ProgramId).ToList(),
            c.Bundles.Select(b => b.BundleId).ToList(),
            c.ReviewStartsAtUtc,
            c.ReviewEndsAtUtc,
            c.CurrentSnapshotVersion,
            c.ApprovedSnapshotVersion,
            c.ApprovedByUserId,
            c.ApprovedAtUtc,
            reviewOverdue,
            approvalStatus);
    }
}
