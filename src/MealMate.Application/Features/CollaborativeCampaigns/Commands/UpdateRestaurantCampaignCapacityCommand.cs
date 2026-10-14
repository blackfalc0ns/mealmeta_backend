using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record UpdateRestaurantCampaignCapacityCommand(
    Guid CampaignId,
    Guid RestaurantId,
    int NewDailyCapacity) : ICommand<bool>;

public sealed class UpdateRestaurantCampaignCapacityCommandHandler
    : ICommandHandler<UpdateRestaurantCampaignCapacityCommand, bool>
{
    private readonly IAppDbContext _context;

    public UpdateRestaurantCampaignCapacityCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<bool>> Handle(
        UpdateRestaurantCampaignCapacityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.NewDailyCapacity <= 0)
        {
            return Error.Validation("Campaign.InvalidCapacity", "Dedicated capacity must be greater than zero.");
        }

        var enrollment = await _context.CampaignRestaurantEnrollments
            .FirstOrDefaultAsync(e => e.CampaignId == request.CampaignId &&
                                      e.RestaurantId == request.RestaurantId,
                cancellationToken);

        if (enrollment != null)
        {
            enrollment.DedicatedDailyCapacity = request.NewDailyCapacity;
        }

        // Also update any today/future capacity records if they exist
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var capacities = await _context.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == request.CampaignId &&
                        c.RestaurantId == request.RestaurantId &&
                        c.ServiceDate >= today)
            .ToListAsync(cancellationToken);

        foreach (var cap in capacities)
        {
            if (request.NewDailyCapacity >= cap.ReservedBoxes)
            {
                cap.MaxBoxes = request.NewDailyCapacity;
            }
        }

        // A committed capacity change is a terms change: new snapshot version, approval dropped.
        if (enrollment != null)
        {
            var campaign = await _context.Campaigns
                .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
            if (campaign != null &&
                campaign.Status is Domain.Enums.CampaignStatus.JoiningClosed
                    or Domain.Enums.CampaignStatus.UnderReview
                    or Domain.Enums.CampaignStatus.Scheduled)
            {
                campaign.BumpSnapshotVersion();
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
