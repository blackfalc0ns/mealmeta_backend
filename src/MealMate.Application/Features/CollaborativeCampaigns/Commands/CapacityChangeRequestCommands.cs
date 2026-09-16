using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record CapacityChangeRequestDto(
    Guid ReviewId,
    Guid CampaignId,
    Guid RestaurantId,
    DateOnly ServiceDate,
    int RequestedMaxBoxes,
    string Status,
    string? Notes);

public sealed record SubmitCapacityChangeRequestCommand(
    Guid CampaignId,
    Guid RestaurantId,
    DateOnly ServiceDate,
    int NewMaxBoxes,
    string Reason) : ICommand<CapacityChangeRequestDto>;

public sealed class SubmitCapacityChangeRequestCommandHandler
    : ICommandHandler<SubmitCapacityChangeRequestCommand, CapacityChangeRequestDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitCapacityChangeRequestCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CapacityChangeRequestDto>> Handle(
        SubmitCapacityChangeRequestCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FindAsync(new object[] { request.CampaignId }, cancellationToken);
        if (campaign == null)
        {
            return Result.Failure<CapacityChangeRequestDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign '{request.CampaignId}' was not found."));
        }

        var metaJson = JsonSerializer.Serialize(new
        {
            request.RestaurantId,
            request.ServiceDate,
            request.NewMaxBoxes,
            request.Reason
        });

        var review = new CampaignCapacityReview
        {
            CampaignId = request.CampaignId,
            ReviewedByUserId = Guid.Empty,
            Notes = metaJson,
            ReviewedAtUtc = _clock.UtcNow,
            Decision = VerificationStatus.UnderReview
        };

        _db.CampaignCapacityReviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CapacityChangeRequestDto(
            review.Id,
            review.CampaignId,
            request.RestaurantId,
            request.ServiceDate,
            request.NewMaxBoxes,
            review.Decision.ToString(),
            request.Reason));
    }
}

public sealed record ReviewCapacityChangeRequestCommand(
    Guid ReviewId,
    bool Approve,
    string? AdminNotes = null) : ICommand<CapacityChangeRequestDto>;

public sealed class ReviewCapacityChangeRequestCommandHandler
    : ICommandHandler<ReviewCapacityChangeRequestCommand, CapacityChangeRequestDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ReviewCapacityChangeRequestCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CapacityChangeRequestDto>> Handle(
        ReviewCapacityChangeRequestCommand request,
        CancellationToken cancellationToken)
    {
        var review = await _db.CampaignCapacityReviews.FindAsync(new object[] { request.ReviewId }, cancellationToken);
        if (review == null)
        {
            return Result.Failure<CapacityChangeRequestDto>(
                Error.NotFound("Campaign.ReviewNotFound", $"Capacity review '{request.ReviewId}' was not found."));
        }

        if (string.IsNullOrWhiteSpace(review.Notes))
        {
            return Result.Failure<CapacityChangeRequestDto>(
                Error.Validation("Campaign.InvalidReview", "Review notes are empty or invalid."));
        }

        var meta = JsonSerializer.Deserialize<CapacityChangePayload>(review.Notes);
        if (meta == null)
        {
            return Result.Failure<CapacityChangeRequestDto>(
                Error.Validation("Campaign.InvalidPayload", "Review payload could not be parsed."));
        }

        var cap = await _db.CampaignRestaurantCapacities
            .FirstOrDefaultAsync(c => c.CampaignId == review.CampaignId &&
                                      c.RestaurantId == meta.RestaurantId &&
                                      c.ServiceDate == meta.ServiceDate,
                cancellationToken);

        if (cap == null)
        {
            return Result.Failure<CapacityChangeRequestDto>(
                Error.NotFound("Campaign.CapacityNotFound", "Campaign restaurant capacity record not found."));
        }

        if (request.Approve)
        {
            if (meta.NewMaxBoxes < cap.ReservedBoxes)
            {
                return Result.Failure<CapacityChangeRequestDto>(
                    Error.Conflict("Campaign.CapacityReductionBreach",
                        $"Cannot reduce capacity to {meta.NewMaxBoxes} because {cap.ReservedBoxes} boxes are already reserved."));
            }

            cap.MaxBoxes = meta.NewMaxBoxes;
            review.Decision = VerificationStatus.Approved;
        }
        else
        {
            review.Decision = VerificationStatus.Rejected;
        }

        review.ReviewedAtUtc = _clock.UtcNow;
        review.Notes = $"{review.Notes} | DecisionNotes: {request.AdminNotes}";

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CapacityChangeRequestDto(
            review.Id,
            review.CampaignId,
            meta.RestaurantId,
            meta.ServiceDate,
            meta.NewMaxBoxes,
            review.Decision.ToString(),
            request.AdminNotes));
    }

    private sealed record CapacityChangePayload(
        Guid RestaurantId,
        DateOnly ServiceDate,
        int NewMaxBoxes,
        string? Reason);
}
