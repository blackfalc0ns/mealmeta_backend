using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

public sealed record UpdateCampaignDraftCommand(
    Guid CampaignId,
    string NameAr,
    string NameEn,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal TotalDiscountRate,
    decimal MealMateShareRate,
    decimal RestaurantShareRate,
    CampaignFundingMode FundingMode = CampaignFundingMode.Shared,
    int? MaxSubscribers = null,
    DateTime? EnrollmentOpensAtUtc = null,
    DateTime? EnrollmentClosesAtUtc = null,
    List<Guid>? ProgramIds = null,
    List<Guid>? BundleIds = null) : ICommand<CampaignDraftDto>;

public sealed class UpdateCampaignDraftCommandHandler : ICommandHandler<UpdateCampaignDraftCommand, CampaignDraftDto>
{
    private readonly IAppDbContext _context;

    public UpdateCampaignDraftCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CampaignDraftDto>> Handle(
        UpdateCampaignDraftCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Programs)
            .Include(c => c.Bundles)
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found.");
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            return Error.Validation("Campaign.NotDraft", "Only campaigns in Draft status can be edited.");
        }

        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
        {
            return Error.Validation("Campaign.NameRequired", "Campaign NameAr and NameEn are required.");
        }

        if (request.StartsAtUtc >= request.EndsAtUtc)
        {
            return Error.Validation("Campaign.InvalidDates", "StartsAtUtc must be before EndsAtUtc.");
        }

        if (request.EnrollmentOpensAtUtc.HasValue && request.EnrollmentClosesAtUtc.HasValue)
        {
            if (request.EnrollmentOpensAtUtc.Value >= request.EnrollmentClosesAtUtc.Value)
            {
                return Error.Validation("Campaign.InvalidEnrollmentDates", "EnrollmentOpensAtUtc must be before EnrollmentClosesAtUtc.");
            }

            if (request.EnrollmentClosesAtUtc.Value > request.StartsAtUtc)
            {
                return Error.Validation("Campaign.EnrollmentClosesAfterStarts", "EnrollmentClosesAtUtc must be before or equal to StartsAtUtc.");
            }
        }

        if (request.TotalDiscountRate <= 0 || request.TotalDiscountRate > 1.0m ||
            request.MealMateShareRate < 0 || request.RestaurantShareRate < 0)
        {
            return Error.Validation("Campaign.InvalidDiscountRates", "Discount rates must be non-negative and not exceed 100%.");
        }

        if (Math.Abs(request.TotalDiscountRate - (request.MealMateShareRate + request.RestaurantShareRate)) > 0.0001m)
        {
            return Error.Validation("Campaign.DiscountSplitMismatch", "TotalDiscountRate must equal the sum of MealMateShareRate and RestaurantShareRate.");
        }

        if (request.MaxSubscribers.HasValue && request.MaxSubscribers.Value <= 0)
        {
            return Error.Validation("Campaign.InvalidMaxSubscribers", "MaxSubscribers must be greater than zero.");
        }

        campaign.NameAr = request.NameAr.Trim();
        campaign.NameEn = request.NameEn.Trim();
        campaign.StartsAtUtc = request.StartsAtUtc;
        campaign.EndsAtUtc = request.EndsAtUtc;
        campaign.TotalDiscountRate = Percentage.FromFraction(request.TotalDiscountRate);
        campaign.MealMateShareRate = Percentage.FromFraction(request.MealMateShareRate);
        campaign.RestaurantShareRate = Percentage.FromFraction(request.RestaurantShareRate);
        campaign.FundingMode = request.FundingMode;
        campaign.MaxSubscribers = request.MaxSubscribers;
        campaign.EnrollmentOpensAtUtc = request.EnrollmentOpensAtUtc;
        campaign.EnrollmentClosesAtUtc = request.EnrollmentClosesAtUtc;

        if (request.ProgramIds != null)
        {
            campaign.Programs.Clear();
            foreach (var programId in request.ProgramIds.Distinct())
            {
                campaign.Programs.Add(new CampaignProgram
                {
                    Campaign = campaign,
                    ProgramId = programId
                });
            }
        }

        if (request.BundleIds != null)
        {
            campaign.Bundles.Clear();
            foreach (var bundleId in request.BundleIds.Distinct())
            {
                campaign.Bundles.Add(new CampaignBundle
                {
                    Campaign = campaign,
                    BundleId = bundleId
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var dto = new CampaignDraftDto(
            campaign.Id,
            campaign.NameAr,
            campaign.NameEn,
            campaign.Status,
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
            campaign.TotalDiscountRate.Value,
            campaign.MealMateShareRate.Value,
            campaign.RestaurantShareRate.Value,
            campaign.FundingMode,
            campaign.MaxSubscribers,
            campaign.EnrollmentOpensAtUtc,
            campaign.EnrollmentClosesAtUtc,
            campaign.Programs.Select(p => p.ProgramId).ToList(),
            campaign.Bundles.Select(b => b.BundleId).ToList());

        return dto;
    }
}
