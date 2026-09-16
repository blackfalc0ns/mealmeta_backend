using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record CampaignPerformanceMetricsDto(
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    CampaignStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int TotalParticipatingRestaurants,
    int TotalDedicatedDailyCapacity,
    int? MaxSubscribers,
    int CurrentSubscribers,
    decimal CapacityUtilizationRate,
    int TotalCheckoutSessions,
    int CompletedCheckoutSessions,
    int ExpiredCheckoutSessions,
    decimal CheckoutConversionRate,
    decimal TotalCustomerPayments,
    decimal TotalRestaurantPayouts,
    decimal TotalCustomerRefunds,
    decimal NetPlatformMargin,
    string Currency);

public sealed record GetCampaignPerformanceMetricsQuery(Guid CampaignId) : IQuery<CampaignPerformanceMetricsDto>;

public sealed class GetCampaignPerformanceMetricsQueryHandler : IQueryHandler<GetCampaignPerformanceMetricsQuery, CampaignPerformanceMetricsDto>
{
    private readonly IAppDbContext _context;

    public GetCampaignPerformanceMetricsQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CampaignPerformanceMetricsDto>> Handle(
        GetCampaignPerformanceMetricsQuery request,
        CancellationToken cancellationToken)
    {
        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Result.Failure<CampaignPerformanceMetricsDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        var enrollments = await _context.CampaignRestaurantEnrollments
            .Where(e => e.CampaignId == request.CampaignId && e.ParticipationStatus == CampaignRestaurantParticipationStatus.Accepted)
            .ToListAsync(cancellationToken);

        var sessions = await _context.CampaignCheckoutSessions
            .Where(s => s.CampaignId == request.CampaignId)
            .ToListAsync(cancellationToken);

        var ledgerEntries = await _context.CampaignFinancialLedgers
            .Where(l => l.CampaignId == request.CampaignId)
            .ToListAsync(cancellationToken);

        var totalRestaurants = enrollments.Count;
        var totalDedicatedCapacity = enrollments.Sum(e => e.DedicatedDailyCapacity ?? 0);

        var totalSessions = sessions.Count;
        var completedSessions = sessions.Count(s => s.Status == CampaignCheckoutSessionStatus.Completed);
        var expiredSessions = sessions.Count(s => s.Status == CampaignCheckoutSessionStatus.Expired);

        var conversionRate = totalSessions > 0
            ? Math.Round((decimal)completedSessions / totalSessions, 4)
            : 0m;

        var capacityUtilization = (campaign.MaxSubscribers.HasValue && campaign.MaxSubscribers.Value > 0)
            ? Math.Round((decimal)campaign.CurrentSubscribers / campaign.MaxSubscribers.Value, 4)
            : 0m;

        var totalCustomerPayments = ledgerEntries
            .Where(l => l.EntryType == CampaignLedgerEntryType.CustomerPayment)
            .Sum(l => l.Amount);

        var totalRestaurantPayouts = ledgerEntries
            .Where(l => l.EntryType == CampaignLedgerEntryType.RestaurantSettlement)
            .Sum(l => l.Amount);

        var totalCustomerRefunds = ledgerEntries
            .Where(l => l.EntryType == CampaignLedgerEntryType.CustomerRefund)
            .Sum(l => l.Amount);

        var netPlatformMargin = totalCustomerPayments - totalRestaurantPayouts - totalCustomerRefunds;

        var dto = new CampaignPerformanceMetricsDto(
            campaign.Id,
            campaign.NameAr,
            campaign.NameEn,
            campaign.Status,
            campaign.StartsAtUtc,
            campaign.EndsAtUtc,
            totalRestaurants,
            totalDedicatedCapacity,
            campaign.MaxSubscribers,
            campaign.CurrentSubscribers,
            capacityUtilization,
            totalSessions,
            completedSessions,
            expiredSessions,
            conversionRate,
            totalCustomerPayments,
            totalRestaurantPayouts,
            totalCustomerRefunds,
            netPlatformMargin,
            "KWD");

        return Result.Success(dto);
    }
}
