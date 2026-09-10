using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

// ==========================================
// DTOs
// ==========================================

public sealed record InfluencerSocialLinksDto(
    string? Tiktok,
    string? Instagram,
    string? Whatsapp,
    string? Telegram,
    string? Youtube,
    string? Snapchat);

public sealed record InfluencerPartnerProfileDto(
    Guid InfluencerProfileId,
    string DisplayName,
    string InfluencerCode,
    decimal CommissionRate,
    bool IsActive,
    bool HasCompletedSocialOnboarding,
    InfluencerBankInfoDto? BankInfo,
    InfluencerSocialLinksDto SocialLinks);

public sealed record QuickStatsDto(
    int LinkClicks,
    int AffiliatedOrders,
    decimal ConversionRate);

public sealed record InfluencerLinksDto(
    string InfluencerCode,
    string ReferralLink,
    string QrCodeData,
    QuickStatsDto QuickStats);

// ==========================================
// Queries & Commands
// ==========================================

public sealed record GetInfluencerMeQuery() : IQuery<InfluencerPartnerProfileDto>;

public sealed record GetInfluencerLinksQuery() : IQuery<InfluencerLinksDto>;

public sealed record UpdateInfluencerSocialLinksCommand(
    string? Tiktok,
    string? Instagram,
    string? Whatsapp,
    string? Telegram,
    string? Youtube,
    string? Snapchat) : ICommand;
