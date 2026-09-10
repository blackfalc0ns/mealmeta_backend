using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

// ==========================================
// DTOs
// ==========================================

public sealed record InfluencerKpisDto(
    int RegisteredCustomers,
    int RegisteredCustomersDiff,
    int CompletedSubscriptions,
    int CompletedSubscriptionsDiff,
    int LinkClicks,
    int LinkClicksDiff,
    decimal ConversionRate,
    decimal ConversionRateDiff);

public sealed record InfluencerRecentActivityDto(
    string ActivityId,
    string ActivityCode, // "subscription_purchased" | "subscription_renewed"
    Guid? ReferenceId,
    CurrencyAmountDto CommissionAmount,
    DateTime OccurredAtUtc);

public sealed record InfluencerHomeDashboardDto(
    CurrencyAmountDto TotalCommissions,
    CurrencyAmountDto ThisWeekCommissionsDiff,
    InfluencerKpisDto Kpis,
    string InfluencerCode,
    IReadOnlyList<InfluencerRecentActivityDto> RecentActivities);

public sealed record EarningsChartPointDto(DateTime DateUtc, decimal Amount);

public sealed record EarningsBarPointDto(
    int PeriodIndex,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    decimal Amount,
    int SubscriptionsCount);

public sealed record TopPackagePerformanceDto(
    Guid BundleId,
    string NameAr,
    string NameEn,
    decimal TotalRevenue,
    int OrderCount);

public sealed record InfluencerAnalyticsDto(
    string Period,
    CurrencyAmountDto TotalEarnings,
    IReadOnlyList<EarningsChartPointDto> EarningsChart,
    IReadOnlyList<EarningsBarPointDto> EarningsBars,
    InfluencerKpisDto OverviewKpis,
    IReadOnlyList<TopPackagePerformanceDto> TopPackages);

// ==========================================
// Queries
// ==========================================

public sealed record GetInfluencerHomeDashboardQuery() : IQuery<InfluencerHomeDashboardDto>;

public sealed record GetInfluencerAnalyticsQuery(string Period) : IQuery<InfluencerAnalyticsDto>;
