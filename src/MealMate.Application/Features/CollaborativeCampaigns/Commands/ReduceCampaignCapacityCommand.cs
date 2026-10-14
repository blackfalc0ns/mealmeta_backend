using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CampaignCapacityDto(
    Guid CampaignId,
    Guid RestaurantId,
    DateOnly ServiceDate,
    int MaxBoxes,
    int ReservedBoxes);

public sealed record ReduceCampaignCapacityCommand(
    Guid CampaignId,
    Guid RestaurantId,
    DateOnly ServiceDate,
    int NewMaxBoxes,
    string Reason) : ICommand<CampaignCapacityDto>;

public sealed class ReduceCampaignCapacityCommandHandler
    : ICommandHandler<ReduceCampaignCapacityCommand, CampaignCapacityDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ReduceCampaignCapacityCommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CampaignCapacityDto>> Handle(
        ReduceCampaignCapacityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.NewMaxBoxes < 0)
        {
            return Result.Failure<CampaignCapacityDto>(
                Error.Validation("Campaign.InvalidCapacity", "Capacity cannot be negative."));
        }

        var cap = await _db.CampaignRestaurantCapacities
            .FirstOrDefaultAsync(c => c.CampaignId == request.CampaignId &&
                                      c.RestaurantId == request.RestaurantId &&
                                      c.ServiceDate == request.ServiceDate,
                cancellationToken);

        if (cap == null)
        {
            return Result.Failure<CampaignCapacityDto>(
                Error.NotFound("Campaign.CapacityNotFound",
                    $"No capacity record found for restaurant '{request.RestaurantId}' on date '{request.ServiceDate}'."));
        }

        // Safety Guard Invariant: NewMaxBoxes must not drop below ReservedBoxes
        if (request.NewMaxBoxes < cap.ReservedBoxes)
        {
            return Result.Failure<CampaignCapacityDto>(
                Error.Conflict("Campaign.CapacityReductionBreach",
                    $"Cannot reduce capacity to {request.NewMaxBoxes} because {cap.ReservedBoxes} boxes are already reserved."));
        }

        cap.MaxBoxes = request.NewMaxBoxes;

        // Capacity is part of the approved terms: bump the snapshot version and drop any stored approval
        // once restaurants are locked (after joining closes).
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        if (campaign != null &&
            campaign.Status is CampaignStatus.JoiningClosed or CampaignStatus.UnderReview or CampaignStatus.Scheduled)
        {
            campaign.BumpSnapshotVersion();
        }

        _db.CampaignAuditLogs.Add(new CampaignAuditLog
        {
            CampaignId = request.CampaignId,
            ActorId = "System",
            Action = "CapacityReduced",
            JustificationReason = request.Reason,
            DetailsJson = $"{{\"RestaurantId\":\"{request.RestaurantId}\",\"ServiceDate\":\"{request.ServiceDate:yyyy-MM-dd}\",\"NewMaxBoxes\":{request.NewMaxBoxes},\"SnapshotVersion\":{campaign?.CurrentSnapshotVersion}}}",
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CampaignCapacityDto(
            cap.CampaignId,
            cap.RestaurantId,
            cap.ServiceDate!.Value,
            cap.MaxBoxes,
            cap.ReservedBoxes));
    }
}
