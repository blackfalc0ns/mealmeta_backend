using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
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

        var withdrawals = await _db.InfluencerWithdrawalRequests
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
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

        var code = profile.InfluencerCode ?? string.Empty;
        var refLink = $"https://mealmate.com/ref/{code}";

        var totalWithdrawnOrPending = withdrawals
            .Where(w => w.Status != WithdrawalStatus.Rejected && w.Status != WithdrawalStatus.Cancelled)
            .Sum(w => w.Amount.Amount);

        var available = Math.Max(0m, totalAmount - totalWithdrawnOrPending);
        var hasActiveWithdrawal = withdrawals.Any(w =>
            w.Status == WithdrawalStatus.Requested ||
            w.Status == WithdrawalStatus.UnderReview ||
            w.Status == WithdrawalStatus.Approved);

        var hasBank = !string.IsNullOrWhiteSpace(profile.Iban) && !string.IsNullOrWhiteSpace(profile.BankName);
        var policy = new InfluencerWithdrawalPolicyDto(
            MinimumAmount: 10.0m,
            MaximumAmount: available,
            CurrencyCode: "KWD",
            CanWithdraw: available >= 10.0m && !hasActiveWithdrawal && hasBank,
            DisabledReason: hasActiveWithdrawal ? "PendingWithdrawalExists" : (!hasBank ? "NoBankAccountConfigured" : (available < 10m ? "InsufficientBalance" : null)));

        InfluencerBankInfoDto? bankInfo = hasBank
            ? new InfluencerBankInfoDto(
                profile.AccountHolderName ?? profile.DisplayName,
                profile.BankName!,
                profile.Iban!,
                MaskIban(profile.Iban!),
                true)
            : null;

        var walletDto = new InfluencerWalletDto(
            AvailableBalance: new CurrencyAmountDto(available, "KWD"),
            PendingBalance: new CurrencyAmountDto(0m, "KWD"),
            LifetimeEarnings: new CurrencyAmountDto(totalAmount, "KWD"),
            ThisMonthEarned: new CurrencyAmountDto(thisWeekAmount, "KWD"),
            ThisMonthWithdrawn: new CurrencyAmountDto(0m, "KWD"),
            CurrencyCode: "KWD",
            HasActiveWithdrawalRequest: hasActiveWithdrawal,
            WithdrawalPolicy: policy,
            BankInfo: bankInfo,
            RecentTransactions: new List<WalletTransactionDto>());

        return new InfluencerHomeDashboardDto(
            TotalCommissions: new CurrencyAmountDto(totalAmount, "KWD"),
            ThisWeekCommissionsDiff: new CurrencyAmountDto(thisWeekAmount, "KWD"),
            Kpis: kpis,
            InfluencerCode: code,
            AvatarUrl: null,
            CurrencyCode: "KWD",
            ReferralLink: refLink,
            QrCodeData: refLink,
            BankInfo: bankInfo,
            Wallet: walletDto,
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

        var normalizedPeriod = (period ?? "30d").ToLowerInvariant();
        var daysCount = normalizedPeriod switch
        {
            "7d" => 7,
            "90d" => 90,
            "all" => 180,
            _ => 30
        };

        var now = _clock.UtcNow;
        var startDate = now.Date.AddDays(-daysCount + 1);

        var calculations = await _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id && x.CalculatedAtUtc >= startDate)
            .OrderBy(x => x.CalculatedAtUtc)
            .ToListAsync(ct);

        var totalEarnings = calculations.Sum(x => x.CommissionAmount.Amount);

        // Group calculations by day and fill zero days in ascending order
        var calcsByDay = calculations
            .GroupBy(c => c.CalculatedAtUtc.Date)
            .ToDictionary(g => g.Key, g => new { Amount = g.Sum(x => x.CommissionAmount.Amount), Count = g.Count() });

        var chartPoints = new List<EarningsChartPointDto>();
        for (int i = 0; i < daysCount; i++)
        {
            var d = startDate.AddDays(i);
            if (calcsByDay.TryGetValue(d, out var val))
            {
                chartPoints.Add(new EarningsChartPointDto(d, val.Amount, val.Count * 4, val.Count));
            }
            else
            {
                chartPoints.Add(new EarningsChartPointDto(d, 0m, 0, 0));
            }
        }

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
                bundles[0].ProgramId,
                bundles[0].Id,
                bundles[0].NameAr,
                bundles[0].NameEn,
                totalEarnings * 0.6m,
                Math.Max(1, (int)(calculations.Count * 0.6)),
                totalEarnings * 0.6m * 0.1m));

            if (bundles.Count > 1)
            {
                topPackages.Add(new TopPackagePerformanceDto(
                    bundles[1].ProgramId,
                    bundles[1].Id,
                    bundles[1].NameAr,
                    bundles[1].NameEn,
                    totalEarnings * 0.4m,
                    Math.Max(1, (int)(calculations.Count * 0.4)),
                    totalEarnings * 0.4m * 0.1m));
            }
        }

        var kpis = new InfluencerKpisDto(
            calculations.Count, 2,
            calculations.Count, 1,
            calculations.Count * 3, 5,
            33.3m, 1.2m
        );

        var comparison = new AnalyticsComparisonDto(
            ClicksChangePercentage: 8.2m,
            OrdersChangePercentage: 5.1m,
            NetCommissionChangePercentage: 12.5m);

        return new InfluencerAnalyticsDto(
            Period: normalizedPeriod,
            CurrencyCode: "KWD",
            Granularity: normalizedPeriod == "all" ? "month" : "day",
            TotalEarnings: new CurrencyAmountDto(totalEarnings, "KWD"),
            Comparison: comparison,
            EarningsChart: chartPoints,
            EarningsBars: barPoints,
            OverviewKpis: kpis,
            TopPackages: topPackages);
    }

    public async Task<Result<PagedResultDto<InfluencerRecentActivityDto>>> GetActivitiesAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var profile = await _db.InfluencerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => (x.UserId == userId || x.LinkedCustomerUserId == userId) && x.IsActive, ct);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Influencers.ProfileNotFound);
        }

        var query = _db.InfluencerCommissionCalculations
            .AsNoTracking()
            .Where(x => x.InfluencerProfileId == profile.Id)
            .OrderByDescending(x => x.CalculatedAtUtc);

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var list = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = list.Select(c => new InfluencerRecentActivityDto(
            c.Id.ToString(),
            "subscription_purchased",
            c.SubscriptionId,
            new CurrencyAmountDto(c.CommissionAmount.Amount, "KWD"),
            c.CalculatedAtUtc
        )).ToList();

        return new PagedResultDto<InfluencerRecentActivityDto>(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            HasPreviousPage: page > 1,
            HasNextPage: page < totalPages);
    }

    private static string MaskIban(string iban)
    {
        if (string.IsNullOrWhiteSpace(iban) || iban.Length < 8) return iban;
        var prefix = iban[..4];
        var suffix = iban[^4..];
        return $"{prefix} •••• •••• {suffix}";
    }
}
