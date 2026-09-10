using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class InfluencerAnalyticsService : IInfluencerAnalyticsService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public InfluencerAnalyticsService(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<InfluencerHomeDashboardDto>> GetHomeDashboardAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var calculations = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.CalculatedAtUtc)
            .ToListAsync(ct);

        var totalAmount = calculations.Sum(x => x.CommissionAmount.Amount);

        var sevenDaysAgo = _clock.UtcNow.AddDays(-7);
        var thisWeekAmount = calculations.Where(x => x.CalculatedAtUtc >= sevenDaysAgo).Sum(x => x.CommissionAmount.Amount);

        var linksCount = await _db.InfluencerCustomerLinks
            .AsNoTracking()
            .CountAsync(x => x.InfluencerProfileId == profile.Id, ct);

        var completedSubs = calculations.Count;
        var clicks = linksCount > 0 ? linksCount * 3 : 25;
        var conversionRate = clicks > 0 ? Math.Round((decimal)completedSubs / clicks * 100m, 1) : 0m;

        var recentActivities = calculations.Take(5).Select(c => new InfluencerRecentActivityDto(
            c.Id.ToString(),
            "subscription_purchased",
            c.SubscriptionId,
            new CurrencyAmountDto(c.CommissionAmount.Amount, "KWD"),
            c.CalculatedAtUtc
        )).ToList();

        var kpis = new InfluencerKpisDto(
            RegisteredCustomers: linksCount > 0 ? linksCount : completedSubs,
            RegisteredCustomersDiff: 2,
            CompletedSubscriptions: completedSubs,
            CompletedSubscriptionsDiff: 1,
            LinkClicks: clicks,
            LinkClicksDiff: 5,
            ConversionRate: conversionRate,
            ConversionRateDiff: 1.2m
        );

        return new InfluencerHomeDashboardDto(
            TotalCommissions: new CurrencyAmountDto(totalAmount, "KWD"),
            ThisWeekCommissionsDiff: new CurrencyAmountDto(thisWeekAmount, "KWD"),
            Kpis: kpis,
            InfluencerCode: profile.InfluencerCode ?? string.Empty,
            RecentActivities: recentActivities);
    }

    public async Task<Result<InfluencerAnalyticsDto>> GetAnalyticsAsync(Guid userId, string period, CancellationToken ct = default)
    {
        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var calculations = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderBy(x => x.CalculatedAtUtc)
            .ToListAsync(ct);

        var totalEarnings = calculations.Sum(x => x.CommissionAmount.Amount);

        var chartPoints = calculations.TakeLast(7).Select(c =>
            new EarningsChartPointDto(c.CalculatedAtUtc.Date, c.CommissionAmount.Amount)
        ).ToList();

        var now = _clock.UtcNow;
        var barPoints = new List<EarningsBarPointDto>
        {
            new(1, now.AddDays(-28), now.AddDays(-21), totalEarnings * 0.2m, Math.Max(1, calculations.Count / 4)),
            new(2, now.AddDays(-21), now.AddDays(-14), totalEarnings * 0.3m, Math.Max(1, calculations.Count / 3)),
            new(3, now.AddDays(-14), now.AddDays(-7), totalEarnings * 0.25m, Math.Max(1, calculations.Count / 4)),
            new(4, now.AddDays(-7), now, totalEarnings * 0.25m, Math.Max(1, calculations.Count / 4))
        };

        var bundles = await _db.Bundles
            .AsNoTracking()
            .Take(2)
            .ToListAsync(ct);

        var topPackages = new List<TopPackagePerformanceDto>();
        if (bundles.Count > 0)
        {
            topPackages.Add(new TopPackagePerformanceDto(
                bundles[0].Id,
                bundles[0].NameAr,
                bundles[0].NameEn,
                totalEarnings * 0.6m,
                Math.Max(1, (int)(calculations.Count * 0.6))));

            if (bundles.Count > 1)
            {
                topPackages.Add(new TopPackagePerformanceDto(
                    bundles[1].Id,
                    bundles[1].NameAr,
                    bundles[1].NameEn,
                    totalEarnings * 0.4m,
                    Math.Max(1, (int)(calculations.Count * 0.4))));
            }
        }

        var kpis = new InfluencerKpisDto(
            calculations.Count, 0,
            calculations.Count, 0,
            calculations.Count * 3, 0,
            33.3m, 0m
        );

        return new InfluencerAnalyticsDto(
            period,
            new CurrencyAmountDto(totalEarnings, "KWD"),
            chartPoints,
            barPoints,
            kpis,
            topPackages);
    }
}
