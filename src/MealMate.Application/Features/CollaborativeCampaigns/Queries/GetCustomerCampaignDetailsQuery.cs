using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record GetCustomerCampaignDetailsQuery(
    Guid CustomerId,
    Guid CampaignId,
    Guid? BundleId = null,
    int? DurationDays = null) : IQuery<CustomerCampaignDetailsDto>;

/// <summary>
/// Customer campaign details: published Active campaign only. A previously published campaign
/// that has ended or been stopped is Gone (410); never-published states are NotFound.
/// Blockers reuse the shared evaluation source; an explicit exhausted option is a Conflict.
/// </summary>
public sealed class GetCustomerCampaignDetailsQueryHandler : IQueryHandler<GetCustomerCampaignDetailsQuery, CustomerCampaignDetailsDto>
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerCampaignEvaluationService _evaluation;
    private readonly CustomerCampaignOptionProjector _projector;

    public GetCustomerCampaignDetailsQueryHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICustomerCampaignEvaluationService evaluation,
        CustomerCampaignOptionProjector projector)
    {
        _context = context;
        _clock = clock;
        _evaluation = evaluation;
        _projector = projector;
    }

    public async Task<Result<CustomerCampaignDetailsDto>> Handle(
        GetCustomerCampaignDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var campaign = await _context.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);
        if (campaign is null
            || campaign.Status is not (CampaignStatus.Active or CampaignStatus.Stopped or CampaignStatus.Ended))
        {
            // Draft/scheduled/review states were never publicly visible.
            return Result.Failure<CustomerCampaignDetailsDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        if (campaign.Status is CampaignStatus.Stopped or CampaignStatus.Ended)
        {
            return Result.Failure<CustomerCampaignDetailsDto>(
                Error.Gone("Campaign.NoLongerAvailable", "This campaign is no longer available."));
        }

        var participantIdsByCampaign = await _projector.LoadApprovedParticipantIdsAsync(
            new[] { campaign.Id }, cancellationToken);
        var participantIds = participantIdsByCampaign.GetValueOrDefault(campaign.Id) ?? Array.Empty<Guid>();

        var projection = participantIds.Count > 0
            ? await _projector.ProjectAsync(campaign, participantIds, now, cancellationToken)
            : CustomerCampaignOptionsProjection.Empty;

        // Shared evaluation source: campaign-level and profile-level blockers per option.
        var blocks = new Dictionary<string, CustomerCampaignBlock>(StringComparer.Ordinal);
        if (projection.Options.Count > 0)
        {
            foreach (var option in projection.Options)
            {
                var evaluation = await _evaluation.EvaluateAsync(
                    new CustomerCampaignEvaluationRequest(
                        campaign.Id, option.CampaignBundleId, option.DurationDays, request.CustomerId),
                    cancellationToken);
                if (evaluation.IsFailure)
                {
                    return Result.Failure<CustomerCampaignDetailsDto>(evaluation.Error!);
                }

                foreach (var block in evaluation.Value.Blocks)
                {
                    blocks.TryAdd(block.Code, block);
                }
            }
        }

        var reservedSlots = await _context.CampaignCheckoutSessions.AsNoTracking()
            .Where(s => s.CampaignId == campaign.Id
                && (s.Status == CampaignCheckoutSessionStatus.Pending || s.Status == CampaignCheckoutSessionStatus.PaymentPending)
                && s.ExpiresAtUtc > now)
            .CountAsync(cancellationToken);

        var remainingSlots = campaign.MaxSubscribers is { } max
            ? Math.Max(0, max - campaign.CurrentSubscribers - reservedSlots)
            : (int?)null;
        if (remainingSlots is 0)
        {
            blocks.TryAdd("campaign.capacity_full", new CustomerCampaignBlock(
                "campaign.capacity_full",
                "اكتمل عدد المشتركين في هذه الحملة.",
                "This campaign has reached its subscriber limit.",
                "browse_campaigns"));
        }
        else if (projection.Options.Count > 0 && projection.Options.All(o => !o.FullSequenceAvailable))
        {
            blocks.TryAdd("campaign.no_available_capacity", new CustomerCampaignBlock(
                "campaign.no_available_capacity",
                "لا تتوفر طاقة كافية لتغطية مدة الاشتراك حاليًا.",
                "There is not enough capacity to cover the requested subscription duration right now.",
                "browse_campaigns"));
        }

        // Explicit option request surfaces a 409 when that specific option is exhausted.
        if (request.BundleId.HasValue || request.DurationDays.HasValue)
        {
            if (!request.BundleId.HasValue || !request.DurationDays.HasValue)
            {
                return Result.Failure<CustomerCampaignDetailsDto>(
                    Error.Validation("Campaign.OptionIncomplete", "Provide both bundleId and durationDays to address a specific option."));
            }

            var option = projection.Options.FirstOrDefault(o =>
                o.CampaignBundleId == request.BundleId.Value && o.DurationDays == request.DurationDays.Value);
            if (option is null)
            {
                return Result.Failure<CustomerCampaignDetailsDto>(
                    Error.NotFound("Campaign.OptionNotFound", "The requested campaign option does not exist for this campaign."));
            }

            if (!option.FullSequenceAvailable)
            {
                return Result.Failure<CustomerCampaignDetailsDto>(
                    Error.Conflict("Campaign.OptionCapacityUnavailable",
                        "The requested campaign option no longer has enough capacity for a full duration."));
            }
        }

        var canCheckout = blocks.Count == 0 && projection.Options.Any(o => o.FullSequenceAvailable);

        return Result.Success(new CustomerCampaignDetailsDto(
            campaign.Id,
            campaign.NameAr,
            campaign.NameEn,
            null,
            null,
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
            now,
            canCheckout,
            remainingSlots,
            blocks.Values.OrderBy(b => b.Code, StringComparer.Ordinal).ToList(),
            projection.Options));
    }
}
