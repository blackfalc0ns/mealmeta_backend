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
    string ActivityCode, // "subscription_purchased" | "subscription_renewed" | "withdrawal_requested" | "withdrawal_completed"
    Guid? ReferenceId,
    CurrencyAmountDto CommissionAmount,
    DateTime OccurredAtUtc);

public sealed record InfluencerHomeDashboardDto(
    CurrencyAmountDto TotalCommissions,
    CurrencyAmountDto ThisWeekCommissionsDiff,
    InfluencerKpisDto Kpis,
    string InfluencerCode,
    string? AvatarUrl,
    string CurrencyCode,
    string ReferralLink,
    string QrCodeData,
    InfluencerBankInfoDto? BankInfo,
    InfluencerWalletDto? Wallet,
    IReadOnlyList<InfluencerRecentActivityDto> RecentActivities);

public sealed record EarningsChartPointDto(DateTime DateUtc, decimal Amount, int Clicks = 0, int Orders = 0);

public sealed record EarningsBarPointDto(
    int PeriodIndex,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    decimal Amount,
    int SubscriptionsCount);

public sealed record TopPackagePerformanceDto(
    Guid ProgramId,
    Guid BundleId,
    string NameAr,
    string NameEn,
    decimal TotalRevenue,
    int OrderCount,
    decimal TotalCommission);

public sealed record AnalyticsComparisonDto(
    decimal ClicksChangePercentage,
    decimal OrdersChangePercentage,
    decimal NetCommissionChangePercentage);

public sealed record InfluencerAnalyticsDto(
    string Period,
    string CurrencyCode,
    string Granularity,
    CurrencyAmountDto TotalEarnings,
    AnalyticsComparisonDto Comparison,
    IReadOnlyList<EarningsChartPointDto> EarningsChart,
    IReadOnlyList<EarningsBarPointDto> EarningsBars,
    InfluencerKpisDto OverviewKpis,
    IReadOnlyList<TopPackagePerformanceDto> TopPackages);

// ==========================================
// Queries
// ==========================================

public sealed record GetInfluencerHomeDashboardQuery() : IQuery<InfluencerHomeDashboardDto>;

public sealed record GetInfluencerAnalyticsQuery(string Period) : IQuery<InfluencerAnalyticsDto>;

public sealed record GetInfluencerActivitiesQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResultDto<InfluencerRecentActivityDto>>;
