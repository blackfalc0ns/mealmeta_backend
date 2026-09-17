using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record GetCampaignByIdQuery(Guid Id) : IQuery<CampaignDraftDto>;

public sealed class GetCampaignByIdQueryHandler : IQueryHandler<GetCampaignByIdQuery, CampaignDraftDto>
{
    private readonly IAppDbContext _context;

    public GetCampaignByIdQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CampaignDraftDto>> Handle(GetCampaignByIdQuery request, CancellationToken cancellationToken)
    {
        var campaign = await _context.Campaigns
            .AsNoTracking()
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.Id}' was not found.");
        }

        return GetCampaignsListQueryHandler.MapDto(campaign);
    }
}
